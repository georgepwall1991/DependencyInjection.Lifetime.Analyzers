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
/// Analyzer that detects <c>HttpContext</c> reaching fire-and-forget background work. ASP.NET Core
/// recycles the context once the response completes, so work still running after that reads a
/// context whose features, request, and services have already been torn down.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI034_HttpContextOffRequestAnalyzer : DiagnosticAnalyzer
{
    private const string HttpContextMetadataName = "Microsoft.AspNetCore.Http.HttpContext";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.HttpContextUsedOffRequest);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var httpContextType = compilationContext.Compilation.GetTypeByMetadataName(
                HttpContextMetadataName
            );
            if (httpContextType is null)
            {
                return;
            }

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                    AnalyzeBackgroundWork(syntaxContext, httpContextType, wellKnownTypes),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeBackgroundWork(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol httpContextType,
        WellKnownTypes wellKnownTypes
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (
            !IsBackgroundWorkStart(invocation, context.SemanticModel)
            || !IsFireAndForget(invocation)
        )
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (
                argument.Expression
                is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            )
            {
                continue;
            }

            if (
                !TryGetHttpContextReference(
                    argument.Expression,
                    context.SemanticModel,
                    httpContextType,
                    wellKnownTypes,
                    out var reference,
                    out var referenceName
                )
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.HttpContextUsedOffRequest,
                    reference.GetLocation(),
                    referenceName
                )
            );
            return;
        }
    }

    /// <summary>
    /// The first reference inside the background work that reaches an <c>HttpContext</c> — either a
    /// value of that type, or the accessor's <c>HttpContext</c> property read from inside the work.
    /// </summary>
    private static bool TryGetHttpContextReference(
        SyntaxNode backgroundWork,
        SemanticModel semanticModel,
        INamedTypeSymbol httpContextType,
        WellKnownTypes wellKnownTypes,
        out SyntaxNode reference,
        out string referenceName
    )
    {
        reference = backgroundWork;
        referenceName = string.Empty;

        foreach (var node in backgroundWork.DescendantNodes())
        {
            // accessor.HttpContext read inside the work is just as stale: by the time it runs the
            // AsyncLocal holding the context has been cleared or reassigned to another request.
            if (
                node is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.ValueText == "HttpContext"
                && wellKnownTypes.IsHttpContextAccessor(
                    semanticModel.GetTypeInfo(memberAccess.Expression).Type
                )
            )
            {
                reference = memberAccess;
                referenceName = memberAccess.ToString();
                return true;
            }

            if (
                node is not IdentifierNameSyntax identifier
                || semanticModel.GetSymbolInfo(identifier).Symbol
                    is not (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol)
            )
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            var type = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null,
            };

            if (type is not null && InheritsFromOrEquals(type, httpContextType))
            {
                reference = identifier;
                referenceName = identifier.Identifier.ValueText;
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFromOrEquals(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBackgroundWorkStart(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containingType = method.ContainingType?.ToDisplayString();

        return (method.Name == "Run" && containingType == "System.Threading.Tasks.Task")
            || (
                method.Name == "StartNew"
                && containingType
                    is "System.Threading.Tasks.TaskFactory"
                        or "System.Threading.Tasks.TaskFactory<TResult>"
            );
    }

    /// <summary>
    /// The started task must be thrown away. An awaited, returned, stored, or synchronously waited
    /// task keeps the request alive until the work completes, so the context is still valid.
    /// </summary>
    private static bool IsFireAndForget(InvocationExpressionSyntax invocation)
    {
        SyntaxNode outermost = invocation;
        while (
            outermost.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression == outermost
        )
        {
            if (memberAccess.Name.Identifier.ValueText is "Wait" or "Result" or "GetResult")
            {
                return false;
            }

            outermost = memberAccess.Parent is InvocationExpressionSyntax chained
                ? chained
                : memberAccess;
        }

        return outermost.Parent switch
        {
            ExpressionStatementSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Left
                is IdentifierNameSyntax { Identifier.ValueText: "_" }
                && assignment.Parent is ExpressionStatementSyntax,
            _ => false,
        };
    }
}
