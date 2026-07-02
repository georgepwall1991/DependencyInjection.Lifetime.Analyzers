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

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI025 and its DI026 scoped-publisher Info tier: event subscription
/// on a longer-lived publisher without a
/// matching unsubscription. Offers the narrow safe repair only — when the handler is a
/// method group and the subscriber already declares a Dispose method in source, insert the
/// mirrored -= statement at the top of that method. Introducing IDisposable on a type that
/// does not have it, or hoisting a lambda into a field, changes container disposal behavior
/// and is intentionally not offered.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI025_EventSubscriptionLeakCodeFixProvider))]
[Shared]
public sealed class DI025_EventSubscriptionLeakCodeFixProvider : CodeFixProvider
{
    private const string AddUnsubscribeEquivalenceKey = "DI025_AddUnsubscribeInDispose";

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
        if (containingType is null)
        {
            return;
        }

        var disposeMethod = FindDisposeMethod(containingType);
        if (disposeMethod?.Body is null ||
            !TypeImplementsDisposalContract(containingType, disposeMethod, semanticModel, context.CancellationToken))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Unsubscribe in '{disposeMethod.Identifier.ValueText}'",
                createChangedDocument: cancellationToken => AddUnsubscribeAsync(
                    context.Document, root, subscription, disposeMethod, cancellationToken),
                equivalenceKey: AddUnsubscribeEquivalenceKey),
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

    private static Task<Document> AddUnsubscribeAsync(
        Document document,
        SyntaxNode root,
        AssignmentExpressionSyntax subscription,
        MethodDeclarationSyntax disposeMethod,
        CancellationToken cancellationToken)
    {
        var unsubscribe = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SubtractAssignmentExpression,
                    subscription.Left.WithoutTrivia(),
                    Unwrap(subscription.Right).WithoutTrivia()))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var updatedBody = disposeMethod.Body!.WithStatements(
            disposeMethod.Body.Statements.Insert(0, unsubscribe));

        var updatedRoot = root.ReplaceNode(disposeMethod.Body!, updatedBody);

        return Task.FromResult(document.WithSyntaxRoot(updatedRoot));
    }
}
