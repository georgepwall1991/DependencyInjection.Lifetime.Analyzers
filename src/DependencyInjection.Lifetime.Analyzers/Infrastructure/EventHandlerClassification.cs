using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// The provable shape of an event subscription's handler (the right-hand side of
/// <c>publisher.Changed += H</c>) as classified for DI025.
/// </summary>
internal enum EventHandlerKind
{
    Unknown,
    InstanceMethodGroup,
    AnonymousCapturingInstance,
    StoredDelegateMember
}

/// <summary>
/// Handler-side classification for the DI025 event-subscription-leak analysis: proving whether a
/// subscription's handler captures the subscriber instance (method group, capturing lambda, or a
/// stored delegate that binds one) and matching handler identities between subscribe/unsubscribe.
/// </summary>
internal static class EventHandlerClassification
{
    private const string AnonymousHandlerDisplay = "an anonymous handler";

    public static (EventHandlerKind Kind, ISymbol? Identity, string Display) ClassifyHandler(
        ExpressionSyntax handler,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        handler = UnwrapHandler(handler);

        if (handler is AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return AnonymousFunctionCapturesInstance(anonymousFunction, containingType, semanticModel, cancellationToken)
                ? (EventHandlerKind.AnonymousCapturingInstance, null, AnonymousHandlerDisplay)
                : (EventHandlerKind.Unknown, null, AnonymousHandlerDisplay);
        }

        var handlerSymbol = ResolveHandlerSymbol(handler, semanticModel, cancellationToken);

        switch (handlerSymbol)
        {
            case IMethodSymbol method when !method.IsStatic && EventReceiverClassification.IsTypeOrBase(containingType, method.ContainingType):
                if (handler is MemberAccessExpressionSyntax methodAccess &&
                    methodAccess.Expression is not ThisExpressionSyntax)
                {
                    // `other.OnMessage` roots `other`, not the subscriber instance.
                    return (EventHandlerKind.Unknown, null, string.Empty);
                }

                return (EventHandlerKind.InstanceMethodGroup, NormalizeMethod(method), method.Name);

            case IFieldSymbol field when !field.IsStatic && EventReceiverClassification.IsTypeOrBase(containingType, field.ContainingType):
                return StoredDelegateCapturesInstance(field, containingType, semanticModel.Compilation, cancellationToken)
                    ? (EventHandlerKind.StoredDelegateMember, field, field.Name)
                    : (EventHandlerKind.Unknown, null, string.Empty);

            case ILocalSymbol storedLocal:
                return StoredLocalCapturesInstance(storedLocal, containingType, semanticModel, cancellationToken)
                    ? (EventHandlerKind.StoredDelegateMember, storedLocal, storedLocal.Name)
                    : (EventHandlerKind.Unknown, null, string.Empty);

            default:
                return (EventHandlerKind.Unknown, null, string.Empty);
        }
    }

    /// <summary>
    /// Resolves the handler expression's symbol. A method group passed where the target is
    /// <c>System.Delegate</c> — the <c>Delegate.Combine</c> argument shape — has no single target
    /// delegate type, so <see cref="SymbolInfo.Symbol"/> is null; the one unambiguous candidate
    /// method is the handler. Ambiguous groups (more than one candidate) stay unresolved.
    /// </summary>
    private static ISymbol? ResolveHandlerSymbol(
        ExpressionSyntax handler,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(handler, cancellationToken);
        if (symbolInfo.Symbol is not null)
        {
            return symbolInfo.Symbol;
        }

        return symbolInfo.CandidateSymbols.Length == 1 && symbolInfo.CandidateSymbols[0] is IMethodSymbol singleCandidate
            ? singleCandidate
            : null;
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
                EventReceiverClassification.IsTypeOrBase(containingType, symbol.ContainingType))
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

        var enclosingBody = EventReceiverClassification.ExecutableBodyOf(declarator);
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
        } method && EventReceiverClassification.IsTypeOrBase(containingType, method.ContainingType);
    }

    private static IEnumerable<(ExpressionSyntax Expression, SemanticModel Model)> EnumerateMemberValueSources(
        ISymbol member,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var modelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        SemanticModel ModelFor(SyntaxTree tree) => EventReceiverClassification.GetSemanticModel(tree, compilation, modelsByTree);

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

    public static bool HandlerIdentitiesMatch(ISymbol? subscriptionIdentity, ISymbol? removalIdentity) =>
        subscriptionIdentity is not null &&
        removalIdentity is not null &&
        SymbolEqualityComparer.Default.Equals(subscriptionIdentity, removalIdentity);

    public static bool IsAnonymousRemoval(EventHandlerKind handlerKind, string handlerDisplay) =>
        handlerKind == EventHandlerKind.AnonymousCapturingInstance ||
        handlerDisplay == AnonymousHandlerDisplay;
}
