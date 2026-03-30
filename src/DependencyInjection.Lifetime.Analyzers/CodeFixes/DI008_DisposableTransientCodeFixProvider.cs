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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        // Verify this is an AddTransient or AddKeyedTransient call
        var kind = GetRegistrationKind(invocation);
        if (kind == RegistrationKind.None)
        {
            return;
        }

        // Offer to change to AddScoped / AddKeyedScoped
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI008_FixTitle_ChangeToScoped,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddScoped", "AddKeyedScoped", c),
                equivalenceKey: ChangeToScopedEquivalenceKey),
            diagnostic);

        // Offer to change to AddSingleton / AddKeyedSingleton
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI008_FixTitle_ChangeToSingleton,
                createChangedDocument: c => ChangeLifetimeAsync(context.Document, invocation, "AddSingleton", "AddKeyedSingleton", c),
                equivalenceKey: ChangeToSingletonEquivalenceKey),
            diagnostic);

        if (CanSafelyConvertToFactoryRegistration(invocation, semanticModel))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI008_FixTitle_UseFactory,
                    createChangedDocument: c => UseFactoryRegistrationAsync(context.Document, invocation, c),
                    equivalenceKey: UseFactoryEquivalenceKey),
                diagnostic);
        }
    }

    private enum RegistrationKind
    {
        None,
        NonKeyed,
        Keyed,
    }

    private static RegistrationKind GetRegistrationKind(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return RegistrationKind.None;
        }

        var methodName = memberAccess.Name switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            _ => null,
        };

        return methodName switch
        {
            "AddTransient" => RegistrationKind.NonKeyed,
            "AddKeyedTransient" => RegistrationKind.Keyed,
            _ => RegistrationKind.None,
        };
    }

    private static async Task<Document> ChangeLifetimeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string nonKeyedMethodName,
        string keyedMethodName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var kind = GetRegistrationKind(invocation);
        var targetName = kind == RegistrationKind.Keyed ? keyedMethodName : nonKeyedMethodName;
        var newInvocation = ReplaceMethodName(invocation, targetName);
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
                newName = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(newMethodName),
                    genericName.TypeArgumentList).WithTriviaFrom(genericName);
                break;

            case IdentifierNameSyntax identifierName:
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

        var newInvocation = TransformToFactoryRegistration(invocation);
        if (newInvocation is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    private static InvocationExpressionSyntax? TransformToFactoryRegistration(
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        if (memberAccess.Name is not GenericNameSyntax genericName)
        {
            return null;
        }

        var typeArgs = genericName.TypeArgumentList.Arguments;
        if (typeArgs.Count is < 1 or > 2)
        {
            return null;
        }

        var serviceTypeName = typeArgs[0].ToString();
        var implTypeName = typeArgs.Count >= 2 ? typeArgs[1].ToString() : serviceTypeName;

        var kind = GetRegistrationKind(invocation);

        // Build the method name: AddTransient<IService> or AddKeyedTransient<IService>
        var baseMethodName = kind == RegistrationKind.Keyed ? "AddKeyedTransient" : "AddTransient";
        var newGenericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier(baseMethodName),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                    SyntaxFactory.ParseTypeName(serviceTypeName))));

        var newMemberAccess = memberAccess.WithName(newGenericName);

        // Build the argument list
        SeparatedSyntaxList<ArgumentSyntax> arguments;

        if (kind == RegistrationKind.Keyed)
        {
            // For keyed: preserve the key argument, then add two-param lambda (sp, _) => new Impl()
            var keyArg = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (keyArg is null)
            {
                return null;
            }

            var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(
                        new[]
                        {
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier("sp")),
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier("_")),
                        })),
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(implTypeName))
                    .WithArgumentList(SyntaxFactory.ArgumentList()));

            arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                [
                    keyArg,
                    SyntaxFactory.Token(SyntaxKind.CommaToken),
                    SyntaxFactory.Argument(lambda)
                ]);
        }
        else
        {
            // Non-keyed: single-param lambda sp => new Impl()
            var lambda = SyntaxFactory.SimpleLambdaExpression(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("sp")),
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(implTypeName))
                    .WithArgumentList(SyntaxFactory.ArgumentList()));

            arguments = SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda));
        }

        var newArguments = SyntaxFactory.ArgumentList(arguments);

        return invocation
            .WithExpression(newMemberAccess)
            .WithArgumentList(newArguments);
    }

    private static bool CanSafelyConvertToFactoryRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Only accept generic forms the transformer actually supports:
        //   AddTransient<TService>()
        //   AddTransient<TService, TImpl>()
        //   AddKeyedTransient<TService>(key)
        //   AddKeyedTransient<TService, TImpl>(key)
        var kind = GetRegistrationKind(invocation);
        if (kind == RegistrationKind.None)
        {
            return false;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name is not GenericNameSyntax genericName)
        {
            return false;
        }

        var typeArgCount = genericName.TypeArgumentList.Arguments.Count;
        if (typeArgCount is < 1 or > 2)
        {
            return false;
        }

        // Non-keyed: must have zero arguments (no existing args)
        // Keyed: must have exactly one argument (the key)
        var expectedArgCount = kind == RegistrationKind.Keyed ? 1 : 0;
        if (invocation.ArgumentList.Arguments.Count != expectedArgCount)
        {
            return false;
        }

        var implementationTypeSyntax = genericName.TypeArgumentList.Arguments[typeArgCount - 1];
        var implementationType = semanticModel.GetTypeInfo(implementationTypeSyntax).Type as INamedTypeSymbol;
        return HasSafeParameterlessConstructor(implementationType);
    }

    private static bool HasSafeParameterlessConstructor(INamedTypeSymbol? implementationType)
    {
        if (implementationType is null ||
            implementationType.TypeKind != TypeKind.Class ||
            implementationType.IsAbstract ||
            implementationType.IsStatic)
        {
            return false;
        }

        if (!implementationType.InstanceConstructors.Any())
        {
            return implementationType.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;
        }

        return implementationType.InstanceConstructors.Any(ctor =>
            !ctor.IsStatic &&
            ctor.Parameters.Length == 0 &&
            ctor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
    }
}
