using System.Collections.Concurrent;
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
/// Analyzer that detects a task started on a service resolved from a <c>using</c> scope and then
/// allowed to leave that scope un-awaited. Disposing a scope disposes every scoped service it
/// created, so work still running when the scope ends operates on torn-down services.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI037_UnawaitedTaskEscapesScopeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Members that consume a task where it stands: awaiting it, or blocking until it finishes.
    /// Either way the work is over before the scope is.
    /// </summary>
    private static readonly ImmutableHashSet<string> BlockingConsumerNames =
        ImmutableHashSet.Create(
            "Wait",
            "GetAwaiter",
            "Result",
            "WaitAsync",
            // Reading completion is how hand-rolled synchronisation waits: a caller polling
            // `IsCompleted` before leaving the scope is waiting for the work, not forgetting it.
            "IsCompleted",
            "IsCompletedSuccessfully",
            "Status"
        );

    /// <summary>
    /// Members that hand back the same pending work in another wrapper, so what happens to the
    /// wrapper decides the original's fate.
    /// </summary>
    /// <summary>
    /// How deep an alias chain from a resolution to the receiver is followed before the service
    /// type is left unknown.
    /// </summary>
    private const int MaxResolutionDepth = 8;

    /// <summary>
    /// How far the constructor graph of a resolved service is followed while asking whether the
    /// scope has anything to dispose.
    /// </summary>
    private const int MaxDependencyDepth = 6;

    /// <summary>
    /// How far a service's own helpers are followed while asking whether it keeps the task it
    /// hands back.
    /// </summary>
    private const int MaxHelperDepth = 3;

    /// <summary>
    /// Generic resolution methods whose type argument names the service that was resolved.
    /// </summary>
    private static readonly ImmutableHashSet<string> ResolutionMethodNames =
        ImmutableHashSet.Create(
            "GetRequiredService",
            "GetService",
            "GetRequiredKeyedService",
            "GetKeyedService"
        );

    /// <summary>
    /// Calls that put what they are handed into a store rather than acting on it, so the task is
    /// being kept for after the scope rather than waited on inside it.
    /// </summary>
    /// <summary>
    /// Calls that block until something finishes. A disposal that makes one of these is waiting
    /// for the work rather than pulling the floor out from under it.
    /// </summary>
    private static readonly ImmutableHashSet<string> JoiningCallNames = ImmutableHashSet.Create(
        "Wait",
        "WaitOne",
        "WaitAll",
        "WaitAny",
        "GetResult",
        "Join"
    );

    private static readonly ImmutableHashSet<string> CollectionAddNames = ImmutableHashSet.Create(
        "Add",
        "AddLast",
        "AddRange",
        "Append",
        "Enqueue",
        "Insert",
        "Push",
        "TryAdd"
    );

    private static readonly ImmutableHashSet<string> TaskForwardingNames = ImmutableHashSet.Create(
        "AsTask",
        "ConfigureAwait",
        "Preserve"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UnawaitedTaskEscapesScope);

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

            // Registrations decide whether the scope owns what it resolved: a singleton comes
            // from the root provider and outlives every scope that hands it out.
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var executableRoots = new ConcurrentBag<SyntaxNode>();
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel
                    );

                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel
                    );
                },
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    executableRoots.Add(syntaxContext.Node);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel
                    );
                },
                ExecutableSyntaxHelper.ExecutableRootKinds
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                var lifetimes = new ServiceLifetimeLookup(
                    registrationCollector,
                    new KnownServiceLifetimeClassifier(wellKnownTypes)
                );

                foreach (var executableRoot in executableRoots)
                {
                    if (
                        !semanticModelsByTree.TryGetValue(
                            executableRoot.SyntaxTree,
                            out var semanticModel
                        )
                    )
                    {
                        continue;
                    }

                    AnalyzeExecutableBody(
                        endContext,
                        executableRoot,
                        semanticModel,
                        wellKnownTypes,
                        lifetimes
                    );
                }
            });
        });
    }

    private static void AnalyzeExecutableBody(
        CompilationAnalysisContext context,
        SyntaxNode executableRoot,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ServiceLifetimeLookup lifetimes
    )
    {
        if (!ExecutableSyntaxHelper.TryGetExecutableBody(executableRoot, out var executableBody))
        {
            return;
        }

        // Only a scope this body disposes itself proves the defect: the `using` fixes the moment
        // of teardown. A scope without one has no proven disposal point here, which is DI001's
        // finding rather than this rule's.
        var scopes = CollectUsingScopes(executableBody, semanticModel, wellKnownTypes);
        if (scopes.Count == 0)
        {
            return;
        }

        var reassignedLocals = CollectReassignedLocals(executableBody, semanticModel);

        foreach (var scope in scopes)
        {
            AnalyzeScope(context, semanticModel, scope, reassignedLocals, lifetimes);
        }
    }

    private static void AnalyzeScope(
        CompilationAnalysisContext context,
        SemanticModel semanticModel,
        ScopeRegion scope,
        HashSet<ILocalSymbol> reassignedLocals,
        ServiceLifetimeLookup lifetimes
    )
    {
        if (reassignedLocals.Contains(scope.Local))
        {
            return;
        }

        var scopeDerived = CollectScopeDerivedLocals(scope, semanticModel, reassignedLocals);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(scope.Region))
        {
            if (
                node is not InvocationExpressionSyntax invocation
                || !scope.Region.Span.Contains(invocation.Span)
            )
            {
                continue;
            }

            // A delegate body runs when its consumer chooses, not where it is written, and
            // background work started with Task.Run is DI023's finding.
            if (IsInsideNestedFunction(invocation, scope.Region))
            {
                continue;
            }

            if (
                !IsAwaitableInvocation(invocation, semanticModel)
                || !IsStartedOnScopedService(
                    invocation,
                    semanticModel,
                    scope,
                    scopeDerived,
                    lifetimes
                )
            )
            {
                continue;
            }

            if (
                ReturnsCompletedTask(
                    invocation,
                    semanticModel,
                    GetResolvedServiceType(
                        ((MemberAccessExpressionSyntax)invocation.Expression).Expression,
                        semanticModel,
                        0
                    ),
                    lifetimes
                )
            )
            {
                continue;
            }

            var outcome = ClassifyTaskFate(invocation, semanticModel, scope);

            if (outcome is not { } escape)
            {
                continue;
            }

            if (
                ServiceIsJoinedLater(
                    invocation,
                    ((MemberAccessExpressionSyntax)invocation.Expression).Expression,
                    semanticModel,
                    scope
                )
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnawaitedTaskEscapesScope,
                    invocation.GetLocation(),
                    escape,
                    scope.Local.Name
                )
            );
        }
    }

    /// <summary>
    /// Locals bound to a scope creation and disposed by a <c>using</c> declaration or statement in
    /// this same body, paired with the region that disposal ends.
    /// </summary>
    private static List<ScopeRegion> CollectUsingScopes(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes
    )
    {
        var scopes = new List<ScopeRegion>();

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            var (declaration, region) = node switch
            {
                LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } usingDeclaration =>
                    (usingDeclaration.Declaration, usingDeclaration.Parent),
                UsingStatementSyntax { Declaration: { } statementDeclaration } usingStatement => (
                    statementDeclaration,
                    (SyntaxNode?)usingStatement
                ),
                _ => (null, null),
            };

            if (declaration is null || region is null)
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

                scopes.Add(new ScopeRegion(local, region, declarator));
            }
        }

        return scopes;
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

        return wellKnownTypes.IsServiceScope(method.ReturnType)
            || wellKnownTypes.IsAsyncServiceScope(method.ReturnType);
    }

    /// <summary>
    /// The scope local plus every local that holds something reached from it — its
    /// <c>ServiceProvider</c> and any service resolved through that provider. Each of them dies
    /// with the scope.
    /// </summary>
    private static HashSet<ILocalSymbol> CollectScopeDerivedLocals(
        ScopeRegion scope,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> reassignedLocals
    )
    {
        var derived = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default) { scope.Local };

        // Scope -> provider -> service is two hops, and nothing bounds how many an ordinary
        // method uses, so grow the set until it stops changing.
        bool addedThisPass;
        do
        {
            addedThisPass = false;

            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(scope.Region))
            {
                if (
                    node is not VariableDeclaratorSyntax declarator
                    || declarator == scope.Declarator
                    || declarator.Initializer?.Value is not { } initializer
                    || semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local
                    || derived.Contains(local)
                    || reassignedLocals.Contains(local)
                    || !CanHoldAService(local.Type)
                    || !CanHoldAService(semanticModel.GetTypeInfo(initializer).Type)
                )
                {
                    continue;
                }

                if (ReferencesScopeDerived(initializer, semanticModel, derived))
                {
                    derived.Add(local);
                    addedThisPass = true;
                }
            }
        } while (addedThisPass);

        return derived;
    }

    /// <summary>
    /// Whether a value could be, or could hold, something the scope owns. An id copied out of a
    /// scoped service is a string, and a worker built from that string is nobody's but its own.
    /// </summary>
    private static bool CanHoldAService(ITypeSymbol? type) =>
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

    private static bool ReferencesScopeDerived(
        SyntaxNode node,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> derived
    ) =>
        node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                && derived.Contains(local)
                && CarriesTheScope(identifier, semanticModel)
            );

    /// <summary>
    /// Whether reaching through a scope-derived name yields something that could still be, or
    /// hold, a service. Reading an id off a scoped service hands back a string, and the string
    /// carries nothing of the scope with it.
    /// </summary>
    private static bool CarriesTheScope(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel
    )
    {
        ExpressionSyntax reached = identifier;

        while (true)
        {
            if (
                reached.Parent is MemberAccessExpressionSyntax member
                && member.Expression == reached
            )
            {
                reached = member;
                continue;
            }

            if (
                reached.Parent is InvocationExpressionSyntax call && call.Expression == reached
            )
            {
                reached = call;
                continue;
            }

            break;
        }

        if (!CanHoldAService(semanticModel.GetTypeInfo(reached).Type))
        {
            return false;
        }

        // A call that answers with something it just built hands back nothing of the scope:
        // `exporter.CreateSnapshot()` is a new object, not the exporter.
        return reached is not InvocationExpressionSyntax made
            || semanticModel.GetSymbolInfo(made).Symbol is not IMethodSymbol maker
            || !ReturnsFreshObject(maker);
    }

    /// <summary>
    /// Whether every answer a method gives is an object it constructs on the spot. Such a result
    /// is nobody's but the caller's, whatever it was built from.
    /// </summary>
    private static bool ReturnsFreshObject(IMethodSymbol method)
    {
        if (
            method.DeclaringSyntaxReferences.Length != 1
            || !ExecutableSyntaxHelper.TryGetExecutableBody(
                method.DeclaringSyntaxReferences[0].GetSyntax(),
                out var body
            )
        )
        {
            return false;
        }

        if (body is ExpressionSyntax expressionBody)
        {
            return expressionBody is ObjectCreationExpressionSyntax
                or ImplicitObjectCreationExpressionSyntax;
        }

        var returns = ExecutableSyntaxHelper
            .EnumerateSameBoundaryNodes(body)
            .OfType<ReturnStatementSyntax>()
            .ToList();

        return returns.Count > 0
            && returns.All(statement =>
                statement.Expression
                    is ObjectCreationExpressionSyntax
                        or ImplicitObjectCreationExpressionSyntax
            );
    }

    /// <summary>
    /// Whether the invocation hands back pending work: a <c>Task</c>, a <c>ValueTask</c>, or one
    /// of their generic forms. A synchronous call finishes inside the scope by definition.
    /// </summary>
    private static bool IsAwaitableInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return IsTaskLike(method.ReturnType);
    }

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name =
                (current as INamedTypeSymbol)?.ConstructedFrom.ToDisplayString()
                ?? current.ToDisplayString();

            if (
                name
                is "System.Threading.Tasks.Task"
                    or "System.Threading.Tasks.Task<TResult>"
                    or "System.Threading.Tasks.ValueTask"
                    or "System.Threading.Tasks.ValueTask<TResult>"
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the call was made on something the scope owns: a scope-derived local, or a service
    /// resolved from the scope's provider in the same expression.
    /// </summary>
    private static bool IsStartedOnScopedService(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ScopeRegion scope,
        HashSet<ILocalSymbol> scopeDerived,
        ServiceLifetimeLookup lifetimes
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var receiver = memberAccess.Expression;

        // The scope itself is not a service: `scope.Dispose()` and friends are not this rule's
        // business, and its provider only resolves.
        if (
            semanticModel.GetSymbolInfo(receiver).Symbol is ILocalSymbol receiverLocal
            && SymbolEqualityComparer.Default.Equals(receiverLocal, scope.Local)
        )
        {
            return false;
        }

        if (!ReferencesScopeDerived(receiver, semanticModel, scopeDerived))
        {
            return false;
        }

        var serviceType = GetResolvedServiceType(receiver, semanticModel, 0);

        // A singleton resolved through a child scope belongs to the root provider, which no
        // `using` here disposes, so the work has nothing torn down underneath it. Neither does a
        // service the scope's disposal provably leaves alone, nor one whose disposal waits.
        return !lifetimes.IsSingleton(serviceType)
            && !lifetimes.NothingToDispose(serviceType)
            && !DisposalJoinsTheWork(lifetimes.GetImplementationType(serviceType));
    }

    /// <summary>
    /// Whether the method provably hands back a task that has already finished, so there is no
    /// work left to outlive the scope. Only a body visible here and made of nothing but
    /// completed-task returns proves it.
    /// </summary>
    private static bool ReturnsCompletedTask(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol? serviceType,
        ServiceLifetimeLookup lifetimes
    )
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
        {
            return false;
        }

        // The interface says nothing about what runs; the registered implementation does.
        var method =
            called.ContainingType?.TypeKind == TypeKind.Interface
            && lifetimes.GetImplementationType(serviceType) is { } implementation
            && implementation.FindImplementationForInterfaceMember(called) is IMethodSymbol resolved
                ? resolved
                : called;

        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();

        if (
            declaration is not BaseMethodDeclarationSyntax and not AccessorDeclarationSyntax
            || !ExecutableSyntaxHelper.TryGetExecutableBody(declaration, out var body)
        )
        {
            return false;
        }

        var bodyNodes = ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(body).ToList();

        // Work that never touches the service cannot be hurt by the service being disposed:
        // `PauseAsync() => Task.Delay(10)` hands back a timer, not a piece of this instance.
        if (!TouchesInstanceStateWhileRunning(method, body))
        {
            return true;
        }

        // A method that keeps the handle it hands back has a join of its own in mind, so the
        // caller discarding the task is not the end of the story. The keeping may happen a
        // helper or two down, so its own calls on this instance are followed too.
        if (RetainsTheTask(method, 0))
        {
            return true;
        }

        // An async body with nothing to await runs straight through to its end and hands back a
        // task that is already finished.
        if (method.IsAsync)
        {
            return !bodyNodes.Any(node =>
                node is YieldStatementSyntax
                || (node is AwaitExpressionSyntax await && Suspends(await))
            );
        }

        if (body is ExpressionSyntax expressionBody)
        {
            return IsCompletedTaskExpression(expressionBody);
        }

        var returns = bodyNodes.OfType<ReturnStatementSyntax>().ToList();

        return returns.Count > 0
            && returns.All(statement =>
                statement.Expression is { } value && IsCompletedTaskExpression(value)
            );
    }

    /// <summary>
    /// Whether the service's own state is read or written by work that is still running after the
    /// method hands its task back. A method that is not <c>async</c> runs to its end before the
    /// caller sees the task, so what it reads then — `Task.Delay(_delayMs)` — is already read;
    /// only a delegate it leaves behind can touch the instance later. An <c>async</c> body, by
    /// contrast, resumes inside the instance, so every mention of it counts.
    /// </summary>
    private static bool TouchesInstanceStateWhileRunning(IMethodSymbol method, SyntaxNode body)
    {
        var firstSuspension = ExecutableSyntaxHelper
            .EnumerateSameBoundaryNodes(body)
            .OfType<AwaitExpressionSyntax>()
            .Where(Suspends)
            .OrderBy(await => await.SpanStart)
            .FirstOrDefault();

        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (!IsInstanceStateReference(node, method.ContainingType))
            {
                continue;
            }

            // A delegate left behind runs whenever its consumer chooses.
            if (IsInsideNestedFunction(node, body))
            {
                return true;
            }

            if (!method.IsAsync)
            {
                continue;
            }

            // Everything up to the first suspension has already run by the time the caller holds
            // the task — including what that first await is handed, since its operands are
            // evaluated before it gives up control — unless a loop brings it back round.
            if (IsInLoopContainingSuspension(node, body))
            {
                return true;
            }

            if (
                firstSuspension is null
                || (
                    node.SpanStart >= firstSuspension.SpanStart
                    && !firstSuspension.Expression.Span.Contains(node.Span)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether awaiting this actually gives up control. Awaiting a task that has already finished
    /// runs straight on.
    /// </summary>
    private static bool Suspends(AwaitExpressionSyntax await) =>
        !IsAlreadyFinished(await.Expression);

    /// <summary>
    /// Whether an awaited expression is work that has already finished, seen through the wrappers
    /// that hand the same pending work back — <c>ConfigureAwait</c>, <c>AsTask</c> — and through a
    /// local that names it.
    /// </summary>
    private static bool IsAlreadyFinished(ExpressionSyntax expression)
    {
        if (IsCompletedTaskExpression(expression))
        {
            return true;
        }

        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsAlreadyFinished(
                parenthesized.Expression
            ),
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax forwarding
            } when TaskForwardingNames.Contains(forwarding.Name.Identifier.ValueText) =>
                IsAlreadyFinished(forwarding.Expression),
            // `var done = Task.CompletedTask; await done;` is the same nothing, one name later.
            IdentifierNameSyntax name => FindLocalInitializer(name) is { } initializer
                && IsAlreadyFinished(initializer),
            _ => false,
        };
    }

    /// <summary>
    /// The initializer of the local a name refers to, when that local is declared in the same
    /// body and initialised where it is declared.
    /// </summary>
    private static ExpressionSyntax? FindLocalInitializer(IdentifierNameSyntax name)
    {
        for (var scope = name.Parent; scope is not null; scope = scope.Parent)
        {
            if (ExecutableSyntaxHelper.IsExecutableBoundary(scope))
            {
                break;
            }

            foreach (
                var declarator in scope
                    .ChildNodes()
                    .OfType<LocalDeclarationStatementSyntax>()
                    .SelectMany(statement => statement.Declaration.Variables)
            )
            {
                if (declarator.Identifier.ValueText == name.Identifier.ValueText)
                {
                    return declarator.Initializer?.Value;
                }
            }
        }

        return null;
    }

    private static bool IsInLoopContainingSuspension(SyntaxNode node, SyntaxNode body) =>
        node.Ancestors()
            .TakeWhile(ancestor => ancestor != body.Parent)
            .Any(ancestor =>
                ancestor
                    is ForStatementSyntax
                        or ForEachStatementSyntax
                        or WhileStatementSyntax
                        or DoStatementSyntax
                && ExecutableSyntaxHelper
                    .EnumerateSameBoundaryNodes(ancestor)
                    .Any(inner => inner is AwaitExpressionSyntax await && Suspends(await))
            );

    /// <summary>
    /// Whether the service's own disposal waits for work to finish. A type that blocks in
    /// <c>Dispose</c> has arranged the join itself, so the scope tearing it down is the moment
    /// the work is collected rather than the moment it is orphaned.
    /// </summary>
    private static bool DisposalJoinsTheWork(INamedTypeSymbol? implementation) =>
        implementation is not null
        && implementation
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(member => member.Name is "Dispose" or "DisposeAsync")
            .Any(member => Blocks(member) || AwaitsPendingWork(member));

    /// <summary>
    /// Whether a disposal awaits something other than a disposal of its own — the escaped task
    /// itself, most often — which means it cannot complete before the work does.
    /// </summary>
    private static bool AwaitsPendingWork(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length == 1
        && ExecutableSyntaxHelper.TryGetExecutableBody(
            method.DeclaringSyntaxReferences[0].GetSyntax(),
            out var body
        )
        && body
            .DescendantNodesAndSelf()
            .OfType<AwaitExpressionSyntax>()
            .Any(await =>
                await.Expression is not InvocationExpressionSyntax call
                || GetInvokedName(call) is not ("Dispose" or "DisposeAsync")
            );

    /// <summary>
    /// Whether a later call on the same service waits for the work before the scope ends. A
    /// service with a <c>Join</c> of its own is being used as a pair of calls, not forgotten
    /// after the first.
    /// </summary>
    private static bool ServiceIsJoinedLater(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        ScopeRegion scope
    )
    {
        if (semanticModel.GetSymbolInfo(receiver).Symbol is not ILocalSymbol service)
        {
            return false;
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(scope.Region))
        {
            if (
                node is not InvocationExpressionSyntax later
                || later.SpanStart <= invocation.SpanStart
                || later.Expression is not MemberAccessExpressionSyntax member
                || !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(member.Expression).Symbol,
                    service
                )
            )
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(later).Symbol is IMethodSymbol laterMethod
                && Blocks(laterMethod))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a method's visible body waits for something to finish.
    /// </summary>
    private static bool Blocks(IMethodSymbol method) => Blocks(method, 0);

    private static bool Blocks(IMethodSymbol method, int depth)
    {
        if (
            depth > MaxHelperDepth
            || method.DeclaringSyntaxReferences.Length != 1
            || !ExecutableSyntaxHelper.TryGetExecutableBody(
                method.DeclaringSyntaxReferences[0].GetSyntax(),
                out var body
            )
        )
        {
            return false;
        }

        foreach (var call in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (GetInvokedName(call) is { } name && JoiningCallNames.Contains(name))
            {
                return true;
            }

            // The wait may be a helper or two down: `Dispose() => Drain();`.
            if (
                GetOwnInstanceMethod(call, method.ContainingType) is { } helper
                && Blocks(helper, depth + 1)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the method, or a helper of its own type that it calls, stores a task in instance
    /// state. A service that keeps what it started intends to join it.
    /// </summary>
    private static bool RetainsTheTask(IMethodSymbol method, int depth)
    {
        if (
            depth > MaxHelperDepth
            || method.DeclaringSyntaxReferences.Length != 1
            || !ExecutableSyntaxHelper.TryGetExecutableBody(
                method.DeclaringSyntaxReferences[0].GetSyntax(),
                out var body
            )
        )
        {
            return false;
        }

        var declaringType = method.ContainingType;

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(body))
        {
            if (
                node is AssignmentExpressionSyntax assignment
                && IsInstanceStateReference(assignment.Left, declaringType)
            )
            {
                return true;
            }

            if (
                node is InvocationExpressionSyntax call
                && GetOwnInstanceMethod(call, declaringType) is { } helper
                && RetainsTheTask(helper, depth + 1)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The method a call names when it is a call on this same instance — unqualified, or written
    /// through <c>this</c>. Calls on anything else say nothing about this service.
    /// </summary>
    private static IMethodSymbol? GetOwnInstanceMethod(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? declaringType
    )
    {
        if (declaringType is null)
        {
            return null;
        }

        var name = invocation.Expression switch
        {
            SimpleNameSyntax simple => simple,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } member => member.Name,
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        return declaringType
            .GetMembers(name.Identifier.ValueText)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => !candidate.IsStatic);
    }

    /// <summary>
    /// Whether a node names instance state of the declaring type: <c>this</c>, <c>base</c>, or a
    /// member of it reached without a receiver of its own.
    /// </summary>
    private static bool IsInstanceStateReference(SyntaxNode node, INamedTypeSymbol? declaringType)
    {
        if (node is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return true;
        }

        if (declaringType is null || node is not SimpleNameSyntax name)
        {
            return false;
        }

        // `other.Field` is somebody else's state; only an unqualified name, or one qualified by
        // `this`, reaches this instance.
        if (
            name.Parent is MemberAccessExpressionSyntax qualified
            && qualified.Name == name
            && qualified.Expression is not ThisExpressionSyntax
        )
        {
            return false;
        }

        return declaringType
            .GetMembers(name.Identifier.ValueText)
            .Any(member => !member.IsStatic);
    }

    private static bool IsCompletedTaskExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsCompletedTaskExpression(
                parenthesized.Expression
            ),
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.DefaultLiteralExpression),
            DefaultExpressionSyntax => true,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText
                is "CompletedTask",
            InvocationExpressionSyntax call => GetInvokedName(call)
                    is "FromResult"
                        or "FromCanceled"
                        or "FromException"
                // `Task.WhenAll()` over nothing — or over work that has already finished — is
                // finished before it is handed back.
                || (
                    GetInvokedName(call) is "WhenAll" or "WhenAny"
                    && call.ArgumentList.Arguments.All(argument =>
                        IsAlreadyFinished(argument.Expression)
                    )
                ),
            ObjectCreationExpressionSyntax creation => creation.ArgumentList is null
                or { Arguments.Count: 0 },
            _ => false,
        };

    private static string? GetInvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => null,
        };

    /// <summary>
    /// The service type a receiver was resolved as, followed back through the locals it passed
    /// through. <see langword="null"/> when the resolution cannot be read here, which leaves the
    /// lifetime unknown rather than proven.
    /// </summary>
    private static ITypeSymbol? GetResolvedServiceType(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        int depth
    )
    {
        if (depth > MaxResolutionDepth)
        {
            return null;
        }

        switch (receiver)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return GetResolvedServiceType(parenthesized.Expression, semanticModel, depth);

            case InvocationExpressionSyntax invocation
                when semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol
                {
                    TypeArguments.Length: 1
                } resolution
                    && ResolutionMethodNames.Contains(resolution.Name):
                return resolution.TypeArguments[0];

            case IdentifierNameSyntax identifier
                when semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol
                {
                    DeclaringSyntaxReferences.Length: 1
                } local:
                return local.DeclaringSyntaxReferences[0].GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: { } initializer }
                    ? GetResolvedServiceType(initializer, semanticModel, depth + 1)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// What becomes of the task, as the name of the escape when it leaves the scope un-awaited and
    /// <see langword="null"/> when the scope outlives the work. Only a fate this rule can name is
    /// reported; anything else is left alone.
    /// </summary>
    private static string? ClassifyTaskFate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ScopeRegion scope
    )
    {
        var methodName = (invocation.Expression as MemberAccessExpressionSyntax)
            ?.Name
            .Identifier
            .ValueText;

        if (methodName is null)
        {
            return null;
        }

        SyntaxNode current = invocation;

        for (var parent = current.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                // Awaited or blocked on where it stands: the work is over before the scope is.
                case AwaitExpressionSyntax:
                    return null;

                case MemberAccessExpressionSyntax member when member.Expression == current:
                    if (BlockingConsumerNames.Contains(member.Name.Identifier.ValueText))
                    {
                        return null;
                    }

                    if (!TaskForwardingNames.Contains(member.Name.Identifier.ValueText))
                    {
                        return null;
                    }

                    break;

                case ParenthesizedExpressionSyntax:
                case CastExpressionSyntax:
                case InvocationExpressionSyntax:
                    break;

                // Handed back to the caller while the scope is disposed on the way out.
                case ReturnStatementSyntax:
                case ArrowExpressionClauseSyntax:
                    return methodName;

                // Started and forgotten: `worker.RunAsync();` or `_ = worker.RunAsync();`.
                case ExpressionStatementSyntax:
                    return methodName;

                case AssignmentExpressionSyntax assignment
                    when assignment.Right == current
                        || assignment.Right.Span.Contains(current.Span):
                    return IsStorageOutsideScope(assignment.Left, semanticModel, scope)
                        && !IsConsumedInsideScope(assignment.Left, semanticModel, scope)
                        ? methodName
                        : null;

                // Collected into something the scope does not own, to be awaited after it ends.
                case ArgumentSyntax argument:
                    return IsCollectedOutsideScope(argument, semanticModel, scope)
                        ? methodName
                        : null;

                default:
                    return null;
            }

            current = parent;

            if (parent == scope.Region)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an assignment target survives the scope: a field, a property, or a local declared
    /// outside the region the scope's disposal ends.
    /// </summary>
    private static bool IsStorageOutsideScope(
        ExpressionSyntax target,
        SemanticModel semanticModel,
        ScopeRegion scope
    )
    {
        var symbol = semanticModel.GetSymbolInfo(target).Symbol;

        return symbol switch
        {
            // `_ = worker.RunAsync()` names the fire-and-forget outright: nothing will ever
            // observe the task, let alone wait for it.
            IDiscardSymbol => true,
            // A setter is a method like any other, and this one may wait on what it is given.
            IPropertySymbol property => property.SetMethod is not { } setter || !Blocks(setter),
            IFieldSymbol => true,
            ILocalSymbol local => IsDeclaredOutside(local, scope),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the task is handed to a container that outlives the scope, as in
    /// <c>pending.Add(worker.RunAsync())</c> where <c>pending</c> was declared before the scope.
    /// </summary>
    private static bool IsCollectedOutsideScope(
        ArgumentSyntax argument,
        SemanticModel semanticModel,
        ScopeRegion scope
    )
    {
        if (
            argument.Parent?.Parent is not InvocationExpressionSyntax call
            || call.Expression is not MemberAccessExpressionSyntax member
            // Handing a task to an arbitrary method proves nothing — it may well wait on it.
            // Only a call that puts the task somewhere names it as kept for later, and even a
            // call named `Add` may turn out to wait when its body is visible.
            || !CollectionAddNames.Contains(member.Name.Identifier.ValueText)
            || (
                semanticModel.GetSymbolInfo(call).Symbol is IMethodSymbol collector
                && Blocks(collector)
            )
        )
        {
            return false;
        }

        return IsStorageOutsideScope(member.Expression, semanticModel, scope)
            && !IsConsumedInsideScope(member.Expression, semanticModel, scope);
    }

    /// <summary>
    /// Whether the storage the task was written to is read again before the scope ends. Reading
    /// it is how the scope waits — directly, through an alias, or by handing it to a helper that
    /// blocks — so only storage nothing looks at again is provably abandoned. Adding to a
    /// collection is the exception: that is the store being filled, not the task being watched.
    /// </summary>
    private static bool IsConsumedInsideScope(
        ExpressionSyntax target,
        SemanticModel semanticModel,
        ScopeRegion scope
    )
    {
        if (semanticModel.GetSymbolInfo(target).Symbol is not { } storage)
        {
            return false;
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(scope.Region))
        {
            if (
                node is not SimpleNameSyntax name
                || !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(name).Symbol,
                    storage
                )
            )
            {
                continue;
            }

            var reference = name.Parent is MemberAccessExpressionSyntax qualified
                && qualified.Name == name
                ? (ExpressionSyntax)qualified
                : name;

            if (IsWriteTarget(reference) || IsReceiverOfCollectionAdd(reference))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsWriteTarget(ExpressionSyntax reference) =>
        reference.Parent is AssignmentExpressionSyntax assignment && assignment.Left == reference;

    private static bool IsReceiverOfCollectionAdd(ExpressionSyntax reference) =>
        reference.Parent is MemberAccessExpressionSyntax
        {
            Parent: InvocationExpressionSyntax
        } member
        && member.Expression == reference
        && CollectionAddNames.Contains(member.Name.Identifier.ValueText);

    private static bool IsDeclaredOutside(ILocalSymbol local, ScopeRegion scope)
    {
        if (local.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        return !scope.Region.Span.Contains(local.DeclaringSyntaxReferences[0].Span);
    }

    private static HashSet<ILocalSymbol> CollectReassignedLocals(
        SyntaxNode executableBody,
        SemanticModel semanticModel
    )
    {
        var reassigned = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in executableBody.DescendantNodes())
        {
            var written = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument => argument.Expression,
                _ => null,
            };

            if (
                written is not null
                && semanticModel.GetSymbolInfo(written).Symbol is ILocalSymbol local
            )
            {
                reassigned.Add(local);
            }
        }

        return reassigned;
    }

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode region)
    {
        for (var current = node; current is not null && current != region; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or QueryExpressionSyntax)
            {
                return true;
            }

            // A local function nobody keeps a delegate to runs where it is called, which is here.
            if (current is LocalFunctionStatementSyntax localFunction && IsDeferred(localFunction))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a local function could run later: something takes it as a value rather than
    /// calling it outright.
    /// </summary>
    private static bool IsDeferred(LocalFunctionStatementSyntax localFunction)
    {
        var container = localFunction.Parent;

        if (container is null)
        {
            return true;
        }

        var name = localFunction.Identifier.ValueText;

        return container
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                identifier.Identifier.ValueText == name
                && identifier.Parent is not InvocationExpressionSyntax
            );
    }

    /// <summary>
    /// Answers whether a service type is provably a singleton, from the registrations in this
    /// compilation and from the framework services whose lifetime is fixed.
    /// </summary>
    private sealed class ServiceLifetimeLookup
    {
        private readonly RegistrationCollector _registrations;
        private readonly KnownServiceLifetimeClassifier _knownLifetimes;

        public ServiceLifetimeLookup(
            RegistrationCollector registrations,
            KnownServiceLifetimeClassifier knownLifetimes
        )
        {
            _registrations = registrations;
            _knownLifetimes = knownLifetimes;
        }

        /// <summary>
        /// The implementation the container would build for a service type, when the
        /// registrations name one. A factory or an instance registration names none.
        /// </summary>
        public INamedTypeSymbol? GetImplementationType(ITypeSymbol? serviceType)
        {
            if (serviceType is not INamedTypeSymbol named)
            {
                return null;
            }

            if (
                _registrations.TryGetRegistration(named, key: null, isKeyed: false, out var found)
                && found?.ImplementationType is { } implementation
            )
            {
                return implementation;
            }

            // A concrete type registered as itself is its own implementation.
            return named.TypeKind == TypeKind.Class ? named : null;
        }

        /// <summary>
        /// Whether disposing the scope provably tears nothing down that the work could still be
        /// using: neither the service itself nor anything it is built from is disposable. A type
        /// this compilation cannot see, or a service the container builds through a factory,
        /// leaves the question open and so is not proven.
        /// </summary>
        public bool NothingToDispose(ITypeSymbol? serviceType)
        {
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            return NothingToDispose(serviceType, visited, 0);
        }

        private bool NothingToDispose(ITypeSymbol? serviceType, HashSet<ITypeSymbol> visited, int depth)
        {
            if (depth > MaxDependencyDepth || serviceType is null)
            {
                return false;
            }

            if (!visited.Add(serviceType))
            {
                // A cycle is DI017's finding; for this question it adds nothing new.
                return true;
            }

            if (GetImplementationType(serviceType) is not { } implementation)
            {
                return false;
            }

            // Only source this compilation can read proves anything about disposal.
            if (
                implementation.DeclaringSyntaxReferences.Length == 0
                || IsDisposable(implementation)
                || IsDisposable(serviceType)
            )
            {
                return false;
            }

            var constructor = implementation
                .InstanceConstructors.Where(candidate =>
                    candidate.DeclaredAccessibility == Accessibility.Public
                )
                .OrderByDescending(candidate => candidate.Parameters.Length)
                .FirstOrDefault();

            return constructor is null
                || constructor.Parameters.All(parameter =>
                    NothingToDispose(parameter.Type, visited, depth + 1)
                );
        }

        private static bool IsDisposable(ITypeSymbol type) =>
            type.AllInterfaces.Any(@interface =>
                @interface.ToDisplayString()
                    is "System.IDisposable"
                        or "System.IAsyncDisposable"
            )
            || type.ToDisplayString() is "System.IDisposable" or "System.IAsyncDisposable";

        public bool IsSingleton(ITypeSymbol? serviceType)
        {
            if (serviceType is null)
            {
                return false;
            }

            if (
                _knownLifetimes.TryGetLifetime(serviceType, isKeyed: false, out var knownLifetime)
                && knownLifetime == ServiceLifetime.Singleton
            )
            {
                return true;
            }

            return _registrations.GetLifetime(serviceType) == ServiceLifetime.Singleton;
        }
    }

    /// <summary>
    /// A scope local together with the syntax its <c>using</c> disposal ends: the enclosing block
    /// for a using declaration, the using statement itself for the statement form.
    /// </summary>
    private sealed class ScopeRegion
    {
        public ScopeRegion(
            ILocalSymbol local,
            SyntaxNode region,
            VariableDeclaratorSyntax declarator
        )
        {
            Local = local;
            Region = region;
            Declarator = declarator;
        }

        public ILocalSymbol Local { get; }

        public SyntaxNode Region { get; }

        public VariableDeclaratorSyntax Declarator { get; }
    }
}
