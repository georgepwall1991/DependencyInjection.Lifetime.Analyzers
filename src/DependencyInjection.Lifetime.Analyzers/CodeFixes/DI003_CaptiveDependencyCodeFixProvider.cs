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
/// Code fix provider for DI003: Captive dependency.
/// Offers to change the consumer's lifetime to match or exceed the dependency's lifetime.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI003_CaptiveDependencyCodeFixProvider))]
[Shared]
public sealed class DI003_CaptiveDependencyCodeFixProvider : CodeFixProvider
{
    private const string ChangeToScopedEquivalenceKey = "DI003_ChangeToScoped";
    private const string ChangeToTransientEquivalenceKey = "DI003_ChangeToTransient";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.CaptiveDependency);

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

        // Find the invocation (e.g., services.AddSingleton<IService, Implementation>())
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // Determine the current lifetime from the method name
        var currentLifetime = GetCurrentLifetime(invocation);
        if (currentLifetime is null)
        {
            return;
        }

        // Get the dependency lifetime from the diagnostic arguments
        // The diagnostic message format is: "{0} (singleton) captures {1} dependency '{2}'"
        // We need to determine what lifetimes to offer based on the captured dependency
        var dependencyLifetime = GetDependencyLifetimeFromDiagnostic(diagnostic);

        // Offer fixes based on the captive scenario
        if (currentLifetime == "Singleton")
        {
            // Singleton capturing something - offer to change to Scoped
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI003_FixTitle_ChangeToScoped,
                    createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddScoped", c),
                    equivalenceKey: ChangeToScopedEquivalenceKey),
                diagnostic);

            // If capturing transient, also offer Transient
            if (dependencyLifetime == "transient")
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Resources.DI003_FixTitle_ChangeToTransient,
                        createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddTransient", c),
                        equivalenceKey: ChangeToTransientEquivalenceKey),
                    diagnostic);
            }
        }
        else if (currentLifetime == "Scoped")
        {
            // Scoped capturing transient - offer to change to Transient
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI003_FixTitle_ChangeToTransient,
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
        // The diagnostic message is "Singleton '{0}' captures {1} dependency '{2}'"
        // The message text contains the lifetime value directly
        // We need to parse it from the formatted message
        var message = diagnostic.GetMessage();

        // Check for "transient" in the message
        if (message.Contains("transient"))
        {
            return "transient";
        }

        // Check for "scoped" in the message
        if (message.Contains("scoped"))
        {
            return "scoped";
        }

        // Default to scoped (safer option)
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
                // AddSingleton -> AddScoped
                newName = SyntaxFactory.IdentifierName(newMethodName).WithTriviaFrom(identifierName);
                break;

            default:
                return invocation;
        }

        var newMemberAccess = memberAccess.WithName(newName);
        return invocation.WithExpression(newMemberAccess);
    }
}
