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
/// Code fix provider for DI004: service used after scope disposed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI004_UseAfterDisposeCodeFixProvider))]
[Shared]
public sealed class DI004_UseAfterDisposeCodeFixProvider : CodeFixProvider
{
    private const string MoveUseIntoScopeEquivalenceKey = "DI004_MoveUseIntoScope";
    private const string SuppressEquivalenceKey = "DI004_Suppress";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.UseAfterScopeDisposed);

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
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return;
        }

        if (CanMoveIntoImmediatelyPrecedingScope(statement))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI004_FixTitle_MoveUseIntoScope,
                    createChangedDocument: c => MoveUseIntoScopeAsync(context.Document, statement, c),
                    equivalenceKey: MoveUseIntoScopeEquivalenceKey),
                diagnostic);
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI004_FixTitle_Suppress,
                createChangedDocument: c => SuppressWithPragmaAsync(context.Document, statement, c),
                equivalenceKey: SuppressEquivalenceKey),
            diagnostic);
    }

    private static bool CanMoveIntoImmediatelyPrecedingScope(StatementSyntax statement)
    {
        if (statement is not ExpressionStatementSyntax)
        {
            return false;
        }

        return TryGetImmediatelyPrecedingUsingBlock(
            statement,
            out _,
            out _,
            out _);
    }

    private static async Task<Document> MoveUseIntoScopeAsync(
        Document document,
        StatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null ||
            !TryGetImmediatelyPrecedingUsingBlock(statement, out var containingBlock, out var usingStatement, out var statementIndex) ||
            usingStatement.Statement is not BlockSyntax usingBlock)
        {
            return document;
        }

        var movedStatement = statement.WithAdditionalAnnotations(Formatter.Annotation);
        var newUsingBlock = usingBlock
            .WithStatements(usingBlock.Statements.Add(movedStatement))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var newUsingStatement = usingStatement
            .WithStatement(newUsingBlock)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newStatements = containingBlock.Statements
            .Replace(usingStatement, newUsingStatement)
            .RemoveAt(statementIndex);
        var newContainingBlock = containingBlock
            .WithStatements(newStatements)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(containingBlock, newContainingBlock));
    }

    private static bool TryGetImmediatelyPrecedingUsingBlock(
        StatementSyntax statement,
        out BlockSyntax containingBlock,
        out UsingStatementSyntax usingStatement,
        out int statementIndex)
    {
        containingBlock = null!;
        usingStatement = null!;
        statementIndex = -1;

        if (statement.Parent is not BlockSyntax parentBlock)
        {
            return false;
        }

        var statements = parentBlock.Statements;
        var index = statements.IndexOf(statement);
        if (index <= 0 ||
            statements[index - 1] is not UsingStatementSyntax { Statement: BlockSyntax } previousUsing)
        {
            return false;
        }

        containingBlock = parentBlock;
        usingStatement = previousUsing;
        statementIndex = index;
        return true;
    }

    private static async Task<Document> SuppressWithPragmaAsync(
        Document document,
        StatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var leadingTrivia = statement.GetLeadingTrivia();
        var indentation = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        var newLine = GetPreferredEndOfLine(sourceText);

        var pragmaDisable = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.UseAfterScopeDisposed)),
                isActive: true));

        var pragmaRestore = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.RestoreKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.UseAfterScopeDisposed)),
                isActive: true));

        var triviaBeforeStatement = SyntaxFactory.TriviaList(
            leadingTrivia.Concat(new[]
            {
                pragmaDisable,
                newLine,
                indentation
            }));

        var triviaAfterStatement = SyntaxFactory.TriviaList(
            new[]
            {
                newLine,
                indentation,
                pragmaRestore
            }.Concat(statement.GetTrailingTrivia()));

        var newStatement = statement
            .WithLeadingTrivia(triviaBeforeStatement)
            .WithTrailingTrivia(triviaAfterStatement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(statement, newStatement));
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
