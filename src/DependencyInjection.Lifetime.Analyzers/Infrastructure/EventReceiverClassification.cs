using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// The provable shape of an event subscription's receiver (the left-hand side of
/// <c>publisher.Changed += H</c>) as classified for DI025.
/// </summary>
internal enum EventReceiverKind
{
    Unknown,
    StaticEvent,
    InjectedMember,
    ConstructorParameter
}

/// <summary>
/// Receiver-side classification for the DI025 event-subscription-leak analysis: proving what a
/// subscription's publisher expression binds to, canonicalizing constructor-parameter receivers
/// to their backing field, and proving that chained receiver projections are stable.
/// </summary>
internal static class EventReceiverClassification
{
    public static (EventReceiverKind Kind, ISymbol? Root, ImmutableArray<ISymbol> Segments, INamedTypeSymbol? PublisherType) ClassifyReceiver(
        ExpressionSyntax left,
        IEventSymbol eventSymbol,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (eventSymbol.IsStatic)
        {
            return (EventReceiverKind.StaticEvent, null, ImmutableArray<ISymbol>.Empty, eventSymbol.ContainingType);
        }

        if (left is not MemberAccessExpressionSyntax memberAccess)
        {
            // Unqualified event access binds to an event on `this`; a subscriber holding
            // itself is not a cross-lifetime edge.
            return (EventReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null);
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
    private static (EventReceiverKind Kind, ISymbol? Root, ImmutableArray<ISymbol> Segments, INamedTypeSymbol? PublisherType) ClassifyChainedReceiver(
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
                return baseKind == EventReceiverKind.Unknown
                    ? (EventReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null)
                    : (baseKind, baseRoot, segments.ToImmutableArray(), basePublisherType);
            }

            if (semanticModel.GetSymbolInfo(current, cancellationToken).Symbol
                    is not { IsStatic: false } segmentSymbol ||
                segmentSymbol is not (IFieldSymbol or IPropertySymbol))
            {
                return (EventReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null);
            }

            segments.Insert(0, segmentSymbol);

            if (inner is MemberAccessExpressionSyntax nextChain)
            {
                current = nextChain;
                continue;
            }

            var (kind, root, publisherType) = ClassifyReceiverRoot(
                inner, containingType, semanticModel, cancellationToken);
            return kind == EventReceiverKind.Unknown
                ? (EventReceiverKind.Unknown, null, ImmutableArray<ISymbol>.Empty, null)
                : (kind, root, segments.ToImmutableArray(), publisherType);
        }
    }

    private static (EventReceiverKind Kind, ISymbol? Root, INamedTypeSymbol? PublisherType) ClassifyReceiverRoot(
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
                    ? (EventReceiverKind.InjectedMember, field, fieldType)
                    : (EventReceiverKind.Unknown, null, null);

            case IPropertySymbol property when !property.IsStatic && IsTypeOrBase(containingType, property.ContainingType):
                return property.Type is INamedTypeSymbol propertyType
                    ? (EventReceiverKind.InjectedMember, property, propertyType)
                    : (EventReceiverKind.Unknown, null, null);

            case IParameterSymbol parameter when
                parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor &&
                IsTypeOrBase(containingType, ctor.ContainingType):
                return parameter.Type is INamedTypeSymbol parameterType
                    ? (EventReceiverKind.ConstructorParameter, parameter, parameterType)
                    : (EventReceiverKind.Unknown, null, null);

            default:
                return (EventReceiverKind.Unknown, null, null);
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

    public static SyntaxNode? ExecutableBodyOf(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(ancestor =>
            ancestor is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    public static SemanticModel GetSemanticModel(
        SyntaxTree syntaxTree,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        #pragma warning disable RS1030 // Compilation-end analysis needs models for foreign trees.
        return semanticModelsByTree.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
        #pragma warning restore RS1030
    }

    public static bool IsTypeOrBase(INamedTypeSymbol type, INamedTypeSymbol candidateOwner)
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

    /// <summary>
    /// Unifies constructor-parameter receivers with the field or property the parameter is
    /// stored into, so a subscription through the parameter matches an unsubscription through
    /// the field.
    /// </summary>
    public static ISymbol? CanonicalizeReceiverRoot(
        EventReceiverKind receiverKind,
        ISymbol? receiverRoot,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (receiverKind != EventReceiverKind.ConstructorParameter ||
            receiverRoot is not IParameterSymbol parameter)
        {
            return receiverRoot;
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
                        return receiverRoot;
                    }

                    storedInto = target;
                }
            }
        }

        return storedInto ?? receiverRoot;
    }

    /// <summary>
    /// An injected-member publisher qualifies only when every source-visible write to the
    /// member is a simple assignment from an instance-constructor parameter of the member's
    /// containing type, and at least one such write exists. Any initializer, compound write,
    /// or non-parameter source makes the publisher's origin unprovable.
    /// </summary>
    public static bool MemberIsProvablyInjected(
        ISymbol? receiverRoot,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        if (receiverRoot is not ISymbol member)
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

    public static bool ReceiverSegmentsMatch(
        ImmutableArray<ISymbol> subscriptionSegments,
        ImmutableArray<ISymbol> removalSegments)
    {
        if (subscriptionSegments.Length != removalSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < subscriptionSegments.Length; i++)
        {
            if (!SegmentSymbolsMatch(subscriptionSegments[i], removalSegments[i]))
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
    public static bool ChainSegmentsAreStableProjections(
        ImmutableArray<ISymbol> receiverSegments,
        INamedTypeSymbol? publisherType,
        List<ServiceRegistration> registrations,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken)
    {
        for (var i = 0; i < receiverSegments.Length; i++)
        {
            var segment = receiverSegments[i];

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
                        property, publisherType, registrations, compilation, semanticModelsByTree, cancellationToken))
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
