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
/// Code fix provider for DI019: Scoped service resolved from root provider.
/// Offers to wrap the resolution in a new service scope.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI019_RootScopedResolutionCodeFixProvider))]
[Shared]
public sealed class DI019_RootScopedResolutionCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.RootScopedResolution);

    /// <inheritdoc />
    public sealed override FixAllProvider? GetFixAllProvider() => null;

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

        // Find the invocation. getInnermostNodeForTie ensures we resolve the flagged resolution
        // call itself (e.g. the inner `provider.GetRequiredService<T>()`) rather than an enclosing
        // invocation when the diagnostic span ties with a wrapping argument node.
        var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // We only support normal member access (no null-conditional member binding)
        if (invocation.Expression is not MemberAccessExpressionSyntax)
        {
            return;
        }

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null || !CanWrapInScope(statement, invocation, semanticModel))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI019_FixTitle_WrapInScope,
                createChangedDocument: c => WrapInScopeAsync(context.Document, invocation, c),
                equivalenceKey: nameof(Resources.DI019_FixTitle_WrapInScope)),
            diagnostic);
    }

    private static bool CanWrapInScope(
        StatementSyntax statement,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        // A resolution evaluated inside a conditional access (`host?.Services.GetRequiredService<T>()`)
        // exposes its provider chain through a MemberBindingExpressionSyntax. Lifting that receiver into
        // `using var scope = ....CreateScope();` would emit a standalone member binding that does not
        // compile, and wrapping the statement would also drop the null-shortcut semantics, so refuse.
        for (SyntaxNode? node = invocation.Parent;
             node is not null && node is not StatementSyntax;
             node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(invocation.Span))
            {
                return false;
            }
        }

        var receiver = memberAccess.Expression;

        // The fix calls `.CreateScope()` on the resolution receiver, so that receiver must be a
        // provider *value*. A static extension-method call such as
        // `ServiceProviderServiceExtensions.GetRequiredService<T>(provider)` exposes the declaring
        // type as the member-access receiver; rewriting it to `SomeType.CreateScope()` would not
        // compile. Reject any receiver that binds to a type rather than an instance.
        if (semanticModel.GetSymbolInfo(receiver).Symbol is ITypeSymbol)
        {
            return false;
        }

        // `CreateScope` / `CreateAsyncScope` are extension methods on `IServiceProvider` from
        // `Microsoft.Extensions.DependencyInjection`. When the diagnostic site resolves through the
        // core `IServiceProvider.GetService(Type)` member, that namespace may not be imported, in
        // which case the rewritten `provider.CreateScope()` would not compile. Only offer the fix
        // when the extension is actually in scope on the receiver type.
        var receiverType = semanticModel.GetTypeInfo(receiver).Type;
        var requiredScopeMethodName = IsInAsyncContext(statement) ? "CreateAsyncScope" : "CreateScope";
        if (receiverType is null ||
            semanticModel.LookupSymbols(
                invocation.SpanStart,
                receiverType,
                name: requiredScopeMethodName,
                includeReducedExtensionMethods: true).IsEmpty)
        {
            return false;
        }

        if (statement is ExpressionStatementSyntax)
        {
            // Wrapping the statement in a scope is only safe when the resolved service is
            // not stored beyond that scope. It is fine to discard the result or to use it as
            // the transient receiver of a member call (e.g. `provider.Get<T>().DoWork();`),
            // but assigning it or passing it as an argument can let the service outlive the
            // scope we are about to dispose, turning DI019 into a use-after-dispose bug.
            return ResultStaysWithinStatement(invocation);
        }

        if (statement is not LocalDeclarationStatementSyntax localDeclaration)
        {
            return false;
        }

        // The local lives until the end of its enclosing block, but the scope we insert is disposed
        // there too, so the resolved service must not outlive that block. Be strict: the local may
        // only be used as the receiver of a member access (`service.DoWork()` / `service?.X`). Any
        // other use -- argument, assignment, return, initializer, ref/out, lambda capture, cast --
        // can store the service beyond the scope, so we refuse the fix rather than risk a
        // use-after-dispose rewrite.
        var localReferenceRoot = GetLocalReferenceSearchRoot(statement);
        if (localReferenceRoot is null)
        {
            return false;
        }

        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            var localSymbol = semanticModel.GetDeclaredSymbol(variable);
            if (localSymbol is null)
            {
                return false;
            }

            foreach (var node in localReferenceRoot.DescendantNodes())
            {
                if (node is not IdentifierNameSyntax identifier ||
                    !SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(identifier).Symbol, localSymbol))
                {
                    continue;
                }

                if (identifier.Ancestors().TakeWhile(ancestor => ancestor != localReferenceRoot).Any(
                        ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                {
                    return false;
                }

                // Every reference must be a transient, discarded use (e.g. `service.DoWork();`).
                // If the reference -- or a value produced from it, such as `service.ToList()` -- can
                // flow into an assignment, argument, return, or initializer, the scoped service can
                // outlive the scope we insert, so we refuse the fix.
                if (!ResultStaysWithinStatement(identifier))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SyntaxNode? GetLocalReferenceSearchRoot(StatementSyntax statement)
    {
        var containingCallable = statement.Ancestors().FirstOrDefault(
            ancestor => ancestor is BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax);
        if (containingCallable is not null)
        {
            return containingCallable;
        }

        if (statement.FirstAncestorOrSelf<GlobalStatementSyntax>() is { Parent: CompilationUnitSyntax compilationUnit })
        {
            return compilationUnit;
        }

        return null;
    }

    /// <summary>
    /// Returns true only when the value produced by <paramref name="expression"/> flows straight up
    /// to its enclosing expression statement through receiver positions (member access, invocation,
    /// conditional access, <c>await</c>), meaning it is discarded or used transiently. As soon as the
    /// value is consumed as an argument, an assignment right-hand side, an initializer, or captured by
    /// a lambda, it can escape the scope we would insert, so the fix must not be offered. Used both for
    /// the resolution expression itself and for every reference to a resolved local.
    /// </summary>
    private static bool ResultStaysWithinStatement(SyntaxNode expression)
    {
        SyntaxNode current = expression;
        for (var parent = current.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case ExpressionStatementSyntax:
                    return true;
                case ParenthesizedExpressionSyntax:
                case MemberAccessExpressionSyntax member when member.Expression == current:
                case MemberBindingExpressionSyntax:
                case ConditionalAccessExpressionSyntax conditional when conditional.Expression == current:
                case InvocationExpressionSyntax outer when outer.Expression == current:
                case AwaitExpressionSyntax await when await.Expression == current:
                case PostfixUnaryExpressionSyntax postfix when postfix.Operand == current:
                    current = parent;
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    private static async Task<Document> WrapInScopeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return document;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return document;
        }

        var providerExpression = memberAccess.Expression;

        var scopeVariableName = GenerateUniqueName(invocation, "scope");

        // In an async context, dispose the scope asynchronously. A scoped service may implement
        // only IAsyncDisposable, and Microsoft DI throws if such a scope is disposed synchronously,
        // so an `await using` scope created via CreateAsyncScope() is the safe rewrite. Outside an
        // async callable we keep the synchronous using/CreateScope() form.
        var isAsync = IsInAsyncContext(statement);
        var createScopeMethodName = isAsync ? "CreateAsyncScope" : "CreateScope";

        // Create: scope.ServiceProvider
        var scopeProviderAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(scopeVariableName),
            SyntaxFactory.IdentifierName("ServiceProvider"));

        // Create: provider.CreateScope() / provider.CreateAsyncScope()
        var createScopeInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                providerExpression,
                SyntaxFactory.IdentifierName(createScopeMethodName)));

        var newExpression = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            scopeProviderAccess,
            memberAccess.Name);

        var newInvocation = invocation.WithExpression(newExpression);

        var awaitKeyword = isAsync
            ? SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space)
            : default;

        // If it's a local declaration, we can use '(await) using var scope = ...'
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            var leadingTrivia = localDeclaration.GetLeadingTrivia();

            // [await] using var scope = provider.Create[Async]Scope();
            var scopeDeclaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(scopeVariableName))
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(createScopeInvocation)))))
                .WithAwaitKeyword(awaitKeyword)
                .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(SyntaxFactory.LineFeed);

            var newStatement = localDeclaration.ReplaceNode(invocation, newInvocation);

            if (localDeclaration.Parent is GlobalStatementSyntax globalStatement)
            {
                var scopeGlobalStatement = SyntaxFactory.GlobalStatement(scopeDeclaration);
                var newGlobalStatement = globalStatement.WithStatement(newStatement);
                var globalNodes = new SyntaxNode[] { scopeGlobalStatement, newGlobalStatement };
                return document.WithSyntaxRoot(root.ReplaceNode(globalStatement, globalNodes));
            }

            var nodes = new SyntaxNode[] { scopeDeclaration, newStatement };
            return document.WithSyntaxRoot(root.ReplaceNode(localDeclaration, nodes));
        }

        // For other statements, fallback to an (await) using block.
        var variableDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(
                    SyntaxFactory.Identifier(scopeVariableName))
                .WithInitializer(
                    SyntaxFactory.EqualsValueClause(createScopeInvocation))));

        var newOtherStatement = statement.ReplaceNode(invocation, newInvocation);

        var usingStatement = SyntaxFactory.UsingStatement(
            variableDeclaration,
            null,
            SyntaxFactory.Block(newOtherStatement))
            .WithAwaitKeyword(awaitKeyword)
            .WithTriviaFrom(statement);

        return document.WithSyntaxRoot(root.ReplaceNode(statement, usingStatement));
    }

    /// <summary>
    /// Returns true when the nearest enclosing callable or top-level program body is async,
    /// so the rewritten scope can use
    /// <c>await using</c> / <c>CreateAsyncScope()</c>.
    /// </summary>
    private static bool IsInAsyncContext(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LockStatementSyntax:
                    return false;
                case GlobalStatementSyntax globalStatement:
                    return IsAsyncTopLevelContext(globalStatement);
                case AccessorDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool IsAsyncTopLevelContext(GlobalStatementSyntax globalStatement)
    {
        if (globalStatement.Parent is not CompilationUnitSyntax compilationUnit)
        {
            return false;
        }

        foreach (var statement in compilationUnit.Members.OfType<GlobalStatementSyntax>())
        {
            foreach (var awaitExpression in statement.DescendantNodes().OfType<AwaitExpressionSyntax>())
            {
                if (!awaitExpression.Ancestors().TakeWhile(ancestor => ancestor != statement).Any(
                        ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GenerateUniqueName(SyntaxNode node, string baseName)
    {
        var containingScope = node.AncestorsAndSelf().FirstOrDefault(
            ancestor => ancestor is BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax
                or AnonymousFunctionExpressionSyntax);
        containingScope ??= node.FirstAncestorOrSelf<GlobalStatementSyntax>()?.Parent;

        var usedNames = (containingScope ?? node)
            .DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText)
            .ToImmutableHashSet();

        var name = baseName;
        var index = 1;
        while (usedNames.Contains(name))
        {
            name = baseName + index++;
        }
        return name;
    }
}
