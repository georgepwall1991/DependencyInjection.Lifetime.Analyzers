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
        if (!TryGetIgnoredRegistrationRemovalTarget(invocation, out var removalTarget))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI012_FixTitle_RemoveIgnoredRegistration,
                createChangedDocument: cancellationToken => RemoveIgnoredRegistrationAsync(
                    context.Document,
                    removalTarget,
                    cancellationToken),
                equivalenceKey: RemoveIgnoredRegistrationEquivalenceKey),
            diagnostic);
    }

    private static bool TryGetIgnoredRegistrationRemovalTarget(
        InvocationExpressionSyntax? invocation,
        out SyntaxNode removalTarget)
    {
        removalTarget = null!;

        if (invocation is null)
        {
            return false;
        }

        // Extract the method name from either a direct member access or a member-binding
        // expression (the conditional-access form `services?.TryAddSingleton(...)`).
        SimpleNameSyntax? methodNameSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            _ => null,
        };

        if (methodNameSyntax is null)
        {
            return false;
        }

        var methodName = methodNameSyntax switch
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

        // The expression statement is either the invocation's parent (direct call) or the
        // enclosing conditional access's parent (`services?.TryAdd*(...)` form).
        ExpressionSyntax statementExpression = invocation;
        if (invocation.Parent is MemberBindingExpressionSyntax)
        {
            // Should not happen because we already handled the invocation.Expression branch,
            // but be defensive in case of unexpected shapes.
            return false;
        }
        if (invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull == invocation)
        {
            statementExpression = conditionalAccess;
        }

        if (statementExpression.Parent is not ExpressionStatementSyntax expressionStatement ||
            !ReferenceEquals(expressionStatement.Expression, statementExpression))
        {
            return false;
        }

        removalTarget = expressionStatement.Parent switch
        {
            BlockSyntax => expressionStatement,
            GlobalStatementSyntax globalStatement => globalStatement,
            _ => null!,
        };

        return removalTarget is not null;
    }

    private static async Task<Document> RemoveIgnoredRegistrationAsync(
        Document document,
        SyntaxNode removalTarget,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newRoot = removalTarget switch
        {
            ExpressionStatementSyntax { Parent: BlockSyntax containingBlock } expressionStatement =>
                root.ReplaceNode(
                    containingBlock,
                    containingBlock.WithStatements(containingBlock.Statements.Remove(expressionStatement))),
            GlobalStatementSyntax { Parent: CompilationUnitSyntax compilationUnit } globalStatement =>
                root.ReplaceNode(
                    compilationUnit,
                    compilationUnit.WithMembers(compilationUnit.Members.Remove(globalStatement))),
            _ => null,
        };

        return newRoot is null ? document : document.WithSyntaxRoot(newRoot);
    }
}
