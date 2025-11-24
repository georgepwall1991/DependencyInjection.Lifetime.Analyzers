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

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI006: Static IServiceProvider cache.
/// Offers to remove the 'static' modifier from fields and properties.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI006_StaticProviderCacheCodeFixProvider))]
[Shared]
public sealed class DI006_StaticProviderCacheCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "DI006_RemoveStatic";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.StaticProviderCache);

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

        // Find the node at the diagnostic location
        var node = root.FindNode(diagnosticSpan);

        // Check if it's a field declaration
        var fieldDeclaration = node.FirstAncestorOrSelf<FieldDeclarationSyntax>();
        if (fieldDeclaration is not null && HasStaticModifier(fieldDeclaration.Modifiers))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI006_FixTitle_RemoveStatic,
                    createChangedDocument: c => RemoveStaticFromFieldAsync(context.Document, fieldDeclaration, c),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
            return;
        }

        // Check if it's a property declaration
        var propertyDeclaration = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (propertyDeclaration is not null && HasStaticModifier(propertyDeclaration.Modifiers))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI006_FixTitle_RemoveStatic,
                    createChangedDocument: c => RemoveStaticFromPropertyAsync(context.Document, propertyDeclaration, c),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    private static bool HasStaticModifier(SyntaxTokenList modifiers)
    {
        return modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
    }

    private static async Task<Document> RemoveStaticFromFieldAsync(
        Document document,
        FieldDeclarationSyntax fieldDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newModifiers = RemoveStaticModifier(fieldDeclaration.Modifiers);
        var newFieldDeclaration = fieldDeclaration.WithModifiers(newModifiers);

        return document.WithSyntaxRoot(root.ReplaceNode(fieldDeclaration, newFieldDeclaration));
    }

    private static async Task<Document> RemoveStaticFromPropertyAsync(
        Document document,
        PropertyDeclarationSyntax propertyDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newModifiers = RemoveStaticModifier(propertyDeclaration.Modifiers);
        var newPropertyDeclaration = propertyDeclaration.WithModifiers(newModifiers);

        return document.WithSyntaxRoot(root.ReplaceNode(propertyDeclaration, newPropertyDeclaration));
    }

    private static SyntaxTokenList RemoveStaticModifier(SyntaxTokenList modifiers)
    {
        var staticToken = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.StaticKeyword));
        if (staticToken == default)
        {
            return modifiers;
        }

        // Remove the static token
        var newModifiers = SyntaxFactory.TokenList(
            modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));

        // Preserve trivia: if static was first, move its leading trivia to the next token
        if (modifiers.Count > 1 && modifiers[0].IsKind(SyntaxKind.StaticKeyword))
        {
            var leadingTrivia = staticToken.LeadingTrivia;
            if (newModifiers.Any())
            {
                var firstNewToken = newModifiers[0];
                newModifiers = newModifiers.Replace(
                    firstNewToken,
                    firstNewToken.WithLeadingTrivia(leadingTrivia));
            }
        }

        return newModifiers;
    }
}
