using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI012: ignored TryAdd registrations.
/// Removes a redundant TryAdd* statement only when the ignored registration is a standalone statement.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI012_TryAddIgnoredCodeFixProvider))]
[Shared]
public sealed class DI012_TryAddIgnoredCodeFixProvider : CodeFixProvider
{
    internal const string RemoveIgnoredRegistrationEquivalenceKey = "DI012_RemoveIgnoredTryAddRegistration";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.TryAddIgnored);

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
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (!TryGetIgnoredRegistrationStatement(invocation, out var statement))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI012_FixTitle_RemoveIgnoredRegistration,
                createChangedDocument: cancellationToken => RemoveIgnoredRegistrationAsync(
                    context.Document,
                    statement,
                    cancellationToken),
                equivalenceKey: RemoveIgnoredRegistrationEquivalenceKey),
            diagnostic);
    }

    private static bool TryGetIgnoredRegistrationStatement(
        InvocationExpressionSyntax? invocation,
        out ExpressionStatementSyntax statement)
    {
        statement = null!;

        if (invocation?.Parent is not ExpressionStatementSyntax expressionStatement ||
            !ReferenceEquals(expressionStatement.Expression, invocation) ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var methodName = memberAccess.Name switch
        {
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(methodName) ||
            !methodName.StartsWith("TryAdd", StringComparison.Ordinal))
        {
            return false;
        }

        statement = expressionStatement;
        return true;
    }

    private static async Task<Document> RemoveIgnoredRegistrationAsync(
        Document document,
        ExpressionStatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (statement.Parent is BlockSyntax containingBlock)
        {
            var newBlock = containingBlock.WithStatements(containingBlock.Statements.Remove(statement));
            return document.WithSyntaxRoot(root.ReplaceNode(containingBlock, newBlock));
        }

        var newRoot = root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
        return newRoot is null ? document : document.WithSyntaxRoot(newRoot);
    }
}
