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
using Microsoft.CodeAnalysis.Editing;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI014_RootProviderNotDisposedCodeFixProvider)), Shared]
public sealed class DI014_RootProviderNotDisposedCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.RootProviderNotDisposed);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
        if (invocation is null) return;

        // Check if we can fix this:
        // 1. It must be assigned to a variable
        // 2. That variable must be a local declaration
        
        if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator &&
            declarator.Parent is VariableDeclarationSyntax declaration &&
            declaration.Parent is LocalDeclarationStatementSyntax localDeclaration &&
            declaration.Variables.Count == 1)
        {
             context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Dispose service provider",
                    createChangedDocument: c => AddUsingStatementAsync(context.Document, localDeclaration, c),
                    equivalenceKey: nameof(DI014_RootProviderNotDisposedCodeFixProvider)),
                diagnostic);
        }
    }

    private async Task<Document> AddUsingStatementAsync(Document document, LocalDeclarationStatementSyntax localDeclaration, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // Capture leading trivia (indentation, comments) from the start of the statement
        var leadingTrivia = localDeclaration.GetLeadingTrivia();
        
        // Remove leading trivia from the original statement so it doesn't end up after 'using'
        var declarationWithoutTrivia = localDeclaration.WithoutLeadingTrivia();

        LocalDeclarationStatementSyntax newLocalDeclaration;

        if (IsAsyncMethod(localDeclaration))
        {
             // await using var ...
             // Apply leading trivia to 'await'
            newLocalDeclaration = declarationWithoutTrivia
                .WithAwaitKeyword(SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                    .WithLeadingTrivia(leadingTrivia)
                    .WithTrailingTrivia(SyntaxFactory.Space))
                .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space));
        }
        else
        {
            // using var ...
            // Apply leading trivia to 'using'
            newLocalDeclaration = declarationWithoutTrivia
                .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                    .WithLeadingTrivia(leadingTrivia)
                    .WithTrailingTrivia(SyntaxFactory.Space));
        }

        // Preserve trailing trivia
        newLocalDeclaration = newLocalDeclaration.WithTrailingTrivia(localDeclaration.GetTrailingTrivia());

        editor.ReplaceNode(localDeclaration, newLocalDeclaration);

        return editor.GetChangedDocument();
    }

    private static bool IsAsyncMethod(SyntaxNode node)
    {
        // Walk ancestors and return the asyncness of the NEAREST enclosing callable,
        // not the first method declaration (which may be an outer non-async wrapper).
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
                case LambdaExpressionSyntax lambda:
                    return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    return anonymousMethod.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
            }
        }

        return false;
    }
}
