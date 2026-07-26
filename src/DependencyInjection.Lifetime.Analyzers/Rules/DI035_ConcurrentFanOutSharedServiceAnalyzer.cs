using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects one non-thread-safe service shared by every task of a
/// <c>Task.WhenAll</c> fan-out. The tasks run at the same time on the same instance, which for an
/// EF Core <c>DbContext</c> is the "second operation started on this context" crash.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI035_ConcurrentFanOutSharedServiceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ConcurrentFanOutSharedService);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeWhenAll, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeWhenAll(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsTaskWhenAll(invocation, context.SemanticModel))
        {
            return;
        }

        // The projection that produces the tasks — `items.Select(x => ...)` — is where the shared
        // instance is captured. Each element's lambda runs concurrently with all the others.
        foreach (var projection in EnumerateTaskProjections(invocation))
        {
            if (
                !TryGetSharedNonThreadSafeCapture(
                    projection,
                    context.SemanticModel,
                    out var capture,
                    out var captureName,
                    out var catalogName
                )
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.ConcurrentFanOutSharedService,
                    capture.GetLocation(),
                    captureName,
                    catalogName
                )
            );
            return;
        }
    }

    private static bool IsTaskWhenAll(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    ) =>
        semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "WhenAll" } method
        && method.ContainingType?.OriginalDefinition.ToDisplayString()
            == "System.Threading.Tasks.Task";

    /// <summary>
    /// The lambdas whose bodies become the concurrent tasks: the selector of a
    /// <c>Select</c>/<c>SelectMany</c> that feeds WhenAll, or a lambda handed to WhenAll directly.
    /// A <c>Where</c> predicate or any other lambda in the chain runs during enumeration, one at a
    /// time, so it is not a concurrent body — and a lambda nested inside a selector belongs to that
    /// selector's single task rather than being a fan-out of its own.
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateTaskProjections(
        InvocationExpressionSyntax whenAll
    )
    {
        foreach (var argument in whenAll.ArgumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax directLambda)
            {
                yield return directLambda;
                continue;
            }

            foreach (var selector in EnumerateSelectorLambdas(argument.Expression))
            {
                yield return selector;
            }
        }
    }

    private static IEnumerable<SyntaxNode> EnumerateSelectorLambdas(ExpressionSyntax expression)
    {
        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess
                || memberAccess.Name.Identifier.ValueText is not ("Select" or "SelectMany")
            )
            {
                continue;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LambdaExpressionSyntax selector)
                {
                    yield return selector;
                }
            }
        }
    }

    /// <summary>
    /// A reference inside the concurrent body to a non-thread-safe value declared OUTSIDE it — a
    /// field, a captured local, or an enclosing parameter. A value created inside the body belongs
    /// to that single task and is not shared.
    /// </summary>
    private static bool TryGetSharedNonThreadSafeCapture(
        SyntaxNode concurrentBody,
        SemanticModel semanticModel,
        out SyntaxNode capture,
        out string captureName,
        out string catalogName
    )
    {
        capture = concurrentBody;
        captureName = string.Empty;
        catalogName = string.Empty;

        foreach (var identifier in concurrentBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (IsInsideNameOf(identifier))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            // Properties are deliberately absent: a computed property can hand back a fresh
            // instance per access (`AppDbContext Db => new()`), which is not sharing at all.
            ITypeSymbol? type = symbol switch
            {
                IFieldSymbol field => field.Type,
                ILocalSymbol local when IsDeclaredOutside(local, concurrentBody) => local.Type,
                IParameterSymbol parameter when IsDeclaredOutside(parameter, concurrentBody) =>
                    parameter.Type,
                _ => null,
            };

            if (MatchNonThreadSafeCatalog(type) is not { } match)
            {
                continue;
            }

            capture = identifier;
            captureName = identifier.Identifier.ValueText;
            catalogName = match;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the symbol is declared outside the concurrent body, which is what makes every task
    /// share it. A local or lambda parameter declared inside belongs to one task only.
    /// </summary>
    private static bool IsDeclaredOutside(ISymbol symbol, SyntaxNode concurrentBody) =>
        symbol.DeclaringSyntaxReferences.Length > 0
        && symbol.DeclaringSyntaxReferences.All(reference =>
            !concurrentBody.Span.Contains(reference.Span)
        );

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (
                current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                }
            )
            {
                return true;
            }

            if (current is StatementSyntax)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// The documented non-thread-safe types, matched the same way DI021 matches them so both rules
    /// speak about the same catalog.
    /// </summary>
    private static string? MatchNonThreadSafeCatalog(ITypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (MatchCatalogEntry(current) is { } baseMatch)
            {
                return baseMatch;
            }
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (MatchCatalogEntry(implemented) is { } interfaceMatch)
            {
                return interfaceMatch;
            }
        }

        return null;
    }

    private static string? MatchCatalogEntry(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return (ns, type.Name) switch
        {
            ("Microsoft.EntityFrameworkCore", "DbContext") => "DbContext",
            ("Microsoft.EntityFrameworkCore.Storage", "IDbContextTransaction") =>
                "IDbContextTransaction",
            ("System.Data.Common", "DbConnection") => "DbConnection",
            ("System.Data.Common", "DbCommand") => "DbCommand",
            ("System.Data.Common", "DbTransaction") => "DbTransaction",
            ("System.Data.Common", "DbDataReader") => "DbDataReader",
            ("System.Data", "IDbConnection") => "IDbConnection",
            ("System.Data", "IDbCommand") => "IDbCommand",
            ("System.Data", "IDbTransaction") => "IDbTransaction",
            ("System.Data", "IDataReader") => "IDataReader",
            _ => null,
        };
    }
}
