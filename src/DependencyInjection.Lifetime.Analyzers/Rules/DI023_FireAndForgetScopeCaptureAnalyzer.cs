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
                || !IsFireAndForget(invocation, semanticModel)
            )
            {
                continue;
            }

            if (
                !TryGetCapturedScopeReference(
                    invocation,
                    semanticModel,
                    capturedLocals,
                    scopeLocals,
                    executableBody,
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
    /// Whether <paramref name="local"/> was definitely overwritten with something that is not
    /// scope-derived before <paramref name="captureSite"/> runs. The proof is dominance by
    /// straight-line order: the replacement and the capture must sit in the same block (or the
    /// replacement in an enclosing one), with the replacement first. A replacement on a branch the
    /// capture is not part of proves nothing and keeps the diagnostic.
    /// </summary>
    private static bool WasReplacedBefore(
        ILocalSymbol local,
        SyntaxNode captureSite,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeLocals,
        Dictionary<ILocalSymbol, string> captured
    )
    {
        if (scopeLocals.Contains(local))
        {
            return false;
        }

        // A write earlier in the capture's own statement dominates it too:
        // `StartNew(..., (local = detached, local))`.
        if (captureSite.FirstAncestorOrSelf<StatementSyntax>() is { } captureStatement)
        {
            AssignmentExpressionSyntax? lastPreceding = null;
            foreach (
                var assignment in captureStatement
                    .DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
            )
            {
                if (
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && assignment.Span.End <= captureSite.SpanStart
                    && SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(assignment.Left).Symbol,
                        local
                    )
                    && !IsConditionallyEvaluated(assignment, captureStatement)
                )
                {
                    lastPreceding = assignment;
                }
            }

            if (lastPreceding is not null)
            {
                return !ReferencesTrackedLocal(lastPreceding.Right, semanticModel, captured);
            }
        }

        // Walk outward from the capture. At each block, a preceding sibling statement that
        // replaces the local dominates it.
        for (
            var statement = captureSite.FirstAncestorOrSelf<StatementSyntax>();
            statement is not null;
            statement = statement.Parent?.FirstAncestorOrSelf<StatementSyntax>()
        )
        {
            if (statement.Parent is not BlockSyntax block)
            {
                continue;
            }

            var captureIndex = block.Statements.IndexOf(statement);
            for (var index = captureIndex - 1; index >= 0; index--)
            {
                if (
                    TryGetReplacementOf(
                        block.Statements[index],
                        local,
                        semanticModel,
                        captured,
                        out var replaces
                    )
                )
                {
                    return replaces;
                }
            }

            if (block.Parent == executableBody || statement.Parent == executableBody)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the statement assigns <paramref name="local"/>, and if so whether the assigned value
    /// is detached from the scope. A conditional-access or short-circuited argument
    /// (<c>receiver?.Use(local = other)</c>) may never evaluate, so it decides nothing.
    /// </summary>
    private static bool TryGetReplacementOf(
        StatementSyntax statement,
        ILocalSymbol local,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, string> captured,
        out bool replacesWithDetachedValue
    )
    {
        replacesWithDetachedValue = false;
        var foundAssignment = false;

        foreach (var assignment in statement.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol,
                    local
                )
                || IsConditionallyEvaluated(assignment, statement)
                // An assignment nested inside a further statement (an if body, a loop) belongs to
                // that construct's control flow, not to this statement's straight-line execution.
                || assignment.FirstAncestorOrSelf<StatementSyntax>() != statement
            )
            {
                continue;
            }

            // Keep scanning: `Consume(local = scoped, local = detached)` leaves the LAST write.
            replacesWithDetachedValue = !ReferencesTrackedLocal(
                assignment.Right,
                semanticModel,
                captured
            );
            foundAssignment = true;
        }

        return foundAssignment;
    }

    private static bool IsConditionallyEvaluated(SyntaxNode node, SyntaxNode boundary)
    {
        for (
            var current = node.Parent;
            current is not null && current != boundary;
            current = current.Parent
        )
        {
            // `(service = x)?.Use()` always evaluates its receiver; only the WhenNotNull side is
            // conditional.
            if (
                current.Parent is ConditionalAccessExpressionSyntax conditionalAccess
                && conditionalAccess.WhenNotNull == current
            )
            {
                return true;
            }

            if (
                current
                is ConditionalExpressionSyntax
                    or SwitchExpressionSyntax
                    or SwitchExpressionArmSyntax
                    or BinaryExpressionSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax
            )
            {
                return true;
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
    /// <summary>
    /// Whether the identifier sits anywhere on the left of an assignment — <c>service = x</c> or
    /// <c>node.Child = x</c>. It is read to reach the storage slot, not handed to the task.
    /// </summary>
    private static bool IsAssignmentTarget(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (
                current.Parent is AssignmentExpressionSyntax assignment
                && assignment.Left == current
            )
            {
                return true;
            }

            if (current is StatementSyntax or LambdaExpressionSyntax or ArgumentSyntax)
            {
                break;
            }
        }

        return false;
    }

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
    /// The started task must be thrown away for the defect to exist. An awaited, returned, stored,
    /// or synchronously waited task keeps the frame — and therefore the scope — alive until the
    /// work completes.
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
        // Task.Run(...).ContinueWith(...) / .Wait() / .GetAwaiter().GetResult() — the chain, not
        // the Task.Run call, decides the lifetime, so classify from the outermost expression.
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
        HashSet<ILocalSymbol> scopeLocals,
        SyntaxNode executableBody,
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
                    && !WasReplacedBefore(
                        local,
                        identifier,
                        executableBody,
                        semanticModel,
                        scopeLocals,
                        capturedLocals
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
