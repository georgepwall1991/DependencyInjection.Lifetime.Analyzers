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
/// Analyzer that detects fire-and-forget background work capturing a scope, a scope's provider,
/// or a service resolved from one. A <c>using</c> scope is disposed the moment the starting method
/// returns, while the thread-pool work it started keeps running, so the captured service is used
/// after its scope has been torn down.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI023_FireAndForgetScopeCaptureAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.FireAndForgetScopeCapture);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeExecutableBody(syntaxContext, wellKnownTypes),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.ConstructorDeclaration,
                SyntaxKind.LocalFunctionStatement,
                SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration
            );
        });
    }

    private static void AnalyzeExecutableBody(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes
    )
    {
        if (!ExecutableSyntaxHelper.TryGetExecutableBody(context.Node, out var executableBody))
        {
            return;
        }

        var semanticModel = context.SemanticModel;

        // Only scopes disposed by the enclosing method itself prove the defect: the `using`
        // guarantees teardown at method exit, and thread-pool work started from that method
        // outlives it. A scope handed to someone else may legitimately outlive this frame.
        var scopeLocals = CollectUsingScopeLocals(executableBody, semanticModel, wellKnownTypes);
        if (scopeLocals.Count == 0)
        {
            return;
        }

        var capturedLocals = CollectCapturedLocals(executableBody, semanticModel, scopeLocals);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (
                node is not InvocationExpressionSyntax invocation
                || !IsBackgroundWorkStart(invocation, semanticModel)
                || !IsFireAndForget(invocation)
            )
            {
                continue;
            }

            if (
                !TryGetCapturedScopeReference(
                    invocation,
                    semanticModel,
                    capturedLocals,
                    out var capturedName
                )
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.FireAndForgetScopeCapture,
                    invocation.GetLocation(),
                    capturedName,
                    GetEnclosingMemberName(context.Node)
                )
            );
        }
    }

    /// <summary>
    /// Locals bound to a scope creation and disposed by a <c>using</c> declaration or statement in
    /// this same body. A scope local without a `using` has no proven disposal point here, so it is
    /// DI001's finding rather than this rule's.
    /// </summary>
    private static HashSet<ILocalSymbol> CollectUsingScopeLocals(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes
    )
    {
        var scopeLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            var declaration = node switch
            {
                LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } usingDeclaration =>
                    usingDeclaration.Declaration,
                UsingStatementSyntax { Declaration: { } usingStatementDeclaration } =>
                    usingStatementDeclaration,
                _ => null,
            };

            if (declaration is null)
            {
                continue;
            }

            foreach (var declarator in declaration.Variables)
            {
                if (
                    declarator.Initializer?.Value is not { } initializer
                    || !IsScopeCreation(initializer, semanticModel, wellKnownTypes)
                    || semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local
                )
                {
                    continue;
                }

                scopeLocals.Add(local);
            }
        }

        return scopeLocals;
    }

    private static bool IsScopeCreation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes
    )
    {
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || method.Name is not ("CreateScope" or "CreateAsyncScope")
        )
        {
            return false;
        }

        var returnType = method.ReturnType;
        return wellKnownTypes.IsServiceScope(returnType)
            || wellKnownTypes.IsAsyncServiceScope(returnType);
    }

    /// <summary>
    /// The scope locals themselves plus every local that holds something resolved from one — the
    /// scope's provider (<c>var provider = scope.ServiceProvider;</c>) and any service resolved
    /// through it. Capturing any of them in background work outlives the same disposal.
    /// </summary>
    private static Dictionary<ILocalSymbol, string> CollectCapturedLocals(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeLocals
    )
    {
        var captured = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var scopeLocal in scopeLocals)
        {
            captured[scopeLocal] = scopeLocal.Name;
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (
                node is not VariableDeclaratorSyntax declarator
                || declarator.Initializer?.Value is not { } initializer
                || semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local
                || captured.ContainsKey(local)
            )
            {
                continue;
            }

            if (ReferencesScopeLocal(initializer, semanticModel, scopeLocals))
            {
                captured[local] = local.Name;
            }
        }

        return captured;
    }

    private static bool ReferencesScopeLocal(
        SyntaxNode node,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeLocals
    ) =>
        node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                && scopeLocals.Contains(local)
            );

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
    /// The started task must be thrown away for the defect to exist. An awaited, returned, stored,
    /// or synchronously waited task keeps the frame — and therefore the scope — alive until the
    /// work completes.
    /// </summary>
    private static bool IsFireAndForget(InvocationExpressionSyntax invocation)
    {
        // Task.Run(...).ContinueWith(...) / .Wait() / .GetAwaiter().GetResult() — the chain, not
        // the Task.Run call, decides the lifetime, so classify from the outermost expression.
        SyntaxNode outermost = invocation;
        while (
            outermost.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression == outermost
        )
        {
            if (memberAccess.Name.Identifier.ValueText is "Wait" or "Result" or "GetAwaiter")
            {
                return false;
            }

            outermost = memberAccess.Parent is InvocationExpressionSyntax chained
                ? chained
                : memberAccess;
        }

        return outermost.Parent switch
        {
            // Task.Run(...);
            ExpressionStatementSyntax => true,
            // _ = Task.Run(...);
            AssignmentExpressionSyntax assignment => assignment.Left
                is IdentifierNameSyntax { Identifier.ValueText: "_" }
                && assignment.Parent is ExpressionStatementSyntax,
            _ => false,
        };
    }

    private static bool TryGetCapturedScopeReference(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, string> capturedLocals,
        out string capturedName
    )
    {
        capturedName = string.Empty;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (
                argument.Expression
                is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            )
            {
                continue;
            }

            foreach (
                var identifier in argument
                    .Expression.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
            )
            {
                if (
                    semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                    && capturedLocals.TryGetValue(local, out var name)
                )
                {
                    capturedName = name;
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetEnclosingMemberName(SyntaxNode declaration) =>
        declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
            AccessorDeclarationSyntax accessor => accessor
                .FirstAncestorOrSelf<PropertyDeclarationSyntax>()
                ?.Identifier.ValueText
                ?? "the accessor",
            _ => "the enclosing method",
        };
}
