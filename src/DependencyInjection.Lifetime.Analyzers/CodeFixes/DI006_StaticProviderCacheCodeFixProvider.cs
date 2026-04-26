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
/// Code fix provider for DI006: Static provider cache.
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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the node at the diagnostic location
        var node = root.FindNode(diagnosticSpan);

        // Check if it's a field declaration
        var fieldDeclaration = node.FirstAncestorOrSelf<FieldDeclarationSyntax>();
        if (fieldDeclaration is not null &&
            HasStaticModifier(fieldDeclaration.Modifiers) &&
            CanSafelyRemoveStatic(fieldDeclaration, semanticModel))
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
        if (propertyDeclaration is not null &&
            HasStaticModifier(propertyDeclaration.Modifiers) &&
            CanSafelyRemoveStatic(propertyDeclaration, semanticModel))
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

    private static bool CanSafelyRemoveStatic(MemberDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        var declaredSymbol = declaration switch
        {
            FieldDeclarationSyntax fieldDeclaration when fieldDeclaration.Declaration.Variables.Count == 1 =>
                semanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0]),
            PropertyDeclarationSyntax propertyDeclaration =>
                semanticModel.GetDeclaredSymbol(propertyDeclaration),
            _ => null
        };

        if (declaredSymbol is null)
        {
            return false;
        }

        var containingType = declaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containingType is null)
        {
            return false;
        }

        if (containingType.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
        {
            return false;
        }

        if (!IsPrivateMember(declaration))
        {
            return false;
        }

        if (IsPartialType(containingType))
        {
            return false;
        }

        foreach (var identifier in containingType.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (declaration.Span.Contains(identifier.Span))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (!SymbolEqualityComparer.Default.Equals(symbol, declaredSymbol))
            {
                continue;
            }

            if (IsInStaticContext(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInStaticContext(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case MemberDeclarationSyntax memberDeclaration when HasStaticModifier(memberDeclaration.GetModifiers()):
                    return true;
                case AccessorDeclarationSyntax accessorDeclaration:
                {
                    var accessorParent = accessorDeclaration.Parent?.Parent as MemberDeclarationSyntax;
                    if (accessorParent is not null && HasStaticModifier(accessorParent.GetModifiers()))
                    {
                        return true;
                    }

                    break;
                }
                case LocalFunctionStatementSyntax localFunction when localFunction.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)):
                    return true;
            }
        }

        return false;
    }

    private static bool IsPrivateMember(MemberDeclarationSyntax declaration)
    {
        var modifiers = declaration switch
        {
            FieldDeclarationSyntax field => field.Modifiers,
            PropertyDeclarationSyntax property => property.Modifiers,
            _ => default,
        };

        if (modifiers == default)
        {
            return false;
        }

        // If any public/internal/protected modifier is present, the member is visible
        // outside the type and removing static could break external references.
        // No accessibility modifier means implicitly private (safe).
        return !modifiers.Any(m =>
            m.IsKind(SyntaxKind.PublicKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword));
    }

    private static bool IsPartialType(TypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
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

        var staticToken = fieldDeclaration.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.StaticKeyword));
        var newModifiers = RemoveStaticModifier(fieldDeclaration.Modifiers);

        var newFieldDeclaration = fieldDeclaration.WithModifiers(newModifiers);

        // When static was the only modifier, the declaration loses its leading trivia
        // (indentation). Restore it from the removed static token.
        if (!newModifiers.Any() && staticToken != default)
        {
            newFieldDeclaration = newFieldDeclaration.WithLeadingTrivia(staticToken.LeadingTrivia);
        }

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

        var staticToken = propertyDeclaration.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.StaticKeyword));
        var newModifiers = RemoveStaticModifier(propertyDeclaration.Modifiers);

        var newPropertyDeclaration = propertyDeclaration.WithModifiers(newModifiers);

        if (!newModifiers.Any() && staticToken != default)
        {
            newPropertyDeclaration = newPropertyDeclaration.WithLeadingTrivia(staticToken.LeadingTrivia);
        }

        return document.WithSyntaxRoot(root.ReplaceNode(propertyDeclaration, newPropertyDeclaration));
    }

    private static SyntaxTokenList RemoveStaticModifier(SyntaxTokenList modifiers)
    {
        var staticToken = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.StaticKeyword));
        if (staticToken == default)
        {
            return modifiers;
        }

        var newModifiers = SyntaxFactory.TokenList(
            modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));

        if (!newModifiers.Any())
        {
            return newModifiers;
        }

        if (modifiers[0].IsKind(SyntaxKind.StaticKeyword))
        {
            var firstToken = newModifiers[0];
            newModifiers = newModifiers.Replace(
                firstToken,
                firstToken.WithLeadingTrivia(staticToken.LeadingTrivia));
        }

        return newModifiers;
    }
}

internal static class MemberDeclarationSyntaxExtensions
{
    public static SyntaxTokenList GetModifiers(this MemberDeclarationSyntax memberDeclaration) => memberDeclaration switch
    {
        BaseFieldDeclarationSyntax fieldDeclaration => fieldDeclaration.Modifiers,
        BaseMethodDeclarationSyntax methodDeclaration => methodDeclaration.Modifiers,
        BasePropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Modifiers,
        _ => default
    };
}
