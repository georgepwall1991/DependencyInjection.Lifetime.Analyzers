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
/// Code fix provider for DI005: Use CreateAsyncScope in async methods.
/// Transforms CreateScope() to CreateAsyncScope() and adds 'await' to the using statement.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI005_AsyncScopeRequiredCodeFixProvider))]
[Shared]
public sealed class DI005_AsyncScopeRequiredCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "DI005_UseCreateAsyncScope";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.AsyncScopeRequired);

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

        // Find the CreateScope invocation
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI005_FixTitle,
                createChangedDocument: c => UseCreateAsyncScopeAsync(context.Document, invocation, c),
                equivalenceKey: EquivalenceKey),
            diagnostic);
    }

    private static async Task<Document> UseCreateAsyncScopeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Check if invocation is in a local declaration with using (e.g., "using var scope = ...")
        var localDeclaration = invocation.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
        if (localDeclaration is not null && localDeclaration.UsingKeyword != default && localDeclaration.AwaitKeyword == default)
        {
            var newLocalDeclaration = TransformUsingVarDeclaration(localDeclaration, invocation);
            return document.WithSyntaxRoot(root.ReplaceNode(localDeclaration, newLocalDeclaration));
        }

        // Check if invocation is in a using statement (block form)
        var usingStatement = invocation.FirstAncestorOrSelf<UsingStatementSyntax>();
        if (usingStatement is not null && usingStatement.AwaitKeyword == default)
        {
            var newUsingStatement = TransformUsingStatement(usingStatement, invocation);
            return document.WithSyntaxRoot(root.ReplaceNode(usingStatement, newUsingStatement));
        }

        // Just replace the invocation if not in a using context
        var newInvocation = ReplaceCreateScopeWithCreateAsyncScope(invocation);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    private static LocalDeclarationStatementSyntax TransformUsingVarDeclaration(
        LocalDeclarationStatementSyntax localDeclaration,
        InvocationExpressionSyntax invocation)
    {
        // First, replace CreateScope with CreateAsyncScope in the declaration
        var newInvocation = ReplaceCreateScopeWithCreateAsyncScope(invocation);
        var newDeclaration = localDeclaration.ReplaceNode(invocation, newInvocation);

        // Then add the await keyword
        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithLeadingTrivia(newDeclaration.UsingKeyword.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newUsingKeyword = newDeclaration.UsingKeyword.WithLeadingTrivia();

        return newDeclaration
            .WithAwaitKeyword(awaitKeyword)
            .WithUsingKeyword(newUsingKeyword);
    }

    private static UsingStatementSyntax TransformUsingStatement(
        UsingStatementSyntax usingStatement,
        InvocationExpressionSyntax invocation)
    {
        // First, replace CreateScope with CreateAsyncScope in the statement
        var newInvocation = ReplaceCreateScopeWithCreateAsyncScope(invocation);
        var newUsingStatement = usingStatement.ReplaceNode(invocation, newInvocation);

        // Then add the await keyword
        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithLeadingTrivia(newUsingStatement.UsingKeyword.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newUsingKeyword = newUsingStatement.UsingKeyword.WithLeadingTrivia();

        return newUsingStatement
            .WithAwaitKeyword(awaitKeyword)
            .WithUsingKeyword(newUsingKeyword);
    }

    private static InvocationExpressionSyntax ReplaceCreateScopeWithCreateAsyncScope(InvocationExpressionSyntax invocation)
    {
        // Change the method name from CreateScope to CreateAsyncScope
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var newName = SyntaxFactory.IdentifierName("CreateAsyncScope")
                .WithTriviaFrom(memberAccess.Name);

            var newMemberAccess = memberAccess.WithName(newName);
            return invocation.WithExpression(newMemberAccess);
        }

        // Handle direct identifier case (rare)
        if (invocation.Expression is IdentifierNameSyntax)
        {
            var newName = SyntaxFactory.IdentifierName("CreateAsyncScope")
                .WithTriviaFrom(invocation.Expression);

            return invocation.WithExpression(newName);
        }

        return invocation;
    }
}
