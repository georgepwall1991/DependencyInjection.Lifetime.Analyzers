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
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI002: Scoped service escapes its scope lifetime.
/// Offers explicit suppression when the escape is intentional and has been reviewed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI002_ScopeEscapeCodeFixProvider))]
[Shared]
public sealed class DI002_ScopeEscapeCodeFixProvider : CodeFixProvider
{
    private const string SuppressEquivalenceKey = "DI002_Suppress";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ScopedServiceEscapes);

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

        // Find the invocation (GetService/GetRequiredService call)
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // Register "Suppress with pragma" fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI002_FixTitle_Suppress,
                createChangedDocument: c => SuppressWithPragmaAsync(context.Document, invocation, c),
                equivalenceKey: SuppressEquivalenceKey),
            diagnostic);
    }

    private static async Task<Document> SuppressWithPragmaAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        // Find the containing statement
        var containingStatement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (containingStatement is null)
        {
            return document;
        }

        // Get the leading trivia and indentation
        var leadingTrivia = containingStatement.GetLeadingTrivia();
        var indentation = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));

        // Create pragma disable directive
        var pragmaDisable = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.ScopedServiceEscapes)),
                isActive: true));

        var newLine = GetPreferredEndOfLine(sourceText);

        // Build trivia before the statement
        var triviaBeforeStatement = SyntaxFactory.TriviaList(
            leadingTrivia.Concat(new[]
            {
                pragmaDisable,
                newLine,
                indentation
            }));

        // Create pragma restore directive for after the statement
        var pragmaRestore = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.RestoreKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.ScopedServiceEscapes)),
                isActive: true));

        // Get trailing trivia
        var trailingTrivia = containingStatement.GetTrailingTrivia();

        // Build trivia after the statement
        var triviaAfterStatement = SyntaxFactory.TriviaList(
            new[]
            {
                newLine,
                indentation,
                pragmaRestore
            }.Concat(trailingTrivia));

        var newStatement = containingStatement
            .WithLeadingTrivia(triviaBeforeStatement)
            .WithTrailingTrivia(triviaAfterStatement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(containingStatement, newStatement));
    }

    private static SyntaxTrivia GetPreferredEndOfLine(SourceText sourceText)
    {
        for (var i = 0; i < sourceText.Length; i++)
        {
            if (sourceText[i] != '\n')
            {
                continue;
            }

            return i > 0 && sourceText[i - 1] == '\r'
                ? SyntaxFactory.CarriageReturnLineFeed
                : SyntaxFactory.LineFeed;
        }

        return SyntaxFactory.LineFeed;
    }
}
