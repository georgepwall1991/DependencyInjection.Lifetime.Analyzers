using System.Linq;
using System.Collections.Immutable;
using System.Composition;
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

    private enum RegistrationKind
    {
        None,
        AddSingleton,
        TryAddSingleton,
        AddKeyedSingleton,
        TryAddKeyedSingleton,
        ServiceDescriptorSingleton,
    }

    private enum LifetimeKind
    {
        Singleton,
        Scoped,
        Transient,
    }

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

        // Find the registration invocation (e.g., AddSingleton / TryAddSingleton /
        // AddKeyedSingleton / Add/TryAdd(ServiceDescriptor.Singleton(...))).
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        if (GetRegistrationKind(invocation, semanticModel) == RegistrationKind.None)
        {
            return;
        }

        // Get the dependency lifetime from the diagnostic
        var dependencyLifetime = GetDependencyLifetimeFromDiagnostic(diagnostic);

        // Offer to change to Scoped
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI009_FixTitle_ChangeToScoped,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, LifetimeKind.Scoped, c),
                equivalenceKey: ChangeToScopedEquivalenceKey),
            diagnostic);

        // If capturing transient, also offer Transient
        if (dependencyLifetime == LifetimeKind.Transient)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI009_FixTitle_ChangeToTransient,
                    createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, LifetimeKind.Transient, c),
                    equivalenceKey: ChangeToTransientEquivalenceKey),
                diagnostic);
        }
    }

    private static RegistrationKind GetRegistrationKind(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (!TryGetMethodName(invocation.Expression, out var methodName))
        {
            return RegistrationKind.None;
        }

        return methodName switch
        {
            "AddSingleton" => RegistrationKind.AddSingleton,
            "TryAddSingleton" => RegistrationKind.TryAddSingleton,
            "AddKeyedSingleton" => RegistrationKind.AddKeyedSingleton,
            "TryAddKeyedSingleton" => RegistrationKind.TryAddKeyedSingleton,
            "Add" or "TryAdd" when TryGetCurrentLifetime(invocation, semanticModel, out var lifetime) &&
                                 lifetime == LifetimeKind.Singleton => RegistrationKind.ServiceDescriptorSingleton,
            _ => RegistrationKind.None,
        };
    }

    private static bool TryGetMethodName(ExpressionSyntax expression, out string? methodName)
    {
        methodName = null;

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            expression = memberAccess.Name;
        }

        methodName = expression switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            SimpleNameSyntax simpleName => simpleName.Identifier.Text,
            _ => null,
        };

        return methodName is not null;
    }

    private static LifetimeKind GetDependencyLifetimeFromDiagnostic(Diagnostic diagnostic)
    {
        if (diagnostic.Properties.TryGetValue(DI009_OpenGenericLifetimeMismatchAnalyzer.DependencyLifetimePropertyName, out var dependencyLifetime) &&
            dependencyLifetime == "transient")
        {
            return LifetimeKind.Transient;
        }

        return LifetimeKind.Scoped;
    }

    private static async Task<Document> ChangeLifetimeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        LifetimeKind targetLifetime,
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

        if (!TryCreateReplacement(invocation, semanticModel, targetLifetime, out var targetInvocation, out var newInvocation))
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(targetInvocation, newInvocation));
    }

    private static bool TryCreateReplacement(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LifetimeKind targetLifetime,
        out InvocationExpressionSyntax targetInvocation,
        out InvocationExpressionSyntax newInvocation)
    {
        targetInvocation = invocation;
        newInvocation = invocation;

        switch (GetRegistrationKind(invocation, semanticModel))
        {
            case RegistrationKind.AddSingleton:
                newInvocation = ReplaceMethodName(invocation, $"Add{targetLifetime}");
                return true;

            case RegistrationKind.TryAddSingleton:
                newInvocation = ReplaceMethodName(invocation, $"TryAdd{targetLifetime}");
                return true;

            case RegistrationKind.AddKeyedSingleton:
                newInvocation = ReplaceMethodName(invocation, $"AddKeyed{targetLifetime}");
                return true;

            case RegistrationKind.TryAddKeyedSingleton:
                newInvocation = ReplaceMethodName(invocation, $"TryAddKeyed{targetLifetime}");
                return true;

            case RegistrationKind.ServiceDescriptorSingleton:
                return TryRewriteServiceDescriptorRegistrationInvocation(
                    invocation,
                    semanticModel,
                    targetLifetime,
                    out newInvocation);

            default:
                return false;
        }
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

        if (!TryGetLifetimeToken(methodSymbol.Name, out _))
        {
            return false;
        }

        var newMethodName = ReplaceLifetimeToken(methodSymbol.Name, newLifetime);
        rewrittenInvocation = invocation.WithExpression(ReplaceExpressionName(invocation.Expression, newMethodName));
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

        var replacementExpression = CreateServiceLifetimeExpression(newLifetime, argumentSyntax.Expression)
            .WithTriviaFrom(argumentSyntax.Expression);
        var rewrittenArgument = argumentSyntax.WithExpression(replacementExpression);
        rewrittenArgumentList = argumentList.ReplaceNode(argumentSyntax, rewrittenArgument);
        return true;
    }

    private static ExpressionSyntax CreateServiceLifetimeExpression(
        LifetimeKind newLifetime,
        ExpressionSyntax originalExpression)
    {
        if (originalExpression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.WithName(
                SyntaxFactory.IdentifierName(newLifetime.ToString())
                    .WithTriviaFrom(memberAccess.Name));
        }

        if (originalExpression is SimpleNameSyntax simpleName)
        {
            return SyntaxFactory.IdentifierName(newLifetime.ToString())
                .WithTriviaFrom(simpleName);
        }

        return SyntaxFactory.ParseExpression(
            $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{newLifetime}")
            .WithTriviaFrom(originalExpression);
    }

    private static bool TryGetCurrentLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out LifetimeKind lifetime)
    {
        lifetime = default;

        if (TryGetMethodName(invocation.Expression, out var methodName) &&
            methodName is not null &&
            TryGetLifetimeToken(methodName, out lifetime))
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

        return TryGetLifetimeToken(methodSymbol.Name, out lifetime);
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
            _ => false,
        };
    }

    private static bool SetLifetime(out LifetimeKind lifetime, LifetimeKind value)
    {
        lifetime = value;
        return true;
    }

    private static bool TryGetLifetimeToken(SimpleNameSyntax nameSyntax, out LifetimeKind lifetime) =>
        TryGetLifetimeToken(GetSimpleName(nameSyntax), out lifetime);

    private static bool TryGetLifetimeToken(string methodName, out LifetimeKind lifetime)
    {
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
            _ => nameSyntax.Identifier.Text,
        };

    private static ExpressionSyntax ReplaceExpressionName(ExpressionSyntax expression, string newMethodName)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(ReplaceSimpleName(memberAccess.Name, newMethodName)),
            SimpleNameSyntax simpleName => ReplaceSimpleName(simpleName, newMethodName),
            _ => expression,
        };
    }

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
            _ => originalName,
        };
    }

    private static bool IsAddStyleRegistrationInvocation(ExpressionSyntax expression)
    {
        return TryGetMethodName(expression, out var methodName) &&
               methodName is "Add" or "TryAdd";
    }

    private static bool IsServiceDescriptorType(ITypeSymbol? type)
    {
        return type?.Name == "ServiceDescriptor" &&
               (type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private static InvocationExpressionSyntax ReplaceMethodName(InvocationExpressionSyntax invocation, string newMethodName)
    {
        if (invocation.Expression is SimpleNameSyntax simpleName)
        {
            return invocation.WithExpression(ReplaceSimpleName(simpleName, newMethodName));
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return invocation;
        }

        var newMemberAccess = memberAccess.WithName(ReplaceSimpleName(memberAccess.Name, newMethodName));
        return invocation.WithExpression(newMemberAccess);
    }
}
