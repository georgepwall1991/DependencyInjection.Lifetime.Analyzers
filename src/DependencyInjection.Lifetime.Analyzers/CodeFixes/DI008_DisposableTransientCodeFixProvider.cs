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
/// Code fix provider for DI008: Transient service implements IDisposable.
/// Offers to change to Scoped, Singleton, or use factory registration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI008_DisposableTransientCodeFixProvider))]
[Shared]
public sealed class DI008_DisposableTransientCodeFixProvider : CodeFixProvider
{
    private const string ChangeToScopedEquivalenceKey = "DI008_ChangeToScoped";
    private const string ChangeToSingletonEquivalenceKey = "DI008_ChangeToSingleton";
    private const string UseFactoryEquivalenceKey = "DI008_UseFactory";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.DisposableTransient);

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

        // Find the invocation
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // Verify this is an AddTransient call
        if (!IsAddTransientInvocation(invocation))
        {
            return;
        }

        // Offer to change to AddScoped
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI008_FixTitle_ChangeToScoped,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddScoped", c),
                equivalenceKey: ChangeToScopedEquivalenceKey),
            diagnostic);

        // Offer to change to AddSingleton
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI008_FixTitle_ChangeToSingleton,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddSingleton", c),
                equivalenceKey: ChangeToSingletonEquivalenceKey),
            diagnostic);

        // Offer to use factory registration
        // This is more complex as we need to transform the call
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI008_FixTitle_UseFactory,
                createChangedDocument: c => UseFactoryRegistrationAsync(context.Document, invocation, c),
                equivalenceKey: UseFactoryEquivalenceKey),
            diagnostic);
    }

    private static bool IsAddTransientInvocation(InvocationExpressionSyntax invocation)
    {
        // Check for xxx.AddTransient<...>(...) pattern
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name switch
            {
                GenericNameSyntax genericName => genericName.Identifier.Text,
                IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
                _ => null
            };

            return methodName?.StartsWith("AddTransient") == true;
        }

        return false;
    }

    private static async Task<Document> ChangeLifetimeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string newMethodName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Replace AddTransient with the new method name
        var newInvocation = ReplaceMethodName(invocation, newMethodName);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    private static InvocationExpressionSyntax ReplaceMethodName(InvocationExpressionSyntax invocation, string newMethodName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return invocation;
        }

        SimpleNameSyntax newName;

        switch (memberAccess.Name)
        {
            case GenericNameSyntax genericName:
                // AddTransient<TService, TImpl> -> AddScoped<TService, TImpl>
                newName = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(newMethodName),
                    genericName.TypeArgumentList).WithTriviaFrom(genericName);
                break;

            case IdentifierNameSyntax identifierName:
                // AddTransient -> AddScoped
                newName = SyntaxFactory.IdentifierName(newMethodName).WithTriviaFrom(identifierName);
                break;

            default:
                return invocation;
        }

        var newMemberAccess = memberAccess.WithName(newName);
        return invocation.WithExpression(newMemberAccess);
    }

    private static async Task<Document> UseFactoryRegistrationAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return document;
        }

        // Transform: services.AddTransient<IService, ServiceImpl>()
        // To:        services.AddTransient<IService>(sp => new ServiceImpl())
        var newInvocation = TransformToFactoryRegistration(invocation, semanticModel);
        if (newInvocation is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    private static InvocationExpressionSyntax? TransformToFactoryRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        // Extract type information from the generic method call
        string? serviceTypeName = null;
        string? implTypeName = null;

        if (memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArgs = genericName.TypeArgumentList.Arguments;
            if (typeArgs.Count >= 1)
            {
                serviceTypeName = typeArgs[0].ToString();
            }
            if (typeArgs.Count >= 2)
            {
                implTypeName = typeArgs[1].ToString();
            }
            else if (typeArgs.Count == 1)
            {
                // AddTransient<TService>() where TService is the implementation
                implTypeName = serviceTypeName;
            }
        }

        if (serviceTypeName is null || implTypeName is null)
        {
            return null;
        }

        // Create new method name: AddTransient<IService>
        var newGenericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("AddTransient"),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                    SyntaxFactory.ParseTypeName(serviceTypeName))));

        var newMemberAccess = memberAccess.WithName(newGenericName);

        // Create the lambda: sp => new ServiceImpl()
        var lambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier("sp")),
            SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.ParseTypeName(implTypeName))
                .WithArgumentList(SyntaxFactory.ArgumentList()));

        // Create new argument list with the lambda
        var newArguments = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(lambda)));

        return invocation
            .WithExpression(newMemberAccess)
            .WithArgumentList(newArguments);
    }
}
