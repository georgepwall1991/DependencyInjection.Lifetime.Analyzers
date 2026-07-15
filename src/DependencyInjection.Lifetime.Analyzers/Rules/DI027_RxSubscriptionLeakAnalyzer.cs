using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// DI027: a shorter-lived registered service subscribes an instance-capturing handler to an
/// observable on a longer-lived publisher (an injected singleton dependency, or a scoped publisher
/// shared by a transient subscriber) and discards the <see cref="System.IDisposable"/> token that
/// <c>IObservable&lt;T&gt;.Subscribe</c> returns. The Rx twin of DI025: where DI025 proves a missing
/// <c>-=</c>, DI027 proves the returned token is thrown away, which roots the observer (and the
/// subscriber it captures) in the publisher for its whole lifetime just the same.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI027_RxSubscriptionLeakAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RxSubscriptionLeak);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var iObservable = compilationContext.Compilation.GetTypeByMetadataName("System.IObservable`1");
            if (iObservable is null)
            {
                return;
            }

            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(
                WellKnownTypes.Create(compilationContext.Compilation));

            var subscriptions = new ConcurrentBag<SubscriptionRecord>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => CollectSubscription(syntaxContext, iObservable, subscriptions),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
                ReportLeakedSubscriptions(endContext, registrationCollector, lifetimeClassifier, subscriptions));
        });
    }

    private sealed class SubscriptionRecord
    {
        public SubscriptionRecord(
            INamedTypeSymbol containingType,
            EventReceiverKind receiverKind,
            ISymbol? receiverRoot,
            ImmutableArray<ISymbol> receiverSegments,
            INamedTypeSymbol? publisherType,
            string observableName,
            string handlerDisplay,
            Location location)
        {
            ContainingType = containingType;
            ReceiverKind = receiverKind;
            ReceiverRoot = receiverRoot;
            ReceiverSegments = receiverSegments;
            PublisherType = publisherType;
            ObservableName = observableName;
            HandlerDisplay = handlerDisplay;
            Location = location;
        }

        public INamedTypeSymbol ContainingType { get; }
        public EventReceiverKind ReceiverKind { get; }
        public ISymbol? ReceiverRoot { get; }

        /// <summary>
        /// Intermediate members between the chain root and the observable, in access order
        /// (<c>_source.Ticks.Subscribe(H)</c> stores <c>Ticks</c>). Empty for direct receivers.
        /// </summary>
        public ImmutableArray<ISymbol> ReceiverSegments { get; }

        public INamedTypeSymbol? PublisherType { get; }
        public string ObservableName { get; }
        public string HandlerDisplay { get; }
        public Location Location { get; }
    }

    private static void CollectSubscription(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol iObservable,
        ConcurrentBag<SubscriptionRecord> records)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        // The IObserver<T> overload is a documented FN for v1.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Subscribe" ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        // FQN-light match: any method named Subscribe returning System.IDisposable, invoked on a
        // System.IObservable<T> receiver. Covers System.Reactive, community Rx, and test stubs alike.
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !ReturnsDisposable(method.ReturnType))
        {
            return;
        }

        var observableExpression = memberAccess.Expression;
        ArgumentSyntax? observableArgument = null;

        // A direct static extension call has the extension container on the left of Subscribe;
        // resolve the actual observable through the bound `this` parameter. Operation argument
        // mapping preserves correctness when named arguments are reordered.
        if (method.IsStatic)
        {
            if (!method.IsExtensionMethod ||
                semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
                operation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Syntax
                    is not ArgumentSyntax sourceArgument)
            {
                return;
            }

            observableArgument = sourceArgument;
            observableExpression = sourceArgument.Expression;
        }

        if (semanticModel.GetTypeInfo(observableExpression, cancellationToken).Type is not { } receiverType ||
            !ImplementsObservable(receiverType, iObservable))
        {
            return;
        }

        if (!TokenIsLeaked(invocation, semanticModel, cancellationToken))
        {
            return;
        }

        var containingType = GetEnclosingNamedType(invocation, semanticModel, cancellationToken);
        if (containingType is null)
        {
            return;
        }

        // Any callback that captures the subscriber roots it through the discarded token, so the
        // whole subscribe overload is scanned (`Subscribe(onNext, onError, onCompleted)`): a static
        // onNext with a capturing onError still leaks. Report on the first capturing argument.
        string? handlerDisplay = null;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument == observableArgument)
            {
                continue;
            }

            var (handlerKind, _, candidateDisplay) = EventHandlerClassification.ClassifyHandler(
                argument.Expression, containingType, semanticModel, cancellationToken);
            if (handlerKind != EventHandlerKind.Unknown)
            {
                handlerDisplay = candidateDisplay;
                break;
            }
        }

        if (handlerDisplay is null)
        {
            return;
        }

        var (receiverKind, receiverRoot, receiverSegments, publisherType) =
            EventReceiverClassification.ClassifyReceiverExpression(
                observableExpression, containingType, semanticModel, cancellationToken);
        if (receiverKind == EventReceiverKind.Unknown)
        {
            return;
        }

        var observableName = receiverSegments.IsEmpty
            ? receiverRoot?.Name ?? publisherType?.Name ?? "the observable"
            : receiverSegments[receiverSegments.Length - 1].Name;

        records.Add(new SubscriptionRecord(
            containingType,
            receiverKind,
            receiverRoot,
            receiverSegments,
            publisherType,
            observableName,
            handlerDisplay,
            invocation.GetLocation()));
    }

    /// <summary>
    /// Proves the subscription token is discarded: an ignored expression statement, a discard
    /// assignment (<c>_ = obs.Subscribe(...)</c>), or a local variable initialized with the
    /// Subscribe result that is never referenced again in the enclosing body (and is not a
    /// <c>using</c> declaration). Anything that touches the token later — a <c>.Dispose()</c>, a
    /// return, an argument, a field store, a <c>using</c> — stays silent because the token may be
    /// disposed on a path the analyzer does not model.
    /// </summary>
    private static bool TokenIsLeaked(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        ExpressionSyntax node = invocation;
        var parent = invocation.Parent;
        while (parent is ParenthesizedExpressionSyntax parenthesized)
        {
            node = parenthesized;
            parent = parenthesized.Parent;
        }

        if (parent is ExpressionStatementSyntax)
        {
            return true;
        }

        if (parent is AssignmentExpressionSyntax assignment &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            assignment.Right == node &&
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is IDiscardSymbol)
        {
            return true;
        }

        return TokenIsUnreferencedLocal(node, parent, semanticModel, cancellationToken);
    }

    private static bool TokenIsUnreferencedLocal(
        ExpressionSyntax node,
        SyntaxNode? parent,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } equals ||
            equals.Value != node ||
            declarator.Parent is not VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax localStatement })
        {
            return false;
        }

        // `using var sub = obs.Subscribe(...)` disposes deterministically at scope end.
        if (!localStatement.UsingKeyword.IsKind(SyntaxKind.None))
        {
            return false;
        }

        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local)
        {
            return false;
        }

        var body = EventReceiverClassification.ExecutableBodyOf(declarator);
        if (body is null)
        {
            return false;
        }

        // A real disposal, return, or escape always references the local by name. If the token is
        // declared and never touched again, it is discarded exactly like an ignored statement.
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != local.Name ||
                identifier.Parent is VariableDeclaratorSyntax)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReturnsDisposable(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_IDisposable)
        {
            return true;
        }

        return returnType.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_IDisposable);
    }

    private static bool ImplementsObservable(ITypeSymbol receiverType, INamedTypeSymbol iObservable)
    {
        if (SymbolEqualityComparer.Default.Equals(receiverType.OriginalDefinition, iObservable))
        {
            return true;
        }

        return receiverType.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iObservable));
    }

    private static INamedTypeSymbol? GetEnclosingNamedType(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        return typeDeclaration is null
            ? null
            : semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
    }

    private static void ReportLeakedSubscriptions(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ConcurrentBag<SubscriptionRecord> subscriptions)
    {
        if (subscriptions.IsEmpty)
        {
            return;
        }

        var registrations = registrationCollector.AllRegistrations.ToList();
        var subscriberRanks = BuildSubscriberRankMap(registrations);
        var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        foreach (var subscription in subscriptions)
        {
            if (!IsReportableSubscription(
                    subscription, registrations, subscriberRanks, lifetimeClassifier,
                    context.Compilation, semanticModelsByTree, context.CancellationToken,
                    out var subscriberRank, out var publisherRank))
            {
                continue;
            }

            var lifetimeText = subscriberRank == RankOf(ServiceLifetime.Transient) ? "transient" : "scoped";
            var publisherText = publisherRank == RankOf(ServiceLifetime.Singleton)
                ? $"the singleton service '{subscription.PublisherType?.Name}'"
                : $"the scoped service '{subscription.PublisherType?.Name}'";

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RxSubscriptionLeak,
                subscription.Location,
                subscription.ContainingType.Name,
                lifetimeText,
                subscription.HandlerDisplay,
                subscription.ObservableName,
                publisherText));
        }
    }

    /// <summary>
    /// Applies the lifetime-rank, injected-member, and chain-stability gates that decide whether a
    /// discarded subscription is a reportable cross-lifetime leak. DI027 is single-tier: a scoped
    /// publisher above a transient subscriber reports at Warning just like a singleton, unlike
    /// DI025/DI026's Info split.
    /// </summary>
    private static bool IsReportableSubscription(
        SubscriptionRecord subscription,
        List<ServiceRegistration> registrations,
        Dictionary<INamedTypeSymbol, int> subscriberRanks,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken,
        out int subscriberRank,
        out int publisherRank)
    {
        publisherRank = -1;

        if (!subscriberRanks.TryGetValue(subscription.ContainingType, out subscriberRank) ||
            subscriberRank >= RankOf(ServiceLifetime.Singleton))
        {
            return false;
        }

        var resolvedPublisherRank = ResolvePublisherRank(subscription, registrations, lifetimeClassifier);
        if (resolvedPublisherRank is null ||
            resolvedPublisherRank < RankOf(ServiceLifetime.Scoped) ||
            resolvedPublisherRank <= subscriberRank)
        {
            return false;
        }

        publisherRank = resolvedPublisherRank.Value;

        if (subscription.ReceiverKind == EventReceiverKind.InjectedMember &&
            !EventReceiverClassification.MemberIsProvablyInjected(
                subscription.ReceiverRoot, compilation, semanticModelsByTree, cancellationToken))
        {
            return false;
        }

        if (!subscription.ReceiverSegments.IsEmpty &&
            !EventReceiverClassification.ChainSegmentsAreStableProjections(
                subscription.ReceiverSegments, subscription.PublisherType, registrations,
                compilation, semanticModelsByTree, cancellationToken))
        {
            return false;
        }

        return true;
    }

    private static int RankOf(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.Transient => 0,
        ServiceLifetime.Scoped => 1,
        _ => 2
    };

    // Mirrors DI025's subscriber-rank map (base-chain aware, most-conservative wins). Duplicated
    // rather than shared so DI025's proven collection stays untouched.
    private static Dictionary<INamedTypeSymbol, int> BuildSubscriberRankMap(List<ServiceRegistration> registrations)
    {
        var ranks = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                continue;
            }

            var rank = RankOf(registration.Lifetime);
            for (var current = implementationType;
                 current is not null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                if (!ranks.TryGetValue(current, out var existing) || rank > existing)
                {
                    ranks[current] = rank;
                }
            }
        }

        return ranks;
    }

    /// <summary>
    /// Resolves the publisher's lifetime rank from the observable's root registration. Exact closed
    /// registrations win over open-generic fallbacks (the DI017 precedent); multiple matches take
    /// the most conservative (shortest-lived) lifetime. Unregistered publishers fall back to the
    /// known framework classifier, and keyed-only publishers stay silent (no unkeyed match).
    /// </summary>
    private static int? ResolvePublisherRank(
        SubscriptionRecord subscription,
        List<ServiceRegistration> registrations,
        KnownServiceLifetimeClassifier lifetimeClassifier)
    {
        if (subscription.PublisherType is null)
        {
            return null;
        }

        var rank = MinimumRegisteredRank(registrations, subscription.PublisherType);

        if (rank is null &&
            subscription.PublisherType.IsGenericType &&
            !subscription.PublisherType.IsUnboundGenericType)
        {
            rank = MinimumRegisteredRank(
                registrations,
                subscription.PublisherType.ConstructUnboundGenericType());
        }

        if (rank is not null)
        {
            return rank;
        }

        return lifetimeClassifier.TryGetLifetime(subscription.PublisherType, isKeyed: false, out var knownLifetime)
            ? RankOf(knownLifetime)
            : null;
    }

    private static int? MinimumRegisteredRank(
        List<ServiceRegistration> registrations,
        INamedTypeSymbol serviceType)
    {
        int? rank = null;
        foreach (var registration in registrations)
        {
            if (registration.IsKeyed ||
                !SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType))
            {
                continue;
            }

            var registrationRank = RankOf(registration.Lifetime);
            if (rank is null || registrationRank < rank)
            {
                rank = registrationRank;
            }
        }

        return rank;
    }
}
