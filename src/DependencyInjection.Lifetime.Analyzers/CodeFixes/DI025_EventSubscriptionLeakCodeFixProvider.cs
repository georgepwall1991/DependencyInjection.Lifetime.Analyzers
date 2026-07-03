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
    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

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

        // Tier 1: an existing block-bodied Dispose on a type that already implements the
        // matching disposal interface — insert the mirrored -= at the top.
        var disposeMethod = FindDisposeMethod(containingType);
        if (disposeMethod?.Body is not null &&
            TypeImplementsDisposalContract(containingType, disposeMethod, semanticModel, context.CancellationToken))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Unsubscribe in '{disposeMethod.Identifier.ValueText}'",
                    createChangedDocument: cancellationToken => AddUnsubscribeAsync(
                        context.Document, root, subscription, disposeMethod, cancellationToken),
                    equivalenceKey: AddUnsubscribeEquivalenceKey),
                diagnostic);
            return;
        }

        // Beyond tier 1 the fix synthesizes a dispose path. If the type already declares its
        // own dispose-shaped method that we could not use here (a different partial, an
        // expression body), refuse rather than risk a duplicate member or a fake repair.
        if (DeclaresOwnDisposeMember(typeSymbol))
        {
            return;
        }

        var unsubscribe = BuildUnsubscribeStatement(subscription);

        var implementsIDisposable = ImplementsInterface(typeSymbol, "System.IDisposable");
        var implementsIAsyncDisposable = ImplementsInterface(typeSymbol, "System.IAsyncDisposable");

        if (implementsIDisposable || implementsIAsyncDisposable)
        {
            // Tier 2: the contract is inherited. Only the standard virtual Dispose(bool)
            // pattern is safe to extend — overriding it guarantees our unsubscribe runs. Any
            // other inherited shape (non-virtual or explicit Dispose) would leave the added
            // method uncalled by the container, a fake repair, so refuse.
            if (FindOverridableBaseDisposeBool(typeSymbol) is null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Override Dispose(bool) to unsubscribe",
                    createChangedDocument: cancellationToken => AddDisposeBoolOverrideAsync(
                        context.Document, root, containingType, unsubscribe, cancellationToken),
                    equivalenceKey: AddDisposeEquivalenceKey),
                diagnostic);
            return;
        }

        // Tier 3: the type implements neither disposal interface. Implementing IDisposable is
        // only safe for scoped-registered subscribers; a transient that becomes IDisposable is
        // the DI008 disposable-transient-capture shape.
        if (!diagnostic.Properties.TryGetValue(SubscriberLifetimePropertyKey, out var lifetime) ||
            lifetime != "Scoped")
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Implement IDisposable to unsubscribe",
                createChangedDocument: cancellationToken => ImplementIDisposableAsync(
                    context.Document, root, containingType, unsubscribe, cancellationToken),
                equivalenceKey: ImplementIDisposableEquivalenceKey),
            diagnostic);
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
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean);

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

    private static ExpressionStatementSyntax BuildUnsubscribeStatement(AssignmentExpressionSyntax subscription) =>
        SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SubtractAssignmentExpression,
                    subscription.Left.WithoutTrivia(),
                    Unwrap(subscription.Right).WithoutTrivia()))
            .WithAdditionalAnnotations(Formatter.Annotation);

    private static Task<Document> AddUnsubscribeAsync(
        Document document,
        SyntaxNode root,
        AssignmentExpressionSyntax subscription,
        MethodDeclarationSyntax disposeMethod,
        CancellationToken cancellationToken)
    {
        var unsubscribe = BuildUnsubscribeStatement(subscription);

        var updatedBody = disposeMethod.Body!.WithStatements(
            disposeMethod.Body.Statements.Insert(0, unsubscribe));

        var updatedRoot = root.ReplaceNode(disposeMethod.Body!, updatedBody);

        return Task.FromResult(document.WithSyntaxRoot(updatedRoot));
    }

    /// <summary>
    /// Adds <c>protected override void Dispose(bool disposing) { &lt;unsubscribe&gt;; base.Dispose(disposing); }</c>
    /// to the analyzed declaration.
    /// </summary>
    private static Task<Document> AddDisposeBoolOverrideAsync(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        ExpressionStatementSyntax unsubscribe,
        CancellationToken cancellationToken)
    {
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
            .WithBody(SyntaxFactory.Block(unsubscribe, baseCall));

        return AppendMemberAsync(document, root, containingType, method, cancellationToken);
    }

    /// <summary>
    /// Adds <c>IDisposable</c> to the base list and a <c>public void Dispose() { &lt;unsubscribe&gt;; }</c>.
    /// </summary>
    private static Task<Document> ImplementIDisposableAsync(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        ExpressionStatementSyntax unsubscribe,
        CancellationToken cancellationToken)
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

        return Task.FromResult(document.WithSyntaxRoot(updatedRoot));
    }

    private static Task<Document> AppendMemberAsync(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        MemberDeclarationSyntax member,
        CancellationToken cancellationToken)
    {
        var newType = containingType.AddMembers(member.WithAdditionalAnnotations(Formatter.Annotation));
        var updatedRoot = root.ReplaceNode(containingType, newType.WithAdditionalAnnotations(Formatter.Annotation));
        return Task.FromResult(document.WithSyntaxRoot(updatedRoot));
    }
}
