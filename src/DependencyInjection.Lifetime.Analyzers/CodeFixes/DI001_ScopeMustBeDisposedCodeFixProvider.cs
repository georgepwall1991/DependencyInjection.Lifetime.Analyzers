using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI001: IServiceScope must be disposed.
/// Offers to add 'using' or 'await using' statement to ensure disposal.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI001_ScopeMustBeDisposedCodeFixProvider))]
[Shared]
public sealed class DI001_ScopeMustBeDisposedCodeFixProvider : CodeFixProvider
{
    private const string AddUsingEquivalenceKey = "DI001_AddUsing";
    private const string AddAwaitUsingEquivalenceKey = "DI001_AddAwaitUsing";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ScopeMustBeDisposed);

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
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // Find the local declaration statement containing this invocation
        var localDeclaration = FindLocalDeclarationStatement(invocation);
        if (localDeclaration is null)
        {
            return;
        }

        // Check if we're in an async context
        var isAsyncContext = IsInAsyncContext(invocation);
        var isCreateAsyncScope = IsCreateAsyncScopeInvocation(invocation);

        // Always offer "Add 'using' statement" option
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI001_FixTitle_AddUsing,
                createChangedDocument: c => AddUsingStatementAsync(context.Document, localDeclaration, c),
                equivalenceKey: AddUsingEquivalenceKey),
            diagnostic);

        // Offer "Add 'await using' statement" if in async context or using CreateAsyncScope
        if (isAsyncContext || isCreateAsyncScope)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI001_FixTitle_AddAwaitUsing,
                    createChangedDocument: c => AddAwaitUsingStatementAsync(context.Document, localDeclaration, invocation, c),
                    equivalenceKey: AddAwaitUsingEquivalenceKey),
                diagnostic);
        }
    }

    private static LocalDeclarationStatementSyntax? FindLocalDeclarationStatement(InvocationExpressionSyntax invocation)
    {
        // Pattern: var scope = _factory.CreateScope();
        var syntax = invocation.Parent;
        while (syntax is not null)
        {
            if (syntax is LocalDeclarationStatementSyntax localDecl)
            {
                return localDecl;
            }

            // Stop at statement boundaries
            if (syntax is StatementSyntax)
            {
                break;
            }

            syntax = syntax.Parent;
        }

        return null;
    }

    private static bool IsInAsyncContext(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method when method.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return true;
                case LocalFunctionStatementSyntax localFunc when localFunc.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return true;
                case ParenthesizedLambdaExpressionSyntax lambda when lambda.AsyncKeyword != default:
                    return true;
                case SimpleLambdaExpressionSyntax simpleLambda when simpleLambda.AsyncKeyword != default:
                    return true;
                case AnonymousMethodExpressionSyntax anonymousMethod when anonymousMethod.AsyncKeyword != default:
                    return true;
                // Stop at method boundaries
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    return false;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsCreateAsyncScopeInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "CreateAsyncScope";
        }

        return false;
    }

    private static async Task<Document> AddUsingStatementAsync(
        Document document,
        LocalDeclarationStatementSyntax localDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Get the leading trivia from the first token of the declaration
        var leadingTrivia = localDeclaration.GetLeadingTrivia();

        // Add 'using' keyword to the local declaration
        var usingKeyword = SyntaxFactory.Token(SyntaxKind.UsingKeyword)
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Remove leading trivia from the declaration and add using keyword
        var newDeclaration = localDeclaration
            .WithoutLeadingTrivia()
            .WithUsingKeyword(usingKeyword);

        return document.WithSyntaxRoot(root.ReplaceNode(localDeclaration, newDeclaration));
    }

    private static async Task<Document> AddAwaitUsingStatementAsync(
        Document document,
        LocalDeclarationStatementSyntax localDeclaration,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Get the leading trivia from the first token of the declaration
        var leadingTrivia = localDeclaration.GetLeadingTrivia();

        // Check if we need to change CreateScope to CreateAsyncScope
        var isCreateScope = invocation.Expression is MemberAccessExpressionSyntax memberAccess
                            && memberAccess.Name.Identifier.Text == "CreateScope";

        LocalDeclarationStatementSyntax newDeclaration;

        if (isCreateScope)
        {
            // Replace CreateScope with CreateAsyncScope first
            var newInvocation = ReplaceCreateScopeWithCreateAsyncScope(invocation);
            newDeclaration = localDeclaration.ReplaceNode(invocation, newInvocation);
        }
        else
        {
            newDeclaration = localDeclaration;
        }

        // Add 'await' and 'using' keywords
        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var usingKeyword = SyntaxFactory.Token(SyntaxKind.UsingKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Remove leading trivia from declaration and add await/using keywords
        newDeclaration = newDeclaration
            .WithoutLeadingTrivia()
            .WithAwaitKeyword(awaitKeyword)
            .WithUsingKeyword(usingKeyword);

        return document.WithSyntaxRoot(root.ReplaceNode(localDeclaration, newDeclaration));
    }

    private static InvocationExpressionSyntax ReplaceCreateScopeWithCreateAsyncScope(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var newName = SyntaxFactory.IdentifierName("CreateAsyncScope")
                .WithTriviaFrom(memberAccess.Name);

            var newMemberAccess = memberAccess.WithName(newName);
            return invocation.WithExpression(newMemberAccess);
        }

        return invocation;
    }
}
