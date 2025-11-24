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
/// Code fix provider for DI009: Open generic singleton captures scoped/transient dependency.
/// Offers to change the open generic's lifetime to match the dependency's lifetime.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI009_OpenGenericLifetimeMismatchCodeFixProvider))]
[Shared]
public sealed class DI009_OpenGenericLifetimeMismatchCodeFixProvider : CodeFixProvider
{
    private const string ChangeToScopedEquivalenceKey = "DI009_ChangeToScoped";
    private const string ChangeToTransientEquivalenceKey = "DI009_ChangeToTransient";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.OpenGenericLifetimeMismatch);

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

        // Find the invocation (e.g., services.AddSingleton(typeof(IRepository<>), typeof(Repository<>)))
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // Determine the current lifetime from the method name
        var currentLifetime = GetCurrentLifetime(invocation);
        if (currentLifetime != "Singleton")
        {
            // Only singleton open generics can have this issue
            return;
        }

        // Get the dependency lifetime from the diagnostic
        var dependencyLifetime = GetDependencyLifetimeFromDiagnostic(diagnostic);

        // Offer to change to Scoped
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI009_FixTitle_ChangeToScoped,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddScoped", c),
                equivalenceKey: ChangeToScopedEquivalenceKey),
            diagnostic);

        // If capturing transient, also offer Transient
        if (dependencyLifetime == "transient")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI009_FixTitle_ChangeToTransient,
                    createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddTransient", c),
                    equivalenceKey: ChangeToTransientEquivalenceKey),
                diagnostic);
        }
    }

    private static string? GetCurrentLifetime(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var methodName = memberAccess.Name switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            _ => null
        };

        return methodName switch
        {
            { } name when name.StartsWith("AddSingleton") => "Singleton",
            { } name when name.StartsWith("AddScoped") => "Scoped",
            { } name when name.StartsWith("AddTransient") => "Transient",
            _ => null
        };
    }

    private static string GetDependencyLifetimeFromDiagnostic(Diagnostic diagnostic)
    {
        // The diagnostic message is "Open generic singleton '{0}' captures {1} dependency '{2}'"
        var message = diagnostic.GetMessage();

        if (message.Contains("transient"))
        {
            return "transient";
        }

        if (message.Contains("scoped"))
        {
            return "scoped";
        }

        return "scoped";
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
                // AddSingleton<TService, TImpl> -> AddScoped<TService, TImpl>
                newName = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(newMethodName),
                    genericName.TypeArgumentList).WithTriviaFrom(genericName);
                break;

            case IdentifierNameSyntax identifierName:
                // AddSingleton -> AddScoped (for non-generic overloads like AddSingleton(typeof(...), typeof(...)))
                newName = SyntaxFactory.IdentifierName(newMethodName).WithTriviaFrom(identifierName);
                break;

            default:
                return invocation;
        }

        var newMemberAccess = memberAccess.WithName(newName);
        return invocation.WithExpression(newMemberAccess);
    }
}
