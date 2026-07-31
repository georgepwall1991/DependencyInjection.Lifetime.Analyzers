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
        ImmutableHashSet.Create("Wait", "GetAwaiter", "Result", "WaitAsync");

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
    /// Calls that wait on the tasks handed to them, so a task passed to one of these inside the
    /// scope is finished before the scope is.
    /// </summary>
    private static readonly ImmutableHashSet<string> WaitingMethodNames = ImmutableHashSet.Create(
        "Wait",
        "WaitAll",
        "WaitAny",
        "WhenAll",
        "WhenAny",
        "GetResult"
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
                || ReturnsCompletedTask(invocation, semanticModel)
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

            var outcome = ClassifyTaskFate(invocation, semanticModel, scope);

            if (outcome is not { } escape)
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
            );

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

        // A singleton resolved through a child scope belongs to the root provider, which no
        // `using` here disposes, so the work has nothing torn down underneath it.
        return !lifetimes.IsSingleton(GetResolvedServiceType(receiver, semanticModel, 0));
    }

    /// <summary>
    /// Whether the method provably hands back a task that has already finished, so there is no
    /// work left to outlive the scope. Only a body visible here and made of nothing but
    /// completed-task returns proves it.
    /// </summary>
    private static bool ReturnsCompletedTask(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        if (
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
            {
                IsAsync: false,
                DeclaringSyntaxReferences.Length: 1
            } method
        )
        {
            return false;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();

        if (declaration is not BaseMethodDeclarationSyntax and not AccessorDeclarationSyntax)
        {
            return false;
        }

        if (
            declaration is MethodDeclarationSyntax { ExpressionBody.Expression: { } expressionBody }
        )
        {
            return IsCompletedTaskExpression(expressionBody);
        }

        var returns = declaration
            .DescendantNodes(node => !ExecutableSyntaxHelper.IsExecutableBoundary(node))
            .OfType<ReturnStatementSyntax>()
            .ToList();

        return returns.Count > 0
            && returns.All(statement =>
                statement.Expression is { } value && IsCompletedTaskExpression(value)
            );
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
            InvocationExpressionSyntax call => GetInvokedName(call) is "FromResult" or "FromCanceled"
                or "FromException",
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
            IFieldSymbol or IPropertySymbol => true,
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
        )
        {
            return false;
        }

        return IsStorageOutsideScope(member.Expression, semanticModel, scope)
            && !IsConsumedInsideScope(member.Expression, semanticModel, scope);
    }

    /// <summary>
    /// Whether the storage the task was written to is awaited, or blocked on, before the scope
    /// ends. Somewhere to put a task is not an escape when the scope waits for it anyway.
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
            var consumed = node switch
            {
                AwaitExpressionSyntax await => await.Expression,
                MemberAccessExpressionSyntax member
                    when BlockingConsumerNames.Contains(member.Name.Identifier.ValueText) =>
                    member.Expression,
                // `Task.WaitAll(pending)` waits on what it is handed rather than on a receiver.
                InvocationExpressionSyntax call
                    when GetInvokedName(call) is { } name && WaitingMethodNames.Contains(name) =>
                    call,
                _ => null,
            };

            if (
                consumed is not null
                && consumed
                    .DescendantNodesAndSelf()
                    .OfType<SimpleNameSyntax>()
                    .Any(name =>
                        SymbolEqualityComparer.Default.Equals(
                            semanticModel.GetSymbolInfo(name).Symbol,
                            storage
                        )
                    )
            )
            {
                return true;
            }
        }

        return false;
    }

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
            if (
                current
                is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or QueryExpressionSyntax
            )
            {
                return true;
            }
        }

        return false;
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
