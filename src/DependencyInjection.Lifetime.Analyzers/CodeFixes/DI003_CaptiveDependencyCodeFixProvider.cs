using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using DependencyInjection.Lifetime.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

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

    private enum LifetimeKind
    {
        Singleton,
        Scoped,
        Transient
    }

    private readonly struct RegistrationFixTarget
    {
        public DocumentId DocumentId { get; }
        public TextSpan RegistrationSpan { get; }
        public LifetimeKind CurrentLifetime { get; }

        public RegistrationFixTarget(
            DocumentId documentId,
            TextSpan registrationSpan,
            LifetimeKind currentLifetime)
        {
            DocumentId = documentId;
            RegistrationSpan = registrationSpan;
            CurrentLifetime = currentLifetime;
        }
    }

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.CaptiveDependency);

    /// <inheritdoc />
    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var fixTarget = await TryGetFixTargetAsync(
            context.Document.Project.Solution,
            diagnostic,
            context.CancellationToken).ConfigureAwait(false);
        if (fixTarget is null)
        {
            return;
        }

        var dependencyLifetime = GetDependencyLifetimeFromDiagnostic(diagnostic);
        if (fixTarget.Value.CurrentLifetime == LifetimeKind.Singleton)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI003_FixTitle_ChangeToScoped,
                    createChangedSolution: c => ChangeLifetimeAsync(
                        context.Document.Project.Solution,
                        fixTarget.Value,
                        LifetimeKind.Scoped,
                        c),
                    equivalenceKey: ChangeToScopedEquivalenceKey),
                diagnostic);

            if (dependencyLifetime == "transient")
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Resources.DI003_FixTitle_ChangeToTransient,
                        createChangedSolution: c => ChangeLifetimeAsync(
                            context.Document.Project.Solution,
                            fixTarget.Value,
                            LifetimeKind.Transient,
                            c),
                        equivalenceKey: ChangeToTransientEquivalenceKey),
                    diagnostic);
            }
        }
    }

    private static async Task<RegistrationFixTarget?> TryGetFixTargetAsync(
        Solution solution,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var registrationLocation = diagnostic.Location;
        if (registrationLocation.SourceTree is null)
        {
            return null;
        }

        var document = solution.GetDocument(registrationLocation.SourceTree);
        if (document is null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return null;
        }

        var node = root.FindNode(registrationLocation.SourceSpan, getInnermostNodeForTie: true);
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is not InvocationExpressionSyntax invocation ||
                !TryGetCurrentLifetime(invocation, semanticModel, out var currentLifetime))
            {
                continue;
            }

            return new RegistrationFixTarget(document.Id, invocation.Span, currentLifetime);
        }

        return null;
    }

    private static string GetDependencyLifetimeFromDiagnostic(Diagnostic diagnostic)
    {
        if (diagnostic.Properties.TryGetValue(DI003_CaptiveDependencyAnalyzer.DependencyLifetimePropertyName, out var dependencyLifetime) &&
            dependencyLifetime is not null)
        {
            return dependencyLifetime;
        }

        return "scoped";
    }

    private static async Task<Solution> ChangeLifetimeAsync(
        Solution solution,
        RegistrationFixTarget fixTarget,
        LifetimeKind newLifetime,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(fixTarget.DocumentId);
        if (document is null)
        {
            return solution;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return solution;
        }

        var node = root.FindNode(fixTarget.RegistrationSpan, getInnermostNodeForTie: true);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return solution;
        }

        if (!TryRewriteRegistrationInvocation(invocation, semanticModel, newLifetime, out var rewrittenInvocation))
        {
            return solution;
        }

        var newRoot = root.ReplaceNode(invocation, rewrittenInvocation);
        return document.WithSyntaxRoot(newRoot).Project.Solution;
    }

    private static bool TryRewriteRegistrationInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out InvocationExpressionSyntax rewrittenInvocation)
    {
        if (TryRewriteServiceCollectionRegistrationInvocation(invocation, newLifetime, out rewrittenInvocation))
        {
            return true;
        }

        if (TryRewriteServiceDescriptorRegistrationInvocation(invocation, semanticModel, newLifetime, out rewrittenInvocation))
        {
            return true;
        }

        rewrittenInvocation = invocation;
        return false;
    }

    private static bool TryRewriteServiceCollectionRegistrationInvocation(
        InvocationExpressionSyntax invocation,
        LifetimeKind newLifetime,
        out InvocationExpressionSyntax rewrittenInvocation)
    {
        rewrittenInvocation = invocation;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TryGetLifetimeToken(memberAccess.Name, out _))
        {
            return false;
        }

        var newMethodName = ReplaceLifetimeToken(GetSimpleName(memberAccess.Name), newLifetime);
        rewrittenInvocation = invocation.WithExpression(memberAccess.WithName(ReplaceSimpleName(memberAccess.Name, newMethodName)));
        return true;
    }

    private static bool TryRewriteServiceDescriptorRegistrationInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out InvocationExpressionSyntax rewrittenInvocation)
    {
        rewrittenInvocation = invocation;

        if (!IsAddStyleRegistrationInvocation(invocation.Expression))
        {
            return false;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            switch (argument.Expression)
            {
                case ObjectCreationExpressionSyntax objectCreation:
                    if (!TryRewriteObjectCreationLifetime(objectCreation, semanticModel, newLifetime, out var rewrittenCreation))
                    {
                        continue;
                    }

                    rewrittenInvocation = invocation.ReplaceNode(objectCreation, rewrittenCreation);
                    return true;

                case InvocationExpressionSyntax descriptorInvocation:
                    if (TryRewriteServiceDescriptorFactoryInvocation(descriptorInvocation, semanticModel, newLifetime, out var rewrittenDescriptorInvocation))
                    {
                        rewrittenInvocation = invocation.ReplaceNode(descriptorInvocation, rewrittenDescriptorInvocation);
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool TryRewriteObjectCreationLifetime(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out ObjectCreationExpressionSyntax rewrittenObjectCreation)
    {
        rewrittenObjectCreation = objectCreation;
        if (!IsServiceDescriptorType(semanticModel.GetTypeInfo(objectCreation).Type) ||
            !TryRewriteLifetimeArgument(objectCreation, semanticModel, newLifetime, out var rewrittenArgumentList))
        {
            return false;
        }

        rewrittenObjectCreation = objectCreation.WithArgumentList(rewrittenArgumentList);
        return true;
    }

    private static bool TryRewriteServiceDescriptorFactoryInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out InvocationExpressionSyntax rewrittenInvocation)
    {
        rewrittenInvocation = invocation;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.ContainingType is null ||
            !IsServiceDescriptorType(methodSymbol.ContainingType))
        {
            return false;
        }

        if (methodSymbol.Name == "Describe")
        {
            if (!TryRewriteLifetimeArgument(invocation, semanticModel, newLifetime, out var rewrittenArgumentList))
            {
                return false;
            }

            rewrittenInvocation = invocation.WithArgumentList(rewrittenArgumentList);
            return true;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TryGetLifetimeToken(memberAccess.Name, out _))
        {
            return false;
        }

        var newMethodName = ReplaceLifetimeToken(GetSimpleName(memberAccess.Name), newLifetime);
        rewrittenInvocation = invocation.WithExpression(memberAccess.WithName(ReplaceSimpleName(memberAccess.Name, newMethodName)));
        return true;
    }

    private static bool TryRewriteLifetimeArgument(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out ArgumentListSyntax rewrittenArgumentList)
    {
        rewrittenArgumentList = objectCreation.ArgumentList ?? SyntaxFactory.ArgumentList();
        if (semanticModel.GetOperation(objectCreation) is not IObjectCreationOperation objectCreationOperation)
        {
            return false;
        }

        return TryRewriteLifetimeArgumentCore(
            rewrittenArgumentList,
            objectCreationOperation.Arguments,
            newLifetime,
            out rewrittenArgumentList);
    }

    private static bool TryRewriteLifetimeArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LifetimeKind newLifetime,
        out ArgumentListSyntax rewrittenArgumentList)
    {
        rewrittenArgumentList = invocation.ArgumentList;
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation invocationOperation)
        {
            return false;
        }

        return TryRewriteLifetimeArgumentCore(
            rewrittenArgumentList,
            invocationOperation.Arguments,
            newLifetime,
            out rewrittenArgumentList);
    }

    private static bool TryRewriteLifetimeArgumentCore(
        ArgumentListSyntax argumentList,
        ImmutableArray<IArgumentOperation> arguments,
        LifetimeKind newLifetime,
        out ArgumentListSyntax rewrittenArgumentList)
    {
        rewrittenArgumentList = argumentList;

        var lifetimeArgument = arguments.FirstOrDefault(argument => argument.Parameter?.Name == "lifetime");
        if (lifetimeArgument?.Syntax is not ArgumentSyntax argumentSyntax)
        {
            return false;
        }

        var replacementExpression = CreateServiceLifetimeExpression(newLifetime)
            .WithTriviaFrom(argumentSyntax.Expression);
        var rewrittenArgument = argumentSyntax.WithExpression(replacementExpression);
        rewrittenArgumentList = argumentList.ReplaceNode(argumentSyntax, rewrittenArgument);
        return true;
    }

    private static ExpressionSyntax CreateServiceLifetimeExpression(LifetimeKind newLifetime)
    {
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(nameof(ServiceLifetime)),
            SyntaxFactory.IdentifierName(newLifetime.ToString()));
    }

    private static bool TryGetCurrentLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out LifetimeKind lifetime)
    {
        lifetime = default;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            TryGetLifetimeToken(memberAccess.Name, out lifetime))
        {
            return true;
        }

        if (!IsAddStyleRegistrationInvocation(invocation.Expression))
        {
            return false;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            switch (argument.Expression)
            {
                case ObjectCreationExpressionSyntax objectCreation:
                    if (TryGetObjectCreationLifetime(objectCreation, semanticModel, out lifetime))
                    {
                        return true;
                    }

                    break;

                case InvocationExpressionSyntax descriptorInvocation:
                    if (TryGetServiceDescriptorInvocationLifetime(descriptorInvocation, semanticModel, out lifetime))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool TryGetObjectCreationLifetime(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        out LifetimeKind lifetime)
    {
        lifetime = default;

        if (!IsServiceDescriptorType(semanticModel.GetTypeInfo(objectCreation).Type) ||
            semanticModel.GetOperation(objectCreation) is not IObjectCreationOperation objectCreationOperation)
        {
            return false;
        }

        return TryGetLifetimeFromArguments(objectCreationOperation.Arguments, out lifetime);
    }

    private static bool TryGetServiceDescriptorInvocationLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out LifetimeKind lifetime)
    {
        lifetime = default;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.ContainingType is null ||
            !IsServiceDescriptorType(methodSymbol.ContainingType))
        {
            return false;
        }

        if (methodSymbol.Name == "Describe")
        {
            return semanticModel.GetOperation(invocation) is IInvocationOperation invocationOperation &&
                   TryGetLifetimeFromArguments(invocationOperation.Arguments, out lifetime);
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               TryGetLifetimeToken(memberAccess.Name, out lifetime);
    }

    private static bool TryGetLifetimeFromArguments(
        ImmutableArray<IArgumentOperation> arguments,
        out LifetimeKind lifetime)
    {
        lifetime = default;

        var lifetimeArgument = arguments.FirstOrDefault(argument => argument.Parameter?.Name == "lifetime");
        if (lifetimeArgument?.Value.ConstantValue.HasValue != true ||
            lifetimeArgument.Value.ConstantValue.Value is not int lifetimeValue)
        {
            return false;
        }

        return lifetimeValue switch
        {
            (int)ServiceLifetime.Singleton => SetLifetime(out lifetime, LifetimeKind.Singleton),
            (int)ServiceLifetime.Scoped => SetLifetime(out lifetime, LifetimeKind.Scoped),
            (int)ServiceLifetime.Transient => SetLifetime(out lifetime, LifetimeKind.Transient),
            _ => false
        };
    }

    private static bool SetLifetime(out LifetimeKind lifetime, LifetimeKind value)
    {
        lifetime = value;
        return true;
    }

    private static bool TryGetLifetimeToken(SimpleNameSyntax nameSyntax, out LifetimeKind lifetime)
    {
        var methodName = GetSimpleName(nameSyntax);

        if (methodName.Contains("Singleton"))
        {
            lifetime = LifetimeKind.Singleton;
            return true;
        }

        if (methodName.Contains("Scoped"))
        {
            lifetime = LifetimeKind.Scoped;
            return true;
        }

        if (methodName.Contains("Transient"))
        {
            lifetime = LifetimeKind.Transient;
            return true;
        }

        lifetime = default;
        return false;
    }

    private static string ReplaceLifetimeToken(string methodName, LifetimeKind newLifetime)
    {
        if (methodName.Contains("Singleton"))
        {
            return methodName.Replace("Singleton", newLifetime.ToString());
        }

        if (methodName.Contains("Scoped"))
        {
            return methodName.Replace("Scoped", newLifetime.ToString());
        }

        if (methodName.Contains("Transient"))
        {
            return methodName.Replace("Transient", newLifetime.ToString());
        }

        return methodName;
    }

    private static string GetSimpleName(SimpleNameSyntax nameSyntax) =>
        nameSyntax switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            _ => nameSyntax.Identifier.Text
        };

    private static SimpleNameSyntax ReplaceSimpleName(SimpleNameSyntax originalName, string newMethodName)
    {
        return originalName switch
        {
            GenericNameSyntax genericName => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(newMethodName),
                    genericName.TypeArgumentList)
                .WithTriviaFrom(genericName),
            IdentifierNameSyntax identifierName => SyntaxFactory.IdentifierName(newMethodName)
                .WithTriviaFrom(identifierName),
            _ => originalName
        };
    }

    private static bool IsAddStyleRegistrationInvocation(ExpressionSyntax expression)
    {
        return expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text is "Add" or "TryAdd";
    }

    private static bool IsServiceDescriptorType(ITypeSymbol? type)
    {
        return type?.Name == "ServiceDescriptor" &&
               (type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }
}
