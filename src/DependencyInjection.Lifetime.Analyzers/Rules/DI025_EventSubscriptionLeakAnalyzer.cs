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
    private const string AnonymousHandlerDisplay = "an anonymous handler";

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

            compilationContext.RegisterCompilationEndAction(endContext =>
                ReportLeakedSubscriptions(endContext, registrationCollector, lifetimeClassifier, subscriptions, removals));
        });
    }

    private enum ReceiverKind
    {
        Unknown,
        StaticEvent,
        InjectedMember,
        ConstructorParameter
    }

    private enum HandlerKind
    {
        Unknown,
        InstanceMethodGroup,
        AnonymousCapturingInstance,
        StoredDelegateMember
    }

    private sealed class EventAccessRecord
    {
        public EventAccessRecord(
            INamedTypeSymbol containingType,
            IEventSymbol eventSymbol,
            ReceiverKind receiverKind,
            ISymbol? receiverRoot,
            ImmutableArray<ISymbol> receiverSegments,
            INamedTypeSymbol? publisherType,
            HandlerKind handlerKind,
            ISymbol? handlerIdentity,
            string handlerDisplay,
            Location location)
        {
            ContainingType = containingType;
            EventSymbol = eventSymbol;
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
        public IEventSymbol EventSymbol { get; }
        public ReceiverKind ReceiverKind { get; }
        public ISymbol? ReceiverRoot { get; }

        /// <summary>
        /// Intermediate members between the chain root and the event, in access order
        /// (<c>_outer.Inner.Changed</c> stores <c>Inner</c>). Empty for direct receivers.
        /// </summary>
        public ImmutableArray<ISymbol> ReceiverSegments { get; }

        public INamedTypeSymbol? PublisherType { get; }
        public HandlerKind HandlerKind { get; }
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

        if (semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is not IEventSymbol eventSymbol)
        {
            return;
        }

        var containingType = GetEnclosingNamedType(assignment, semanticModel, context.CancellationToken);
        if (containingType is null)
        {
            return;
        }

        var (receiverKind, receiverRoot, receiverSegments, publisherType) = ClassifyReceiver(
            assignment.Left, eventSymbol, containingType, semanticModel, context.CancellationToken);

        var (handlerKind, handlerIdentity, handlerDisplay) = ClassifyHandler(
            assignment.Right, containingType, semanticModel, context.CancellationToken);

        if (isSubscription &&
            (receiverKind == ReceiverKind.Unknown || handlerKind == HandlerKind.Unknown))
        {
            // Silence-on-unknown: an unprovable subscription is never a candidate. Removals of
            // unknown shape are still recorded so they can suppress or feed the ineffective-
            // unsubscribe arm when their identity happens to resolve.
            return;
        }

        records.Add(new EventAccessRecord(
            containingType,
            eventSymbol,
            receiverKind,
            receiverRoot,
            receiverSegments,
            publisherType,
            handlerKind,
            handlerIdentity,
            handlerDisplay,
            assignment.GetLocation()));
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

    private static (ReceiverKind Kind, ISymbol? Root, ImmutableArray<ISymbol> Segments, INamedTypeSymbol? PublisherType) ClassifyReceiver(
        ExpressionSyntax left,
        IEventSymbol eventSymbol,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (eventSymbol.IsStatic)
        {
            return (ReceiverKind.StaticEvent, null, ImmutableArray<ISymbol>.Empty, eventSymbol.ContainingType);
        }

        if (left is not MemberAccessExpressionSyntax memberAccess)
        {
            // Unqualified event access binds to an event on `this`; a subscriber holding
            // itself is not a cross-lifetime edge.
            return (ReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null);
        }

        var receiverExpression = UnwrapReceiver(memberAccess.Expression);

        if (receiverExpression is MemberAccessExpressionSyntax chain)
        {
            return ClassifyChainedReceiver(chain, containingType, semanticModel, cancellationToken);
        }

        var (kind, root, publisherType) = ClassifyReceiverRoot(
            receiverExpression, containingType, semanticModel, cancellationToken);
        return (kind, root, ImmutableArray<ISymbol>.Empty, publisherType);
    }

    /// <summary>
    /// Classifies a multi-segment receiver (<c>_outer.Inner.Changed += H</c>). The chain
    /// anchors on the root member's declared type — the publisher rank comes from the root's
    /// registration — and records every intermediate segment so the compilation-end pass can
    /// prove each one is a stable projection of the root before reporting.
    /// </summary>
    private static (ReceiverKind Kind, ISymbol? Root, ImmutableArray<ISymbol> Segments, INamedTypeSymbol? PublisherType) ClassifyChainedReceiver(
        MemberAccessExpressionSyntax chain,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var segments = new List<ISymbol>();
        var current = chain;

        while (true)
        {
            var inner = UnwrapReceiver(current.Expression);

            if (inner is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                // `base.Bus` is the root member itself, not a segment above one; classify it
                // directly so base-qualified injected receivers keep their direct shape.
                var (baseKind, baseRoot, basePublisherType) = ClassifyReceiverRoot(
                    current, containingType, semanticModel, cancellationToken);
                return baseKind == ReceiverKind.Unknown
                    ? (ReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null)
                    : (baseKind, baseRoot, segments.ToImmutableArray(), basePublisherType);
            }

            if (semanticModel.GetSymbolInfo(current, cancellationToken).Symbol
                    is not { IsStatic: false } segmentSymbol ||
                segmentSymbol is not (IFieldSymbol or IPropertySymbol))
            {
                return (ReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null);
            }

            segments.Insert(0, segmentSymbol);

            if (inner is MemberAccessExpressionSyntax nextChain)
            {
                current = nextChain;
                continue;
            }

            var (kind, root, publisherType) = ClassifyReceiverRoot(
                inner, containingType, semanticModel, cancellationToken);
            return kind == ReceiverKind.Unknown
                ? (ReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null)
                : (kind, root, segments.ToImmutableArray(), publisherType);
        }
    }

    private static (ReceiverKind Kind, ISymbol? Root, INamedTypeSymbol? PublisherType) ClassifyReceiverRoot(
        ExpressionSyntax receiverExpression,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverSymbol = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol;

        if (receiverSymbol is ILocalSymbol local)
        {
            receiverSymbol = ResolveLocalAlias(local, receiverExpression, semanticModel, cancellationToken);
        }

        switch (receiverSymbol)
        {
            case IFieldSymbol field when !field.IsStatic && IsTypeOrBase(containingType, field.ContainingType):
                return field.Type is INamedTypeSymbol fieldType
                    ? (ReceiverKind.InjectedMember, field, fieldType)
                    : (ReceiverKind.Unknown, null, null);

            case IPropertySymbol property when !property.IsStatic && IsTypeOrBase(containingType, property.ContainingType):
                return property.Type is INamedTypeSymbol propertyType
                    ? (ReceiverKind.InjectedMember, property, propertyType)
                    : (ReceiverKind.Unknown, null, null);

            case IParameterSymbol parameter when
                parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor &&
                IsTypeOrBase(containingType, ctor.ContainingType):
                return parameter.Type is INamedTypeSymbol parameterType
                    ? (ReceiverKind.ConstructorParameter, parameter, parameterType)
                    : (ReceiverKind.Unknown, null, null);

            default:
                return (ReceiverKind.Unknown, null, null);
        }
    }

    private static ExpressionSyntax UnwrapReceiver(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed:
                    expression = suppressed.Operand;
                    continue;
                case MemberAccessExpressionSyntax thisAccess when thisAccess.Expression is ThisExpressionSyntax:
                    return thisAccess.Name;
                default:
                    return expression;
            }
        }
    }

    /// <summary>
    /// Resolves a simple same-method local alias of an injected member (<c>var bus = _bus;</c>).
    /// The alias qualifies only when its single declaration initializer is a plain member or
    /// parameter reference and the local is never reassigned in the enclosing member body.
    /// </summary>
    private static ISymbol? ResolveLocalAlias(
        ILocalSymbol local,
        ExpressionSyntax use,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator ||
            declarator.Initializer is null)
        {
            return null;
        }

        var enclosingBody = ExecutableBodyOf(declarator);
        if (enclosingBody is null || ExecutableBodyOf(use) != enclosingBody)
        {
            return null;
        }

        foreach (var candidate in enclosingBody.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(candidate.Left, cancellationToken).Symbol is ILocalSymbol assigned &&
                SymbolEqualityComparer.Default.Equals(assigned, local))
            {
                return null;
            }
        }

        var initializer = UnwrapReceiver(declarator.Initializer.Value);
        var initializerSymbol = semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol;
        return initializerSymbol is IFieldSymbol or IPropertySymbol or IParameterSymbol
            ? initializerSymbol
            : null;
    }

    private static SyntaxNode? ExecutableBodyOf(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(ancestor =>
            ancestor is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    private static (HandlerKind Kind, ISymbol? Identity, string Display) ClassifyHandler(
        ExpressionSyntax handler,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        handler = UnwrapHandler(handler);

        if (handler is AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return AnonymousFunctionCapturesInstance(anonymousFunction, containingType, semanticModel, cancellationToken)
                ? (HandlerKind.AnonymousCapturingInstance, null, AnonymousHandlerDisplay)
                : (HandlerKind.Unknown, null, AnonymousHandlerDisplay);
        }

        var handlerSymbol = semanticModel.GetSymbolInfo(handler, cancellationToken).Symbol;

        switch (handlerSymbol)
        {
            case IMethodSymbol method when !method.IsStatic && IsTypeOrBase(containingType, method.ContainingType):
                if (handler is MemberAccessExpressionSyntax methodAccess &&
                    methodAccess.Expression is not ThisExpressionSyntax)
                {
                    // `other.OnMessage` roots `other`, not the subscriber instance.
                    return (HandlerKind.Unknown, null, string.Empty);
                }

                return (HandlerKind.InstanceMethodGroup, NormalizeMethod(method), method.Name);

            case IFieldSymbol field when !field.IsStatic && IsTypeOrBase(containingType, field.ContainingType):
                return StoredDelegateCapturesInstance(field, containingType, semanticModel.Compilation, cancellationToken)
                    ? (HandlerKind.StoredDelegateMember, field, field.Name)
                    : (HandlerKind.Unknown, null, string.Empty);

            case ILocalSymbol storedLocal:
                return StoredLocalCapturesInstance(storedLocal, containingType, semanticModel, cancellationToken)
                    ? (HandlerKind.StoredDelegateMember, storedLocal, storedLocal.Name)
                    : (HandlerKind.Unknown, null, string.Empty);

            default:
                return (HandlerKind.Unknown, null, string.Empty);
        }
    }

    /// <summary>
    /// Normalizes an override chain to its root declaration so a subscription through a
    /// derived override matches an unsubscription written against the base declaration.
    /// </summary>
    private static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        while (method.OverriddenMethod is not null)
        {
            method = method.OverriddenMethod;
        }

        return method;
    }

    private static ExpressionSyntax UnwrapHandler(ExpressionSyntax handler)
    {
        while (true)
        {
            switch (handler)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    handler = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    handler = cast.Expression;
                    continue;
                case ObjectCreationExpressionSyntax creation when
                    creation.ArgumentList is { Arguments.Count: 1 }:
                    handler = creation.ArgumentList.Arguments[0].Expression;
                    continue;
                default:
                    return handler;
            }
        }
    }

    private static bool AnonymousFunctionCapturesInstance(
        AnonymousFunctionExpressionSyntax anonymousFunction,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var node in anonymousFunction.DescendantNodes())
        {
            if (node is ThisExpressionSyntax)
            {
                return true;
            }

            if (node is not IdentifierNameSyntax identifier)
            {
                continue;
            }

            // A qualified name (`receiver.Member`) does not bind through the implicit `this`.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == identifier &&
                memberAccess.Expression is not ThisExpressionSyntax)
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol &&
                !symbol.IsStatic &&
                IsTypeOrBase(containingType, symbol.ContainingType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A delegate field counts as instance-capturing only when it has at least one initializer
    /// or assignment in source and every one of them provably binds an instance method group of
    /// the subscriber or an instance-capturing anonymous function.
    /// </summary>
    private static bool StoredDelegateCapturesInstance(
        IFieldSymbol field,
        INamedTypeSymbol containingType,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var sawValue = false;

        foreach (var (expression, model) in EnumerateMemberValueSources(field, compilation, cancellationToken))
        {
            sawValue = true;
            if (!ExpressionCapturesInstance(expression, containingType, model, cancellationToken))
            {
                return false;
            }
        }

        return sawValue;
    }

    private static bool StoredLocalCapturesInstance(
        ILocalSymbol local,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator)
        {
            return false;
        }

        var enclosingBody = ExecutableBodyOf(declarator);
        if (enclosingBody is null)
        {
            return false;
        }

        var sawValue = false;

        if (declarator.Initializer is not null)
        {
            sawValue = true;
            if (!ExpressionCapturesInstance(declarator.Initializer.Value, containingType, semanticModel, cancellationToken))
            {
                return false;
            }
        }

        foreach (var assignment in enclosingBody.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol assigned ||
                !SymbolEqualityComparer.Default.Equals(assigned, local))
            {
                continue;
            }

            sawValue = true;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !ExpressionCapturesInstance(assignment.Right, containingType, semanticModel, cancellationToken))
            {
                return false;
            }
        }

        return sawValue;
    }

    private static bool ExpressionCapturesInstance(
        ExpressionSyntax expression,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapHandler(expression);

        if (expression is AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return AnonymousFunctionCapturesInstance(anonymousFunction, containingType, semanticModel, cancellationToken);
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol
        {
            IsStatic: false
        } method && IsTypeOrBase(containingType, method.ContainingType);
    }

    private static SemanticModel GetSemanticModel(
        SyntaxTree syntaxTree,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        #pragma warning disable RS1030 // Compilation-end analysis needs models for foreign trees.
        return semanticModelsByTree.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
        #pragma warning restore RS1030
    }

    private static IEnumerable<(ExpressionSyntax Expression, SemanticModel Model)> EnumerateMemberValueSources(
        ISymbol member,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var modelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        SemanticModel ModelFor(SyntaxTree tree) => GetSemanticModel(tree, compilation, modelsByTree);

        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: not null } declarator)
            {
                yield return (declarator.Initializer.Value, ModelFor(declarator.SyntaxTree));
            }
        }

        foreach (var typeReference in member.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            var model = ModelFor(typeDeclaration.SyntaxTree);
            foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var target = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (target is null || !SymbolEqualityComparer.Default.Equals(target, member))
                {
                    continue;
                }

                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    // Compound writes make the member's value unprovable.
                    yield return (assignment, model);
                    continue;
                }

                yield return (assignment.Right, model);
            }
        }
    }

    private static bool IsTypeOrBase(INamedTypeSymbol type, INamedTypeSymbol candidateOwner)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidateOwner))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreRelatedTypes(INamedTypeSymbol left, INamedTypeSymbol right) =>
        IsTypeOrBase(left, right) || IsTypeOrBase(right, left);

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
            if (!subscriberRanks.TryGetValue(subscription.ContainingType, out var subscriberRank) ||
                subscriberRank >= RankOf(ServiceLifetime.Singleton))
            {
                continue;
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
                continue;
            }

            var isScopedPublisherTier = publisherRank == RankOf(ServiceLifetime.Scoped);

            if (IsEventSourcePublisher(subscription.EventSymbol.ContainingType))
            {
                continue;
            }

            var canonicalRoot = CanonicalizeReceiverRoot(subscription, context.Compilation, semanticModelsByTree, context.CancellationToken);
            if (subscription.ReceiverKind == ReceiverKind.InjectedMember &&
                !MemberIsProvablyInjected(subscription, context.Compilation, semanticModelsByTree, context.CancellationToken))
            {
                continue;
            }

            if (!subscription.ReceiverSegments.IsEmpty &&
                !ChainSegmentsAreStableProjections(subscription, registrations, context.Compilation, semanticModelsByTree, context.CancellationToken))
            {
                continue;
            }

            EventAccessRecord? ineffectiveAnonymousRemoval = null;
            var suppressed = false;

            foreach (var removal in removalList)
            {
                if (!AreRelatedTypes(subscription.ContainingType, removal.ContainingType) ||
                    !SymbolEqualityComparer.Default.Equals(subscription.EventSymbol, removal.EventSymbol))
                {
                    continue;
                }

                var removalRoot = CanonicalizeReceiverRoot(removal, context.Compilation, semanticModelsByTree, context.CancellationToken);
                var rootsMatch = subscription.ReceiverKind == ReceiverKind.StaticEvent
                    ? removal.ReceiverKind == ReceiverKind.StaticEvent
                    : removalRoot is not null &&
                      canonicalRoot is not null &&
                      SymbolEqualityComparer.Default.Equals(removalRoot, canonicalRoot);

                if (!rootsMatch || !ReceiverSegmentsMatch(subscription, removal))
                {
                    continue;
                }

                if (HandlerIdentitiesMatch(subscription, removal))
                {
                    suppressed = true;
                    break;
                }

                if (subscription.HandlerKind == HandlerKind.AnonymousCapturingInstance &&
                    removal.HandlerIdentity is null &&
                    removal.HandlerKind is HandlerKind.AnonymousCapturingInstance or HandlerKind.Unknown &&
                    IsAnonymousRemoval(removal))
                {
                    ineffectiveAnonymousRemoval ??= removal;
                }
            }

            if (suppressed)
            {
                continue;
            }

            context.ReportDiagnostic(
                CreateDiagnostic(subscription, subscriberRank, isScopedPublisherTier, ineffectiveAnonymousRemoval));
        }
    }

    private static bool IsAnonymousRemoval(EventAccessRecord removal) =>
        removal.HandlerKind == HandlerKind.AnonymousCapturingInstance ||
        removal.HandlerDisplay == "an anonymous handler";

    private static bool HandlerIdentitiesMatch(EventAccessRecord subscription, EventAccessRecord removal) =>
        subscription.HandlerIdentity is not null &&
        removal.HandlerIdentity is not null &&
        SymbolEqualityComparer.Default.Equals(subscription.HandlerIdentity, removal.HandlerIdentity);

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
                : $"'{subscription.EventSymbol.ContainingType.Name}' held by the scoped service '{subscription.PublisherType?.Name}'";

            if (subscription.HandlerKind == HandlerKind.AnonymousCapturingInstance)
            {
                if (ineffectiveAnonymousRemoval is not null)
                {
                    return Diagnostic.Create(
                        DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherIneffectiveUnsubscribe,
                        subscription.Location,
                        additionalLocations: new[] { ineffectiveAnonymousRemoval.Location },
                        subscription.ContainingType.Name,
                        subscription.EventSymbol.Name,
                        scopedPublisherText);
                }

                return Diagnostic.Create(
                    DiagnosticDescriptors.EventSubscriptionLeakScopedPublisherAnonymousHandler,
                    subscription.Location,
                    subscription.ContainingType.Name,
                    subscription.EventSymbol.Name,
                    scopedPublisherText);
            }

            return Diagnostic.Create(
                DiagnosticDescriptors.EventSubscriptionLeakScopedPublisher,
                subscription.Location,
                subscription.ContainingType.Name,
                subscription.HandlerDisplay,
                subscription.EventSymbol.Name,
                scopedPublisherText);
        }

        var lifetimeText = subscriberRank == RankOf(ServiceLifetime.Transient) ? "transient" : "scoped";
        var publisherText = subscription.ReceiverKind == ReceiverKind.StaticEvent
            ? $"the static event publisher '{subscription.EventSymbol.ContainingType.Name}'"
            : subscription.ReceiverSegments.IsEmpty
                ? $"the singleton service '{subscription.PublisherType?.Name}'"
                : $"'{subscription.EventSymbol.ContainingType.Name}' held by the singleton service '{subscription.PublisherType?.Name}'";

        if (subscription.HandlerKind == HandlerKind.AnonymousCapturingInstance)
        {
            if (ineffectiveAnonymousRemoval is not null)
            {
                return Diagnostic.Create(
                    DiagnosticDescriptors.EventSubscriptionLeakIneffectiveUnsubscribe,
                    subscription.Location,
                    additionalLocations: new[] { ineffectiveAnonymousRemoval.Location },
                    subscription.ContainingType.Name,
                    lifetimeText,
                    subscription.EventSymbol.Name,
                    publisherText);
            }

            return Diagnostic.Create(
                DiagnosticDescriptors.EventSubscriptionLeakAnonymousHandler,
                subscription.Location,
                subscription.ContainingType.Name,
                lifetimeText,
                subscription.EventSymbol.Name,
                publisherText);
        }

        return Diagnostic.Create(
            DiagnosticDescriptors.EventSubscriptionLeak,
            subscription.Location,
            subscription.ContainingType.Name,
            lifetimeText,
            subscription.HandlerDisplay,
            subscription.EventSymbol.Name,
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
        if (subscription.ReceiverKind == ReceiverKind.StaticEvent)
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

    /// <summary>
    /// Unifies constructor-parameter receivers with the field or property the parameter is
    /// stored into, so a subscription through the parameter matches an unsubscription through
    /// the field.
    /// </summary>
    private static ISymbol? CanonicalizeReceiverRoot(
        EventAccessRecord record,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (record.ReceiverKind != ReceiverKind.ConstructorParameter ||
            record.ReceiverRoot is not IParameterSymbol parameter)
        {
            return record.ReceiverRoot;
        }

        ISymbol? storedInto = null;

        foreach (var typeReference in parameter.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            var model = GetSemanticModel(typeDeclaration.SyntaxTree, compilation, semanticModelsByTree);
            foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    model.GetSymbolInfo(UnwrapReceiver(assignment.Right), cancellationToken).Symbol
                        is not IParameterSymbol sourceParameter ||
                    !SymbolEqualityComparer.Default.Equals(sourceParameter, parameter))
                {
                    continue;
                }

                var target = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (target is IFieldSymbol or IPropertySymbol)
                {
                    if (storedInto is not null &&
                        !SymbolEqualityComparer.Default.Equals(storedInto, target))
                    {
                        return record.ReceiverRoot;
                    }

                    storedInto = target;
                }
            }
        }

        return storedInto ?? record.ReceiverRoot;
    }

    /// <summary>
    /// An injected-member publisher qualifies only when every source-visible write to the
    /// member is a simple assignment from an instance-constructor parameter of the member's
    /// containing type, and at least one such write exists. Any initializer, compound write,
    /// or non-parameter source makes the publisher's origin unprovable.
    /// </summary>
    private static bool MemberIsProvablyInjected(
        EventAccessRecord subscription,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (subscription.ReceiverRoot is not ISymbol member)
        {
            return false;
        }

        var sawInjectedWrite = false;

        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            if (syntax is VariableDeclaratorSyntax { Initializer: not null } ||
                syntax is PropertyDeclarationSyntax { Initializer: not null })
            {
                return false;
            }
        }

        foreach (var typeReference in member.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            var model = GetSemanticModel(typeDeclaration.SyntaxTree, compilation, semanticModelsByTree);
            foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var target = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (target is null || !SymbolEqualityComparer.Default.Equals(target, member))
                {
                    continue;
                }

                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    return false;
                }

                if (model.GetSymbolInfo(UnwrapReceiver(assignment.Right), cancellationToken).Symbol
                        is not IParameterSymbol parameter ||
                    parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor ||
                    !SymbolEqualityComparer.Default.Equals(ctor.ContainingType, member.ContainingType))
                {
                    return false;
                }

                sawInjectedWrite = true;
            }
        }

        return sawInjectedWrite;
    }

    private static bool ReceiverSegmentsMatch(EventAccessRecord subscription, EventAccessRecord removal)
    {
        if (subscription.ReceiverSegments.Length != removal.ReceiverSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < subscription.ReceiverSegments.Length; i++)
        {
            if (!SegmentSymbolsMatch(subscription.ReceiverSegments[i], removal.ReceiverSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A += written through a concrete receiver binds the implementation member while a -=
    /// through an interface-typed alias of the same instance binds the interface member;
    /// both name the same projection, so segments match when one implements the other.
    /// </summary>
    private static bool SegmentSymbolsMatch(ISymbol subscription, ISymbol removal)
    {
        return SymbolEqualityComparer.Default.Equals(subscription, removal) ||
               ImplementsInterfaceSegment(subscription, removal) ||
               ImplementsInterfaceSegment(removal, subscription);
    }

    private static bool ImplementsInterfaceSegment(ISymbol implementation, ISymbol interfaceMember)
    {
        return interfaceMember.ContainingType.TypeKind == TypeKind.Interface &&
               SymbolEqualityComparer.Default.Equals(
                   implementation.ContainingType.FindImplementationForInterfaceMember(interfaceMember),
                   implementation);
    }

    /// <summary>
    /// A chained subscription only roots the subscriber when every segment between the chain
    /// root and the event provably returns the same instance for the root's whole lifetime:
    /// a readonly field, a get-only auto-property, or a getter that returns one. Interface
    /// segments are provable only on the first hop, through the root's registered
    /// implementation types; anything else (settable, computed, metadata-only, virtual)
    /// leaves the projection unstable and the subscription silent.
    /// </summary>
    private static bool ChainSegmentsAreStableProjections(
        EventAccessRecord subscription,
        List<ServiceRegistration> registrations,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        for (var i = 0; i < subscription.ReceiverSegments.Length; i++)
        {
            var segment = subscription.ReceiverSegments[i];

            if (segment is IFieldSymbol field)
            {
                if (!field.IsReadOnly)
                {
                    return false;
                }

                continue;
            }

            if (segment is not IPropertySymbol property)
            {
                return false;
            }

            if (property.ContainingType.TypeKind == TypeKind.Interface)
            {
                if (i != 0 ||
                    !InterfaceSegmentHasStableImplementations(
                        property, subscription.PublisherType, registrations, compilation, semanticModelsByTree, cancellationToken))
                {
                    return false;
                }

                continue;
            }

            if (property.IsAbstract || property.IsVirtual || property.IsOverride ||
                !PropertyIsStableProjection(property, compilation, semanticModelsByTree, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InterfaceSegmentHasStableImplementations(
        IPropertySymbol interfaceProperty,
        INamedTypeSymbol? rootServiceType,
        List<ServiceRegistration> registrations,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (rootServiceType is null)
        {
            return false;
        }

        // Exact closed registrations win over open-generic fallbacks, mirroring the
        // publisher-rank resolution (the DI017 precedent): a chain rooted in IOuter<Order>
        // registered via AddSingleton(typeof(IOuter<>), typeof(Outer<>)) proves its segments
        // through the constructed Outer<Order>.
        var matched = MatchingUnkeyedRegistrations(registrations, rootServiceType);
        var constructFromRoot = false;

        if (matched.Count == 0 &&
            rootServiceType.IsGenericType &&
            !rootServiceType.IsUnboundGenericType)
        {
            matched = MatchingUnkeyedRegistrations(registrations, rootServiceType.ConstructUnboundGenericType());
            constructFromRoot = true;
        }

        if (matched.Count == 0)
        {
            return false;
        }

        foreach (var registration in matched)
        {
            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                return false;
            }

            if (constructFromRoot)
            {
                if (implementationType.Arity != rootServiceType.Arity)
                {
                    return false;
                }

                implementationType = implementationType.OriginalDefinition
                    .Construct(rootServiceType.TypeArguments.ToArray());
            }

            if (implementationType.FindImplementationForInterfaceMember(interfaceProperty)
                    is not IPropertySymbol implementationProperty ||
                implementationProperty.IsAbstract || implementationProperty.IsVirtual ||
                !PropertyIsStableProjection(implementationProperty, compilation, semanticModelsByTree, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static List<ServiceRegistration> MatchingUnkeyedRegistrations(
        List<ServiceRegistration> registrations,
        INamedTypeSymbol serviceType)
    {
        var matched = new List<ServiceRegistration>();

        foreach (var registration in registrations)
        {
            if (!registration.IsKeyed &&
                SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType))
            {
                matched.Add(registration);
            }
        }

        return matched;
    }

    private static bool PropertyIsStableProjection(
        IPropertySymbol property,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (property.IsStatic ||
            property.GetMethod is null ||
            property.SetMethod is { IsInitOnly: false } ||
            property.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax declaration)
            {
                return false;
            }

            if (IsGetOnlyAutoProperty(declaration))
            {
                continue;
            }

            var returned = SingleReturnedExpression(declaration);
            if (returned is null)
            {
                return false;
            }

            var model = GetSemanticModel(declaration.SyntaxTree, compilation, semanticModelsByTree);
            if (!ReturnedExpressionIsStable(returned, model, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReturnedExpressionIsStable(
        ExpressionSyntax returned,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Only a simple self-reference (`_inner` / `this._inner`) qualifies: a member reached
        // through another receiver (`_holder.Inner`) may repoint when the holder is
        // reassigned, so the stable-looking leaf proves nothing on its own.
        var unwrapped = UnwrapReceiver(returned);
        if (unwrapped is not IdentifierNameSyntax)
        {
            return false;
        }

        return semanticModel.GetSymbolInfo(unwrapped, cancellationToken).Symbol switch
        {
            IFieldSymbol field => field.IsReadOnly && !field.IsStatic,
            IPropertySymbol property =>
                !property.IsStatic &&
                !property.IsVirtual && !property.IsAbstract && !property.IsOverride &&
                property.SetMethod is null &&
                property.GetMethod is not null &&
                !property.DeclaringSyntaxReferences.IsEmpty &&
                property.DeclaringSyntaxReferences.All(reference =>
                    reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax nested &&
                    IsGetOnlyAutoProperty(nested)),
            _ => false
        };
    }

    private static bool IsGetOnlyAutoProperty(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody is not null || declaration.AccessorList is null)
        {
            return false;
        }

        var sawAutoGetter = false;

        foreach (var accessor in declaration.AccessorList.Accessors)
        {
            if (accessor.Body is not null || accessor.ExpressionBody is not null)
            {
                return false;
            }

            switch (accessor.Kind())
            {
                case SyntaxKind.GetAccessorDeclaration:
                    sawAutoGetter = true;
                    break;
                case SyntaxKind.InitAccessorDeclaration:
                    break;
                default:
                    return false;
            }
        }

        return sawAutoGetter;
    }

    private static ExpressionSyntax? SingleReturnedExpression(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody is not null)
        {
            return declaration.ExpressionBody.Expression;
        }

        var getter = declaration.AccessorList?.Accessors
            .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

        if (getter?.ExpressionBody is not null)
        {
            return getter.ExpressionBody.Expression;
        }

        return getter?.Body is { Statements.Count: 1 } body &&
               body.Statements[0] is ReturnStatementSyntax { Expression: { } returnedExpression }
            ? returnedExpression
            : null;
    }
}
