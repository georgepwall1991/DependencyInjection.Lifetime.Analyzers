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
/// Code fix for DI020: middleware captures a scoped or transient service in its constructor.
/// Offers a deterministic <c>#pragma warning disable</c> suppression around the constructor.
/// A move-to-Invoke-parameter rewrite is deferred to a follow-up; suppression is the safe
/// universal fallback that documents the captive intent explicitly.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI020_MiddlewareCaptiveDependencyCodeFixProvider))]
[Shared]
public sealed class DI020_MiddlewareCaptiveDependencyCodeFixProvider : CodeFixProvider
{
    internal const string SuppressEquivalenceKey = "DI020_Suppress";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.MiddlewareCaptiveDependency);

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

        var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
        var constructor = node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();
        if (constructor is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI020_FixTitle_Suppress,
                createChangedDocument: c => SuppressWithPragmaAsync(context.Document, constructor, c),
                equivalenceKey: SuppressEquivalenceKey),
            diagnostic);
    }

    private static async Task<Document> SuppressWithPragmaAsync(
        Document document,
        ConstructorDeclarationSyntax constructor,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newLine = GetPreferredEndOfLine(sourceText);

        var leadingTrivia = constructor.GetLeadingTrivia();
        var indentation = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));

        var pragmaDisable = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.MiddlewareCaptiveDependency)),
                isActive: true));

        var pragmaRestore = SyntaxFactory.Trivia(
            SyntaxFactory.PragmaWarningDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.RestoreKeyword),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.IdentifierName(DiagnosticIds.MiddlewareCaptiveDependency)),
                isActive: true));

        var triviaBefore = SyntaxFactory.TriviaList(
            leadingTrivia.Concat(new[]
            {
                pragmaDisable,
                newLine,
                indentation
            }));

        var trailingTrivia = constructor.GetTrailingTrivia();
        var triviaAfter = SyntaxFactory.TriviaList(
            new[]
            {
                newLine,
                indentation,
                pragmaRestore
            }.Concat(trailingTrivia));

        var newConstructor = constructor
            .WithLeadingTrivia(triviaBefore)
            .WithTrailingTrivia(triviaAfter)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(constructor, newConstructor));
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
