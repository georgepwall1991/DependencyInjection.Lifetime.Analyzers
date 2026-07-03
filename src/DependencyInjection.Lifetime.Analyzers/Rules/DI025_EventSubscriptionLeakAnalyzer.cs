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
/// DI025: a shorter-lived registered service subscribes an instance-capturing handler to an
/// event on a longer-lived publisher (an injected singleton dependency or a static event)
/// without a matching unsubscription. The publisher's delegate list roots every subscriber
/// instance the container ever creates.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI025_EventSubscriptionLeakAnalyzer : DiagnosticAnalyzer
{
    private const int StaticPublisherRank = 3;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.EventSubscriptionLeak,
            DiagnosticDescriptors.EventSubscriptionLeakAnonymousHandler,
            DiagnosticDescriptors.EventSubscriptionLeakIneffectiveUnsubscribe,
            DiagnosticDescriptors.EventSubscriptionLeakScopedPublisher,
            DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherAnonymousHandler,
            DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherIneffectiveUnsubscribe);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(
                WellKnownTypes.Create(compilationContext.Compilation));

            var subscriptions = new ConcurrentBag<EventAccessRecord>();
            var removals = new ConcurrentBag<EventAccessRecord>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => CollectEventAccess(syntaxContext, subscriptions, isSubscription: true),
                SyntaxKind.AddAssignmentExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => CollectEventAccess(syntaxContext, removals, isSubscription: false),
                SyntaxKind.SubtractAssignmentExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => CollectDelegateCombineAccess(syntaxContext, subscriptions, removals),
                SyntaxKind.SimpleAssignmentExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
                ReportLeakedSubscriptions(endContext, registrationCollector, lifetimeClassifier, subscriptions, removals));
        });
    }

    private sealed class EventAccessRecord
    {
        public EventAccessRecord(
            INamedTypeSymbol containingType,
            ISymbol member,
            EventReceiverKind receiverKind,
            ISymbol? receiverRoot,
            ImmutableArray<ISymbol> receiverSegments,
            INamedTypeSymbol? publisherType,
            EventHandlerKind handlerKind,
            ISymbol? handlerIdentity,
            string handlerDisplay,
            Location location)
        {
            ContainingType = containingType;
            Member = member;
            ReceiverKind = receiverKind;
            ReceiverRoot = receiverRoot;
            ReceiverSegments = receiverSegments;
            PublisherType = publisherType;
            HandlerKind = handlerKind;
            HandlerIdentity = handlerIdentity;
            HandlerDisplay = handlerDisplay;
            Location = location;
        }

        public INamedTypeSymbol ContainingType { get; }

        /// <summary>
        /// The subscribed-to member: a field-like event, or a delegate-typed field/property
        /// that carries the subscription through <c>+=</c> or a <c>Delegate.Combine</c>
        /// self-assignment. Only <c>.Name</c>, <c>.ContainingType</c>, <c>.IsStatic</c>, and
        /// symbol-equality matching are used, all valid on <see cref="ISymbol"/>.
        /// </summary>
        public ISymbol Member { get; }
        public EventReceiverKind ReceiverKind { get; }
        public ISymbol? ReceiverRoot { get; }

        /// <summary>
        /// Intermediate members between the chain root and the event, in access order
        /// (<c>_outer.Inner.Changed</c> stores <c>Inner</c>). Empty for direct receivers.
        /// </summary>
        public ImmutableArray<ISymbol> ReceiverSegments { get; }

        public INamedTypeSymbol? PublisherType { get; }
        public EventHandlerKind HandlerKind { get; }
        public ISymbol? HandlerIdentity { get; }
        public string HandlerDisplay { get; }
        public Location Location { get; }
    }

    private static void CollectEventAccess(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<EventAccessRecord> records,
        bool isSubscription)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // A field-like event binds to an IEventSymbol; C# forbids assigning another type's
        // event, so the cross-type += leak on a public delegate member binds the field or
        // property itself — accept both.
        if (semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is not { } leftSymbol ||
            !IsSubscribableMember(leftSymbol))
        {
            return;
        }

        AddRecord(
            context, records, isSubscription, assignment,
            leftSymbol, assignment.Left, assignment.Right, semanticModel);
    }

    /// <summary>
    /// Collects the delegate-typed twin of the <c>+=</c> shape: a self-assignment of the form
    /// <c>_bus.Handlers = (EventHandler)Delegate.Combine(_bus.Handlers, OnMessage)</c>. Only the
    /// trivially-provable form — the first Combine argument is the same member the result is
    /// assigned back to — is recorded; <c>Delegate.Remove</c> mirrors it as an unsubscription.
    /// Anything indirect (result stored elsewhere, a different first argument) stays silent.
    /// </summary>
    private static void CollectDelegateCombineAccess(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<EventAccessRecord> subscriptions,
        ConcurrentBag<EventAccessRecord> removals)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        if (semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } leftSymbol ||
            !IsDelegateTypedMember(leftSymbol))
        {
            return;
        }

        if (PeelCastsAndParens(assignment.Right) is not InvocationExpressionSyntax invocation ||
            invocation.ArgumentList.Arguments.Count != 2 ||
            semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol combineMethod ||
            !IsDelegateCombineOrRemove(combineMethod, out var isSubscription))
        {
            return;
        }

        // The first argument must be the same member the result is assigned back to, reached
        // through the same receiver — otherwise the assignment does not accumulate onto the LHS.
        var firstArgument = PeelCastsAndParens(invocation.ArgumentList.Arguments[0].Expression);
        if (semanticModel.GetSymbolInfo(firstArgument, cancellationToken).Symbol is not { } firstArgumentSymbol ||
            !SymbolEqualityComparer.Default.Equals(firstArgumentSymbol, leftSymbol) ||
            !ReceiversAreSameRoot(assignment.Left, firstArgument, leftSymbol, semanticModel, cancellationToken))
        {
            return;
        }

        AddRecord(
            context, isSubscription ? subscriptions : removals, isSubscription, assignment,
            leftSymbol, assignment.Left, invocation.ArgumentList.Arguments[1].Expression, semanticModel);
    }

    private static void AddRecord(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<EventAccessRecord> records,
        bool isSubscription,
        AssignmentExpressionSyntax assignment,
        ISymbol member,
        ExpressionSyntax receiverExpression,
        ExpressionSyntax handlerExpression,
        SemanticModel semanticModel)
    {
        var containingType = GetEnclosingNamedType(assignment, semanticModel, context.CancellationToken);
        if (containingType is null)
        {
            return;
        }

        var (receiverKind, receiverRoot, receiverSegments, publisherType) = EventReceiverClassification.ClassifyReceiver(
            receiverExpression, member, containingType, semanticModel, context.CancellationToken);

        var (handlerKind, handlerIdentity, handlerDisplay) = EventHandlerClassification.ClassifyHandler(
            handlerExpression, containingType, semanticModel, context.CancellationToken);

        if (isSubscription &&
            (receiverKind == EventReceiverKind.Unknown || handlerKind == EventHandlerKind.Unknown))
        {
            // Silence-on-unknown: an unprovable subscription is never a candidate. Removals of
            // unknown shape are still recorded so they can suppress or feed the ineffective-
            // unsubscribe arm when their identity happens to resolve.
            return;
        }

        records.Add(new EventAccessRecord(
            containingType,
            member,
            receiverKind,
            receiverRoot,
            receiverSegments,
            publisherType,
            handlerKind,
            handlerIdentity,
            handlerDisplay,
            assignment.GetLocation()));
    }

    private static bool IsSubscribableMember(ISymbol symbol) =>
        symbol is IEventSymbol || IsDelegateTypedMember(symbol);

    private static bool IsDelegateTypedMember(ISymbol symbol) => symbol switch
    {
        IFieldSymbol field => field.Type.TypeKind == TypeKind.Delegate,
        IPropertySymbol property => property.Type.TypeKind == TypeKind.Delegate,
        _ => false
    };

    private static bool IsDelegateCombineOrRemove(IMethodSymbol method, out bool isSubscription)
    {
        isSubscription = method.Name == "Combine";
        return (isSubscription || method.Name == "Remove") &&
               method.ContainingType is { Name: "Delegate", ContainingNamespace.Name: "System" };
    }

    /// <summary>
    /// Confirms the LHS member access and the first Combine argument reach the member through
    /// the same receiver root (both <c>_bus.Handlers</c>), so the assignment accumulates onto
    /// the very member it reads. Direct <c>this</c>-qualified access on both sides also matches.
    /// </summary>
    private static bool ReceiversAreSameRoot(
        ExpressionSyntax left,
        ExpressionSyntax firstArgument,
        ISymbol member,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (member.IsStatic)
        {
            return true;
        }

        var leftReceiver = (left as MemberAccessExpressionSyntax)?.Expression;
        var firstReceiver = (firstArgument as MemberAccessExpressionSyntax)?.Expression;

        if (leftReceiver is null || firstReceiver is null)
        {
            // Both sides unqualified (`Handlers = Combine(Handlers, ...)` on `this`) is a match;
            // a mismatch of qualification shapes is not provably the same receiver.
            return leftReceiver is null && firstReceiver is null;
        }

        var leftRoot = semanticModel.GetSymbolInfo(leftReceiver, cancellationToken).Symbol;
        var firstRoot = semanticModel.GetSymbolInfo(firstReceiver, cancellationToken).Symbol;
        return leftRoot is not null &&
               SymbolEqualityComparer.Default.Equals(leftRoot, firstRoot);
    }

    private static ExpressionSyntax PeelCastsAndParens(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                default:
                    return expression;
            }
        }
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
        ConcurrentBag<EventAccessRecord> subscriptions,
        ConcurrentBag<EventAccessRecord> removals)
    {
        if (subscriptions.IsEmpty)
        {
            return;
        }

        var registrations = registrationCollector.AllRegistrations.ToList();
        var subscriberRanks = BuildSubscriberRankMap(registrations);
        var removalList = removals.ToList();
        var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        foreach (var subscription in subscriptions)
        {
            if (!IsReportableSubscription(
                    subscription, registrations, subscriberRanks, lifetimeClassifier,
                    context.Compilation, semanticModelsByTree, context.CancellationToken,
                    out var subscriberRank, out var isScopedPublisherTier))
            {
                continue;
            }

            if (TryMatchRemoval(
                    subscription, removalList, context.Compilation, semanticModelsByTree,
                    context.CancellationToken, out var ineffectiveAnonymousRemoval))
            {
                continue;
            }

            context.ReportDiagnostic(
                CreateDiagnostic(subscription, subscriberRank, isScopedPublisherTier, ineffectiveAnonymousRemoval));
        }
    }

    /// <summary>
    /// Applies the lifetime-rank, EventSource quiet-list, injected-member, and chain-stability
    /// gates that decide whether a subscription is a reportable cross-lifetime leak. Reports the
    /// subscriber's rank and whether the publisher is the scoped (DI026 Info) tier.
    /// </summary>
    private static bool IsReportableSubscription(
        EventAccessRecord subscription,
        List<ServiceRegistration> registrations,
        Dictionary<INamedTypeSymbol, int> subscriberRanks,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken,
        out int subscriberRank,
        out bool isScopedPublisherTier)
    {
        isScopedPublisherTier = false;

        if (!subscriberRanks.TryGetValue(subscription.ContainingType, out subscriberRank) ||
            subscriberRank >= RankOf(ServiceLifetime.Singleton))
        {
            return false;
        }

        // Provably process-lifetime publishers (singleton registrations and static events)
        // report DI025 at Warning; a scoped publisher whose growth is bounded by its scope
        // reports the DI026 Info tier. Equal-rank pairs (scoped/scoped) resolve from the
        // same scope and are torn down together, so they stay silent.
        var publisherRank = ResolvePublisherRank(subscription, registrations, lifetimeClassifier);
        if (publisherRank is null ||
            publisherRank < RankOf(ServiceLifetime.Scoped) ||
            publisherRank <= subscriberRank)
        {
            return false;
        }

        isScopedPublisherTier = publisherRank == RankOf(ServiceLifetime.Scoped);

        if (IsEventSourcePublisher(subscription.Member.ContainingType))
        {
            return false;
        }

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

    /// <summary>
    /// Scans the recorded removals for one that cancels the subscription. Returns true when a
    /// matching-identity unsubscription suppresses the leak; otherwise reports the first
    /// ineffective anonymous removal (a <c>-= (s, e) =&gt; ...</c> that never removes the handler).
    /// </summary>
    private static bool TryMatchRemoval(
        EventAccessRecord subscription,
        List<EventAccessRecord> removalList,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken,
        out EventAccessRecord? ineffectiveAnonymousRemoval)
    {
        ineffectiveAnonymousRemoval = null;

        var canonicalRoot = EventReceiverClassification.CanonicalizeReceiverRoot(
            subscription.ReceiverKind, subscription.ReceiverRoot, compilation, semanticModelsByTree, cancellationToken);

        foreach (var removal in removalList)
        {
            if (!AreRelatedTypes(subscription.ContainingType, removal.ContainingType) ||
                !SymbolEqualityComparer.Default.Equals(subscription.Member, removal.Member))
            {
                continue;
            }

            var removalRoot = EventReceiverClassification.CanonicalizeReceiverRoot(
                removal.ReceiverKind, removal.ReceiverRoot, compilation, semanticModelsByTree, cancellationToken);

            if (!RemovalTargetsSameReceiver(subscription, removal, canonicalRoot, removalRoot))
            {
                continue;
            }

            if (EventHandlerClassification.HandlerIdentitiesMatch(subscription.HandlerIdentity, removal.HandlerIdentity))
            {
                return true;
            }

            if (IsIneffectiveAnonymousRemoval(subscription, removal))
            {
                ineffectiveAnonymousRemoval ??= removal;
            }
        }

        return false;
    }

    private static bool RemovalTargetsSameReceiver(
        EventAccessRecord subscription,
        EventAccessRecord removal,
        ISymbol? canonicalRoot,
        ISymbol? removalRoot)
    {
        var rootsMatch = subscription.ReceiverKind == EventReceiverKind.StaticEvent
            ? removal.ReceiverKind == EventReceiverKind.StaticEvent
            : removalRoot is not null &&
              canonicalRoot is not null &&
              SymbolEqualityComparer.Default.Equals(removalRoot, canonicalRoot);

        return rootsMatch &&
               EventReceiverClassification.ReceiverSegmentsMatch(subscription.ReceiverSegments, removal.ReceiverSegments);
    }

    private static bool IsIneffectiveAnonymousRemoval(EventAccessRecord subscription, EventAccessRecord removal) =>
        subscription.HandlerKind == EventHandlerKind.AnonymousCapturingInstance &&
        removal.HandlerIdentity is null &&
        removal.HandlerKind is EventHandlerKind.AnonymousCapturingInstance or EventHandlerKind.Unknown &&
        EventHandlerClassification.IsAnonymousRemoval(removal.HandlerKind, removal.HandlerDisplay);

    private static Diagnostic CreateDiagnostic(
        EventAccessRecord subscription,
        int subscriberRank,
        bool isScopedPublisherTier,
        EventAccessRecord? ineffectiveAnonymousRemoval)
    {
        if (isScopedPublisherTier)
        {
            // The scoped tier only ever fires for transient subscribers (equal ranks stay
            // silent), so its message formats omit the subscriber-lifetime placeholder.
            var scopedPublisherText = subscription.ReceiverSegments.IsEmpty
                ? $"the scoped service '{subscription.PublisherType?.Name}'"
                : $"'{subscription.Member.ContainingType.Name}' held by the scoped service '{subscription.PublisherType?.Name}'";

            if (subscription.HandlerKind == EventHandlerKind.AnonymousCapturingInstance)
            {
                if (ineffectiveAnonymousRemoval is not null)
                {
                    return Diagnostic.Create(
                        DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherIneffectiveUnsubscribe,
                        subscription.Location,
                        additionalLocations: new[] { ineffectiveAnonymousRemoval.Location },
                        subscription.ContainingType.Name,
                        subscription.Member.Name,
                        scopedPublisherText);
                }

                return Diagnostic.Create(
                    DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherAnonymousHandler,
                    subscription.Location,
                    subscription.ContainingType.Name,
                    subscription.Member.Name,
                    scopedPublisherText);
            }

            return Diagnostic.Create(
                DiagnosticDescriptors.EventSubscriptionLeakScopedPublisher,
                subscription.Location,
                subscription.ContainingType.Name,
                subscription.HandlerDisplay,
                subscription.Member.Name,
                scopedPublisherText);
        }

        var lifetimeText = subscriberRank == RankOf(ServiceLifetime.Transient) ? "transient" : "scoped";
        var publisherText = subscription.ReceiverKind == EventReceiverKind.StaticEvent
            ? $"the static event publisher '{subscription.Member.ContainingType.Name}'"
            : subscription.ReceiverSegments.IsEmpty
                ? $"the singleton service '{subscription.PublisherType?.Name}'"
                : $"'{subscription.Member.ContainingType.Name}' held by the singleton service '{subscription.PublisherType?.Name}'";

        if (subscription.HandlerKind == EventHandlerKind.AnonymousCapturingInstance)
        {
            if (ineffectiveAnonymousRemoval is not null)
            {
                return Diagnostic.Create(
                    DiagnosticDescriptors.EventSubscriptionLeakIneffectiveUnsubscribe,
                    subscription.Location,
                    additionalLocations: new[] { ineffectiveAnonymousRemoval.Location },
                    subscription.ContainingType.Name,
                    lifetimeText,
                    subscription.Member.Name,
                    publisherText);
            }

            return Diagnostic.Create(
                DiagnosticDescriptors.EventSubscriptionLeakAnonymousHandler,
                subscription.Location,
                subscription.ContainingType.Name,
                lifetimeText,
                subscription.Member.Name,
                publisherText);
        }

        return Diagnostic.Create(
            DiagnosticDescriptors.EventSubscriptionLeak,
            subscription.Location,
            subscription.ContainingType.Name,
            lifetimeText,
            subscription.HandlerDisplay,
            subscription.Member.Name,
            publisherText);
    }

    private static int RankOf(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.Transient => 0,
        ServiceLifetime.Scoped => 1,
        _ => 2
    };

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
    /// Resolves the publisher's lifetime rank. Static events rank above every registration.
    /// Registered publishers use the most conservative (shortest-lived) registration for the
    /// service type; unregistered publishers fall back to the known framework classifier.
    /// </summary>
    private static int? ResolvePublisherRank(
        EventAccessRecord subscription,
        List<ServiceRegistration> registrations,
        KnownServiceLifetimeClassifier lifetimeClassifier)
    {
        if (subscription.ReceiverKind == EventReceiverKind.StaticEvent)
        {
            return StaticPublisherRank;
        }

        if (subscription.PublisherType is null)
        {
            return null;
        }

        // Exact closed registrations win over open-generic fallbacks (the DI017 precedent);
        // multiple matches take the most conservative (shortest-lived) lifetime.
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

    private static bool IsEventSourcePublisher(INamedTypeSymbol publisherType)
    {
        for (var current = publisherType; current is not null; current = current.BaseType)
        {
            if (current.Name == "EventSource" &&
                current.ContainingNamespace.ToDisplayString() == "System.Diagnostics.Tracing")
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreRelatedTypes(INamedTypeSymbol left, INamedTypeSymbol right) =>
        EventReceiverClassification.IsTypeOrBase(left, right) ||
        EventReceiverClassification.IsTypeOrBase(right, left);
}
