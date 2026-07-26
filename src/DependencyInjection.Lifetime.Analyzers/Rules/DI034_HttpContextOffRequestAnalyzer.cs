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
            || !IsFireAndForget(invocation, context.SemanticModel)
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
                && !IsInsideNameOf(memberAccess)
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

            if (node is IdentifierNameSyntax nameOfCandidate && IsInsideNameOf(nameOfCandidate))
            {
                continue;
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

    /// <summary>
    /// <c>nameof(context)</c> binds to the symbol but compiles to a constant string, so it neither
    /// captures nor reads the context.
    /// </summary>
    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (
                current
                is InvocationExpressionSyntax
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

        // A constructed TaskFactory<int> displays as itself, so compare the original definition.
        var containingType = method.ContainingType?.OriginalDefinition.ToDisplayString();

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
    /// <summary>
    /// Whether this <c>Wait</c> blocks until the work actually finishes. Arguments are bound to
    /// their parameters rather than read positionally, so named and reordered arguments classify
    /// correctly: a finite timeout can return with the work still running, and a cancelable token
    /// can abort the wait while the task keeps going.
    /// </summary>
    private static bool WaitsForCompletion(
        MemberAccessExpressionSyntax waitAccess,
        SemanticModel semanticModel
    )
    {
        if (waitAccess.Parent is not InvocationExpressionSyntax waitCall)
        {
            return true;
        }

        var arguments = waitCall.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return true;
        }

        if (semanticModel.GetSymbolInfo(waitCall).Symbol is not IMethodSymbol waitMethod)
        {
            return false;
        }

        foreach (var argument in arguments)
        {
            IParameterSymbol? parameter;
            if (argument.NameColon?.Name.Identifier.ValueText is { } named)
            {
                parameter = waitMethod.Parameters.FirstOrDefault(p => p.Name == named);
            }
            else
            {
                var ordinal = arguments.IndexOf(argument);
                parameter = ordinal < waitMethod.Parameters.Length
                    ? waitMethod.Parameters[ordinal]
                    : null;
            }

            if (parameter is null)
            {
                return false;
            }

            var value = argument.Expression.ToString();
            var proves = parameter.Name switch
            {
                "millisecondsTimeout" or "timeout" => IsInfiniteTimeout(value),
                // A cancelable token can end the wait while the work continues; only a token that
                // can never be cancelled leaves completion as the sole exit.
                "cancellationToken" => value
                    is "CancellationToken.None"
                        or "System.Threading.CancellationToken.None"
                        or "default"
                        or "default(CancellationToken)",
                _ => false,
            };

            if (!proves)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInfiniteTimeout(string argument) =>
        argument
            is "Timeout.Infinite"
                or "System.Threading.Timeout.Infinite"
                or "-1"
                or "Timeout.InfiniteTimeSpan"
                or "System.Threading.Timeout.InfiniteTimeSpan";

    private static bool IsFireAndForget(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        SyntaxNode outermost = invocation;
        while (
            outermost.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression == outermost
        )
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName is "Result" or "GetResult")
            {
                return false;
            }

            // Wait() blocks until the work finishes; Wait(timeout) can return while it is still
            // running. The infinite and cancellation-free overloads block just as the
            // parameterless one does.
            if (memberName == "Wait" && WaitsForCompletion(memberAccess, semanticModel))
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
