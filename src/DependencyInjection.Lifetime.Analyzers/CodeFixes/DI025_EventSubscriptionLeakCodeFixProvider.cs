using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI025 and its DI026 scoped-publisher Info tier: event subscription
/// on a longer-lived publisher without a matching unsubscription. The handler must be a
/// method group whose receiver still resolves inside Dispose; three tiers of repair are then
/// offered, in decreasing order of confidence:
/// <list type="number">
/// <item>Insert the mirrored -= at the top of an existing block-bodied Dispose method
/// (when the type already implements the matching disposal interface).</item>
/// <item>When the disposal contract is inherited from a base that follows the standard
/// virtual Dispose(bool) pattern, add a <c>protected override void Dispose(bool)</c> that
/// unsubscribes and chains to <c>base.Dispose(disposing)</c> — overriding the pattern means
/// our unsubscribe actually runs through the base's Dispose() -&gt; Dispose(true) dispatch.</item>
/// <item>For a SCOPED-registered subscriber that implements neither disposal interface,
/// implement IDisposable outright (add the interface plus a Dispose that unsubscribes). The
/// owning scope disposes it deterministically, so this introduces no leak.</item>
/// </list>
/// Introducing IDisposable on a <em>transient</em> subscriber is refused: it is exactly the
/// DI008 disposable-transient-capture shape, so the fix would trade a DI025 for a DI008.
/// Hoisting a lambda into a field is refused because it changes capture semantics.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI025_EventSubscriptionLeakCodeFixProvider))]
[Shared]
public sealed class DI025_EventSubscriptionLeakCodeFixProvider : CodeFixProvider
{
    private const string AddUnsubscribeEquivalenceKey = "DI025_AddUnsubscribeInDispose";
    private const string AddDisposeEquivalenceKey = "DI025_AddDisposeWithUnsubscribe";
    private const string ImplementIDisposableEquivalenceKey = "DI025_ImplementIDisposableWithUnsubscribe";
    private const string SubscriberLifetimePropertyKey = "SubscriberLifetime";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            DiagnosticIds.EventSubscriptionLeak,
            DiagnosticIds.EventSubscriptionLeakScopedPublisher);

    /// <inheritdoc />
    public sealed override FixAllProvider GetFixAllProvider() => SequentialFixAllProvider.Instance;

    /// <summary>
    /// The disposal-member strategy a given equivalence key resolves to for a subscription in
    /// the CURRENT document. Recomputed per diagnostic so that, once an earlier fix-all
    /// iteration has synthesized the member, later diagnostics merge into it (one member holding
    /// every <c>-=</c>) instead of each synthesizing tier emitting a duplicate member.
    /// </summary>
    private enum FixStrategy
    {
        None,
        InsertIntoExisting,
        CreateDisposeBoolOverride,
        InsertIntoDisposeBoolOverride,
        ImplementInterface,
        InsertIntoImplementedDispose
    }

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var subscription = node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();

        if (subscription is null ||
            !subscription.IsKind(SyntaxKind.AddAssignmentExpression) ||
            !IsMethodGroupHandler(subscription.Right))
        {
            return;
        }

        // The -= statement clones the += receiver, so the receiver must still resolve inside
        // Dispose: a field/property member access or a static event. A constructor-parameter
        // receiver would not compile there.
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null || !ReceiverIsAvailableInDispose(subscription.Left, semanticModel, context.CancellationToken))
        {
            return;
        }

        var containingType = subscription.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containingType is null ||
            semanticModel.GetDeclaredSymbol(containingType, context.CancellationToken) is not INamedTypeSymbol typeSymbol)
        {
            return;
        }

        diagnostic.Properties.TryGetValue(SubscriberLifetimePropertyKey, out var lifetime);

        // Offer every equivalence key that applies to this subscription's current shape. Each
        // key's create-or-merge behavior is decided by the same SelectStrategy used by fix-all,
        // so single-fix and fix-all can never drift. Registering all applicable keys (rather
        // than one "best" tier) is what lets fix-all's second diagnostic still be fixable under
        // the originally-invoked key after the first diagnostic synthesized the member.
        foreach (var (equivalenceKey, title) in new[]
                 {
                     (AddUnsubscribeEquivalenceKey, "Unsubscribe in existing Dispose"),
                     (AddDisposeEquivalenceKey, "Unsubscribe by overriding Dispose(bool)"),
                     (ImplementIDisposableEquivalenceKey, "Implement IDisposable to unsubscribe")
                 })
        {
            if (SelectStrategy(equivalenceKey, containingType, typeSymbol, lifetime, semanticModel, context.CancellationToken).Strategy
                == FixStrategy.None)
            {
                continue;
            }

            var key = equivalenceKey;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: cancellationToken => ComputeFixedDocumentAsync(
                        context.Document, subscription, key, lifetime, cancellationToken),
                    equivalenceKey: key),
                diagnostic);
        }
    }

    /// <summary>
    /// Resolves which disposal-member strategy the given equivalence key maps to for this
    /// subscription's containing type in its current shape (and the target method for the merge
    /// strategies). This is the single decision point shared by <see cref="RegisterCodeFixesAsync"/>
    /// and the sequential fix-all provider.
    /// </summary>
    private static (FixStrategy Strategy, MethodDeclarationSyntax? Target) SelectStrategy(
        string equivalenceKey,
        TypeDeclarationSyntax containingType,
        INamedTypeSymbol typeSymbol,
        string? subscriberLifetime,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (equivalenceKey)
        {
            case AddUnsubscribeEquivalenceKey:
            {
                var usable = FindUsableExistingDispose(containingType, semanticModel, cancellationToken);
                return usable is not null ? (FixStrategy.InsertIntoExisting, usable) : (FixStrategy.None, null);
            }

            case AddDisposeEquivalenceKey:
            {
                if (FindOverridableBaseDisposeBool(typeSymbol) is null ||
                    !BaseDisposeDispatchesToBoolHook(typeSymbol, cancellationToken))
                {
                    return (FixStrategy.None, null);
                }

                var ownOverride = FindOwnDisposeBoolOverride(containingType);
                if (ownOverride is not null)
                {
                    return (FixStrategy.InsertIntoDisposeBoolOverride, ownOverride);
                }

                // Do not add a second dispose-shaped member if the type already declares one we
                // could not use here (a different partial, an expression body).
                return DeclaresOwnDisposeMember(typeSymbol)
                    ? (FixStrategy.None, null)
                    : (FixStrategy.CreateDisposeBoolOverride, null);
            }

            case ImplementIDisposableEquivalenceKey:
            {
                // Introducing IDisposable is safe only for a scoped subscriber; a transient that
                // becomes IDisposable is the DI008 disposable-transient-capture shape.
                if (subscriberLifetime != "Scoped")
                {
                    return (FixStrategy.None, null);
                }

                var implementsIDisposable = ImplementsInterface(typeSymbol, "System.IDisposable");
                var implementsIAsyncDisposable = ImplementsInterface(typeSymbol, "System.IAsyncDisposable");

                if (!implementsIDisposable && !implementsIAsyncDisposable)
                {
                    return DeclaresOwnDisposeMember(typeSymbol)
                        ? (FixStrategy.None, null)
                        : (FixStrategy.ImplementInterface, null);
                }

                // Continuation: a prior fix-all iteration already added IDisposable to THIS
                // declaration (not inherited from a base) plus a parameterless Dispose — merge
                // into it rather than trying to add the interface again.
                if (implementsIDisposable && !InheritsDisposalContract(typeSymbol) &&
                    FindOwnParameterlessDispose(containingType) is { } ownDispose)
                {
                    return (FixStrategy.InsertIntoImplementedDispose, ownDispose);
                }

                return (FixStrategy.None, null);
            }

            default:
                return (FixStrategy.None, null);
        }
    }

    /// <summary>
    /// Applies the strategy the <paramref name="equivalenceKey"/> resolves to for
    /// <paramref name="subscription"/> against the current state of <paramref name="document"/>.
    /// Returns the original document unchanged when nothing applies (so callers can treat it as a
    /// no-op) — the gate revalidation mirrors <see cref="RegisterCodeFixesAsync"/> exactly.
    /// </summary>
    private static async Task<Document> ComputeFixedDocumentAsync(
        Document document,
        AssignmentExpressionSyntax subscription,
        string equivalenceKey,
        string? subscriberLifetime,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null ||
            !subscription.IsKind(SyntaxKind.AddAssignmentExpression) ||
            !IsMethodGroupHandler(subscription.Right) ||
            !ReceiverIsAvailableInDispose(subscription.Left, semanticModel, cancellationToken))
        {
            return document;
        }

        var containingType = subscription.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containingType is null ||
            semanticModel.GetDeclaredSymbol(containingType, cancellationToken) is not INamedTypeSymbol typeSymbol)
        {
            return document;
        }

        var (strategy, target) = SelectStrategy(
            equivalenceKey, containingType, typeSymbol, subscriberLifetime, semanticModel, cancellationToken);

        var unsubscribe = BuildUnsubscribeStatement(subscription);

        return strategy switch
        {
            FixStrategy.InsertIntoExisting =>
                InsertUnsubscribeIntoMethod(document, root, unsubscribe, target!, atTop: true),
            FixStrategy.InsertIntoDisposeBoolOverride =>
                InsertUnsubscribeIntoMethod(document, root, unsubscribe, target!, atTop: false),
            FixStrategy.InsertIntoImplementedDispose =>
                InsertUnsubscribeIntoMethod(document, root, unsubscribe, target!, atTop: false),
            FixStrategy.CreateDisposeBoolOverride =>
                AddDisposeBoolOverride(document, root, containingType, unsubscribe),
            FixStrategy.ImplementInterface =>
                ImplementIDisposable(document, root, containingType, unsubscribe),
            _ => document
        };
    }

    /// <summary>
    /// A method merely named Dispose on a type that does not implement the matching disposal
    /// interface is never called by the container; inserting -= there would fake a repair.
    /// </summary>
    private static bool TypeImplementsDisposalContract(
        TypeDeclarationSyntax containingType,
        MethodDeclarationSyntax disposeMethod,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(containingType, cancellationToken) is not INamedTypeSymbol typeSymbol)
        {
            return false;
        }

        var requiredInterface = disposeMethod.Identifier.ValueText == "DisposeAsync"
            ? "System.IAsyncDisposable"
            : "System.IDisposable";

        foreach (var implemented in typeSymbol.AllInterfaces)
        {
            if (implemented.ToDisplayString() == requiredInterface)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReceiverIsAvailableInDispose(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is IEventSymbol { IsStatic: true })
        {
            return true;
        }

        if (left is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        // Chained receivers re-resolve verbatim in Dispose as long as the chain root is an
        // instance field or property; a constructor-parameter or local root would not compile
        // there. Parentheses and null-forgiving operators are peeled at every hop so a
        // parenthesized chain cannot hide its root.
        var receiver = UnwrapReceiver(memberAccess.Expression);
        while (receiver is MemberAccessExpressionSyntax chain)
        {
            var inner = UnwrapReceiver(chain.Expression);
            if (inner is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                receiver = chain.Name;
                break;
            }

            receiver = inner;
        }

        return semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol
            is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false };
    }

    private static bool IsMethodGroupHandler(ExpressionSyntax handler)
    {
        handler = Unwrap(handler);
        return handler is IdentifierNameSyntax ||
               (handler is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is ThisExpressionSyntax);
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
                default:
                    return expression;
            }
        }
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 1 } creation:
                    expression = creation.ArgumentList.Arguments[0].Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    /// <summary>
    /// Finds a block-bodied dispose-shaped method declared on the subscriber itself,
    /// preferring Dispose() over Dispose(bool) over DisposeAsync().
    /// </summary>
    private static MethodDeclarationSyntax? FindDisposeMethod(TypeDeclarationSyntax containingType)
    {
        var methods = containingType.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Body is not null)
            .ToList();

        return methods.FirstOrDefault(method =>
                   method.Identifier.ValueText == "Dispose" && method.ParameterList.Parameters.Count == 0)
               ?? methods.FirstOrDefault(method =>
                   method.Identifier.ValueText == "Dispose" &&
                   method.ParameterList.Parameters.Count == 1 &&
                   method.ParameterList.Parameters[0].Type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword })
               ?? methods.FirstOrDefault(method =>
                   method.Identifier.ValueText == "DisposeAsync" && method.ParameterList.Parameters.Count == 0);
    }

    /// <summary>
    /// True when the type itself (across every partial) declares a dispose-shaped method.
    /// Inherited members do not count — those are handled by the tier-2 base-pattern path.
    /// </summary>
    private static bool DeclaresOwnDisposeMember(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetMembers().OfType<IMethodSymbol>().Any(method =>
            (method.Name == "Dispose" &&
             (method.Parameters.Length == 0 ||
              (method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean))) ||
            (method.Name == "DisposeAsync" && method.Parameters.Length == 0));

    private static bool ImplementsInterface(INamedTypeSymbol typeSymbol, string metadataDisplayName) =>
        typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == metadataDisplayName);

    /// <summary>
    /// Finds the <c>Dispose(bool)</c> hook that <c>AddDisposeBoolOverrideAsync</c> can correctly
    /// override: exactly a <c>protected</c>, non-abstract, non-sealed, <c>virtual</c>/<c>override</c>
    /// method — the standard dispose pattern. Only the NEAREST base declaration is considered, so a
    /// sealed override on an intermediate base hides a virtual grandparent hook and is refused.
    /// Anything else (public or protected-internal accessibility that a <c>protected override</c>
    /// would mis-match, an abstract hook the emitted <c>base.Dispose(disposing)</c> could not call,
    /// or a sealed override) returns null so the fix declines rather than emit code that does not
    /// compile or silently widens accessibility.
    /// </summary>
    private static IMethodSymbol? FindOverridableBaseDisposeBool(INamedTypeSymbol typeSymbol)
    {
        for (var baseType = typeSymbol.BaseType;
             baseType is not null && baseType.SpecialType != SpecialType.System_Object;
             baseType = baseType.BaseType)
        {
            var nearest = baseType.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault(method =>
                method.ReturnsVoid &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean &&
                method.Parameters[0].RefKind == RefKind.None);

            if (nearest is null)
            {
                // This base does not declare Dispose(bool); keep walking toward the declaration.
                continue;
            }

            var overridable =
                nearest.DeclaredAccessibility == Accessibility.Protected &&
                !nearest.IsAbstract &&
                !nearest.IsSealed &&
                (nearest.IsVirtual || nearest.IsOverride);

            return overridable ? nearest : null;
        }

        return null;
    }

    /// <summary>
    /// Confirms the standard dispose pattern is actually wired: the nearest accessible
    /// parameterless <c>Dispose()</c> in the base chain must, in source, call its own
    /// <c>Dispose(true)</c> (a bare <c>Dispose(true)</c> or <c>this.Dispose(true)</c>). A
    /// metadata-only base (no syntax to inspect), a <c>Dispose()</c> that dispatches to another
    /// object (<c>_inner.Dispose(true)</c>), or one that passes <c>false</c> means a
    /// <c>protected override void Dispose(bool)</c> we add would never run its unsubscribe on the
    /// container path.
    /// </summary>
    private static bool BaseDisposeDispatchesToBoolHook(
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken)
    {
        for (var baseType = typeSymbol.BaseType;
             baseType is not null && baseType.SpecialType != SpecialType.System_Object;
             baseType = baseType.BaseType)
        {
            var parameterlessDispose = baseType.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault(method =>
                method.Parameters.Length == 0 &&
                method.DeclaredAccessibility is Accessibility.Public
                    or Accessibility.Protected
                    or Accessibility.ProtectedOrInternal);

            if (parameterlessDispose is null)
            {
                continue;
            }

            // Found the nearest dispatching candidate; it alone decides. If it has no source
            // (metadata-only) we cannot prove dispatch, so refuse.
            foreach (var reference in parameterlessDispose.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                {
                    continue;
                }

                var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
                if (body is not null && DispatchesToDisposeBool(body))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool DispatchesToDisposeBool(SyntaxNode body) =>
        // A Dispose(true) nested inside a lambda or local function only runs if that
        // delegate is invoked, which this proof cannot see — stay out of nested bodies.
        body.DescendantNodesAndSelf(descendIntoChildren: node =>
                node == body ||
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.ArgumentList.Arguments.Count == 1 &&
                invocation.ArgumentList.Arguments[0].Expression.IsKind(SyntaxKind.TrueLiteralExpression) &&
                IsSelfDisposeCall(invocation.Expression));

    /// <summary>
    /// Recognizes a call to the current instance's own <c>Dispose</c> — a bare <c>Dispose(...)</c>
    /// or <c>this.Dispose(...)</c>. A member access on any other receiver (<c>_inner.Dispose(...)</c>,
    /// <c>base.Dispose(...)</c>) disposes a different object, not the base's virtual hook, so it
    /// does not count as dispatch to the pattern we are extending.
    /// </summary>
    private static bool IsSelfDisposeCall(ExpressionSyntax invoked) => invoked switch
    {
        IdentifierNameSyntax { Identifier.ValueText: "Dispose" } => true,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name.Identifier.ValueText: "Dispose" } => true,
        _ => false
    };

    private static ExpressionStatementSyntax BuildUnsubscribeStatement(AssignmentExpressionSyntax subscription) =>
        SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SubtractAssignmentExpression,
                    subscription.Left.WithoutTrivia(),
                    Unwrap(subscription.Right).WithoutTrivia()))
            .WithAdditionalAnnotations(Formatter.Annotation);

    /// <summary>The usable existing Dispose (block-bodied, and the type implements the matching
    /// contract) that a mirrored -= can be inserted into, or null.</summary>
    private static MethodDeclarationSyntax? FindUsableExistingDispose(
        TypeDeclarationSyntax containingType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var method = FindDisposeMethod(containingType);
        return method?.Body is not null &&
               TypeImplementsDisposalContract(containingType, method, semanticModel, cancellationToken)
            ? method
            : null;
    }

    /// <summary>A block-bodied <c>Dispose(bool)</c> override declared on this type (the shape a
    /// prior fix-all iteration would have synthesized).</summary>
    private static MethodDeclarationSyntax? FindOwnDisposeBoolOverride(TypeDeclarationSyntax containingType) =>
        containingType.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(method =>
            method.Body is not null &&
            method.Modifiers.Any(SyntaxKind.OverrideKeyword) &&
            IsDisposeBoolMethod(method, out _));

    private static MethodDeclarationSyntax? FindOwnParameterlessDispose(TypeDeclarationSyntax containingType) =>
        containingType.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(method =>
            method.Body is not null &&
            method.Identifier.ValueText == "Dispose" &&
            method.ParameterList.Parameters.Count == 0);

    private static bool IsDisposeBoolMethod(MethodDeclarationSyntax method, out string parameterName)
    {
        parameterName = string.Empty;
        if (method.Identifier.ValueText != "Dispose" || method.ParameterList.Parameters.Count != 1)
        {
            return false;
        }

        var parameter = method.ParameterList.Parameters[0];
        if (parameter.Type is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword })
        {
            return false;
        }

        parameterName = parameter.Identifier.ValueText;
        return true;
    }

    /// <summary>True when a base type (not this type's own declaration) supplies the disposal
    /// contract — distinguishing an inherited contract from an <c>IDisposable</c> we added here.</summary>
    private static bool InheritsDisposalContract(INamedTypeSymbol typeSymbol) =>
        typeSymbol.BaseType is { } baseType &&
        baseType.AllInterfaces.Any(i =>
            i.ToDisplayString() is "System.IDisposable" or "System.IAsyncDisposable");

    /// <summary>
    /// Inserts the mirrored -= into <paramref name="method"/>. For the standard guarded
    /// <c>Dispose(bool)</c> shape (a leading <c>if (disposing) { ... }</c>) it lands inside the
    /// guard so it never runs on the finalizer path; otherwise it lands in the method body.
    /// <paramref name="atTop"/> keeps the existing tier-1 "unsubscribe first" behavior; merge
    /// insertions append so several unsubscribes accumulate in source order.
    /// </summary>
    private static Document InsertUnsubscribeIntoMethod(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax unsubscribe,
        MethodDeclarationSyntax method,
        bool atTop)
    {
        var body = method.Body!;

        if (IsDisposeBoolMethod(method, out var parameterName) &&
            body.Statements.FirstOrDefault() is IfStatementSyntax
            {
                Condition: IdentifierNameSyntax condition,
                Statement: BlockSyntax guardBlock
            } &&
            condition.Identifier.ValueText == parameterName)
        {
            var newGuard = guardBlock.WithStatements(
                atTop ? guardBlock.Statements.Insert(0, unsubscribe) : guardBlock.Statements.Add(unsubscribe));
            return document.WithSyntaxRoot(root.ReplaceNode(guardBlock, newGuard));
        }

        var newBody = body.WithStatements(
            atTop ? body.Statements.Insert(0, unsubscribe) : body.Statements.Add(unsubscribe));
        return document.WithSyntaxRoot(root.ReplaceNode(body, newBody));
    }

    /// <summary>
    /// Adds <c>protected override void Dispose(bool disposing) { if (disposing) { &lt;unsubscribe&gt;; } base.Dispose(disposing); }</c>
    /// to the analyzed declaration. The unsubscribe is guarded by <c>if (disposing)</c> because
    /// <c>Dispose(bool)</c> also runs on the finalizer path (<c>Dispose(false)</c>), where
    /// touching the managed publisher would violate the dispose pattern.
    /// </summary>
    private static Document AddDisposeBoolOverride(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        ExpressionStatementSyntax unsubscribe)
    {
        var guardedUnsubscribe = SyntaxFactory.IfStatement(
            SyntaxFactory.IdentifierName("disposing"),
            SyntaxFactory.Block(unsubscribe));

        var baseCall = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.BaseExpression(),
                    SyntaxFactory.IdentifierName("Dispose")),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("disposing"))))));

        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                SyntaxFactory.Identifier("Dispose"))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.ProtectedKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("disposing"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))))))
            .WithBody(SyntaxFactory.Block(guardedUnsubscribe, baseCall));

        return AppendMember(document, root, containingType, method);
    }

    /// <summary>
    /// Adds <c>IDisposable</c> to the base list and a <c>public void Dispose() { &lt;unsubscribe&gt;; }</c>.
    /// </summary>
    private static Document ImplementIDisposable(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        ExpressionStatementSyntax unsubscribe)
    {
        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                SyntaxFactory.Identifier("Dispose"))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithBody(SyntaxFactory.Block(unsubscribe));

        // Emit the fully-qualified name so the base entry always binds to System.IDisposable —
        // an unqualified `IDisposable` would mis-bind in files without `using System` or with a
        // namespace-local IDisposable. Simplifier.Annotation lets Roslyn reduce it back to the
        // shortest unambiguous form (bare `IDisposable` when `using System` is present and no
        // type collides).
        var disposableBaseType = SyntaxFactory.SimpleBaseType(
            SyntaxFactory.ParseTypeName("global::System.IDisposable")
                .WithAdditionalAnnotations(Simplifier.Annotation));

        TypeDeclarationSyntax typeWithBase;
        if (containingType.BaseList is null)
        {
            // No base list yet: move the identifier's trailing trivia (the newline before the
            // opening brace) to after the new base list so we get `Name : IDisposable\n{`
            // rather than the base list landing on its own line.
            var identifier = containingType.Identifier;
            var trailing = identifier.TrailingTrivia;
            var baseList = SyntaxFactory
                .BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(disposableBaseType))
                .WithTrailingTrivia(trailing);

            typeWithBase = containingType
                .WithIdentifier(identifier.WithTrailingTrivia(SyntaxFactory.Space))
                .WithBaseList(baseList);
        }
        else
        {
            typeWithBase = containingType.WithBaseList(containingType.BaseList.AddTypes(disposableBaseType));
        }

        var newType = typeWithBase.AddMembers(method.WithAdditionalAnnotations(Formatter.Annotation));

        var updatedRoot = root.ReplaceNode(containingType, newType.WithAdditionalAnnotations(Formatter.Annotation));

        return document.WithSyntaxRoot(updatedRoot);
    }

    private static Document AppendMember(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        MemberDeclarationSyntax member)
    {
        var newType = containingType.AddMembers(member.WithAdditionalAnnotations(Formatter.Annotation));
        var updatedRoot = root.ReplaceNode(containingType, newType.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(updatedRoot);
    }

    /// <summary>
    /// Applies the fix to every DI025/DI026 diagnostic in a document sequentially rather than by
    /// batch-merging independent edits. The batch fixer would run a synthesizing tier once per
    /// diagnostic and emit a duplicate dispose member (or lose unsubscribes) when one type
    /// carried several leaks; here each subscription is re-evaluated against the evolving
    /// document, so the second leak onward merges into the member the first one created.
    /// </summary>
    private sealed class SequentialFixAllProvider : DocumentBasedFixAllProvider
    {
        public static readonly SequentialFixAllProvider Instance = new();

        protected override async Task<Document?> FixAllAsync(
            FixAllContext fixAllContext,
            Document document,
            ImmutableArray<Diagnostic> diagnostics)
        {
            var cancellationToken = fixAllContext.CancellationToken;
            var equivalenceKey = fixAllContext.CodeActionEquivalenceKey;
            if (diagnostics.IsEmpty || equivalenceKey is null)
            {
                return document;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return document;
            }

            // Tag each subscription with a unique annotation in one rewrite up front, so later
            // edits can never invalidate the spans we still have to fix.
            var pending = new List<(SyntaxAnnotation Annotation, string? Lifetime)>();
            var rewrites = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var diagnostic in diagnostics.OrderBy(d => d.Location.SourceSpan.Start))
            {
                if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<AssignmentExpressionSyntax>()
                    is not { } subscription || rewrites.ContainsKey(subscription))
                {
                    continue;
                }

                var annotation = new SyntaxAnnotation();
                rewrites[subscription] = subscription.WithAdditionalAnnotations(annotation);
                diagnostic.Properties.TryGetValue(SubscriberLifetimePropertyKey, out var lifetime);
                pending.Add((annotation, lifetime));
            }

            document = document.WithSyntaxRoot(root.ReplaceNodes(rewrites.Keys, (original, _) => rewrites[original]));

            // Apply in source order so the merged member lists every -= in order.
            foreach (var (annotation, lifetime) in pending)
            {
                var currentRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (currentRoot?.GetAnnotatedNodes(annotation).FirstOrDefault() is not AssignmentExpressionSyntax subscription)
                {
                    continue;
                }

                document = await ComputeFixedDocumentAsync(
                    document, subscription, equivalenceKey, lifetime, cancellationToken).ConfigureAwait(false);
            }

            return document;
        }
    }
}
