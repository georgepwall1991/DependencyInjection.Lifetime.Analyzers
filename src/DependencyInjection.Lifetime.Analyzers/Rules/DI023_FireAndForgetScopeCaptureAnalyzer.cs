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
        var replacementPositions = CollectReplacementPositions(
            executableBody,
            semanticModel,
            scopeLocals,
            capturedLocals
        );

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
                    replacementPositions,
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

        // Scope -> provider -> service is two hops, and nothing bounds how many an ordinary
        // method uses, so grow the set until it stops changing.
        bool addedThisPass;
        do
        {
            addedThisPass = false;

            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
            {
                if (
                    node is not VariableDeclaratorSyntax declarator
                    || declarator.Initializer?.Value is not { } initializer
                    || semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local
                    || captured.ContainsKey(local)
                    || !CanKeepScopeAlive(local.Type)
                    || !CanKeepScopeAlive(semanticModel.GetTypeInfo(initializer).Type)
                )
                {
                    continue;
                }

                if (ReferencesTrackedLocal(initializer, semanticModel, captured))
                {
                    captured[local] = local.Name;
                    addedThisPass = true;
                }
            }
        } while (addedThisPass);

        return captured;
    }

    /// <summary>
    /// The position at which each tracked local is *definitely* overwritten with something that
    /// is not scope-derived. Work started after that point captured the replacement, not the
    /// scoped value. Only unconditional straight-line assignments count: a replacement inside an
    /// if, loop, switch, or try may not run at all, and suppressing on it would hide a real
    /// capture.
    /// </summary>
    private static Dictionary<ILocalSymbol, int> CollectReplacementPositions(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeLocals,
        Dictionary<ILocalSymbol, string> captured
    )
    {
        var replacedAt = new Dictionary<ILocalSymbol, int>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (
                node is not AssignmentExpressionSyntax assignment
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || semanticModel.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol assigned
                || scopeLocals.Contains(assigned)
                || !captured.ContainsKey(assigned)
                || ReferencesTrackedLocal(assignment.Right, semanticModel, captured)
            )
            {
                continue;
            }

            if (!IsUnconditionalIn(assignment, executableBody))
            {
                continue;
            }

            if (
                !replacedAt.TryGetValue(assigned, out var existing)
                || assignment.SpanStart < existing
            )
            {
                replacedAt[assigned] = assignment.SpanStart;
            }
        }

        return replacedAt;
    }

    /// <summary>
    /// Whether the node runs exactly once on every path through the body — no conditional, loop,
    /// switch, or exception-handling construct stands between it and the body itself.
    /// </summary>
    private static bool IsUnconditionalIn(SyntaxNode node, SyntaxNode executableBody)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current == executableBody)
            {
                return true;
            }

            if (
                current
                is IfStatementSyntax
                    or SwitchStatementSyntax
                    or SwitchExpressionSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or ForStatementSyntax
                    or ForEachStatementSyntax
                    or TryStatementSyntax
                    or ConditionalExpressionSyntax
                    or BinaryExpressionSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax
                    or LocalFunctionStatementSyntax
            )
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a value of this type could still hold the scope's graph. A primitive, enum, or
    /// string computed FROM a scope (<c>scope.GetHashCode()</c>) keeps nothing alive.
    /// </summary>
    private static bool CanKeepScopeAlive(ITypeSymbol? type) =>
        type is not null
        && type.SpecialType
            is not (
                SpecialType.System_String
                or SpecialType.System_Boolean
                or SpecialType.System_Char
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Decimal
                or SpecialType.System_DateTime
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
            )
        && type.TypeKind is not TypeKind.Enum;

    private static bool ReferencesTrackedLocal(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, string> trackedLocals
    ) =>
        node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => !IsInsideNameOf(identifier))
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                && trackedLocals.ContainsKey(local)
            );

    /// <summary>
    /// <c>nameof(service)</c> binds to the local but compiles to a constant string, so it captures
    /// nothing at runtime.
    /// </summary>
    private static bool IsAssignmentTarget(SyntaxNode node) =>
        node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (
                current
                is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                }
            )
            {
                return true;
            }

            if (current is StatementSyntax or LambdaExpressionSyntax)
            {
                break;
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
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName is "Wait" or "Result" or "GetResult")
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
        Dictionary<ILocalSymbol, int> replacementPositions,
        out string capturedName
    )
    {
        capturedName = string.Empty;

        // Inline lambdas, anonymous methods, method groups on a scoped service, and delegate
        // locals all reach the background work the same way: through an argument.
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            // A state argument is evaluated synchronously; if its value cannot hold the scope
            // graph (a boxed int, a string, `service.Id`), the started work retains nothing from
            // the scope. A method group has no type of its own, so it stays in.
            var argumentType = semanticModel.GetTypeInfo(argument.Expression).Type;
            if (argumentType is not null && !CanKeepScopeAlive(argumentType))
            {
                continue;
            }

            foreach (
                var identifier in argument
                    .Expression.DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
            )
            {
                if (IsInsideNameOf(identifier) || IsAssignmentTarget(identifier))
                {
                    continue;
                }

                if (
                    semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                    && capturedLocals.TryGetValue(local, out var name)
                    && !(
                        replacementPositions.TryGetValue(local, out var replacedAt)
                        && replacedAt < identifier.SpanStart
                    )
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
