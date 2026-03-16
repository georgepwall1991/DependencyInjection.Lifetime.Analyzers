using System;
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
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI015: Unresolvable dependency.
/// Offers a conservative self-binding registration insertion when the shape is provably safe.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI015_UnresolvableDependencyCodeFixProvider))]
[Shared]
public sealed class DI015_UnresolvableDependencyCodeFixProvider : CodeFixProvider
{
    internal const string AddMissingRegistrationEquivalenceKey = "DI015_AddMissingRegistration";

    private readonly struct RegistrationFixTarget
    {
        public RegistrationFixTarget(
            TextSpan invocationSpan,
            string missingDependencyTypeName,
            ServiceLifetime lifetime)
        {
            InvocationSpan = invocationSpan;
            MissingDependencyTypeName = missingDependencyTypeName;
            Lifetime = lifetime;
        }

        public TextSpan InvocationSpan { get; }

        public string MissingDependencyTypeName { get; }

        public ServiceLifetime Lifetime { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.UnresolvableDependency);

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var fixTarget = await TryGetFixTargetAsync(context.Document, diagnostic, context.CancellationToken).ConfigureAwait(false);
        if (fixTarget is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Resources.DI015_FixTitle_AddMissingRegistration,
                createChangedDocument: cancellationToken => AddMissingRegistrationAsync(
                    context.Document,
                    fixTarget.Value,
                    cancellationToken),
                equivalenceKey: AddMissingRegistrationEquivalenceKey),
            diagnostic);
    }

    private static async Task<RegistrationFixTarget?> TryGetFixTargetAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        if (!TryParseDiagnosticProperties(diagnostic, out var missingDependencyTypeName, out var lifetime))
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (!IsSafeRegistrationSite(invocation, semanticModel))
        {
            return null;
        }

        return new RegistrationFixTarget(invocation!.Span, missingDependencyTypeName, lifetime);
    }

    private static bool TryParseDiagnosticProperties(
        Diagnostic diagnostic,
        out string missingDependencyTypeName,
        out ServiceLifetime lifetime)
    {
        missingDependencyTypeName = string.Empty;
        lifetime = ServiceLifetime.Scoped;

        if (!diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.MissingDependencyTypeNamePropertyName,
                out var typeName) ||
            string.IsNullOrWhiteSpace(typeName) ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.MissingDependencyCountPropertyName,
                out var missingCountText) ||
            !int.TryParse(missingCountText, out var missingCount) ||
            missingCount != 1 ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.MissingDependencyPathLengthPropertyName,
                out var pathLengthText) ||
            !int.TryParse(pathLengthText, out var pathLength) ||
            pathLength != 1 ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.MissingDependencyCanSelfBindPropertyName,
                out var canSelfBindText) ||
            !bool.TryParse(canSelfBindText, out var canSelfBind) ||
            !canSelfBind ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.MissingDependencyIsKeyedPropertyName,
                out var isKeyedText) ||
            !bool.TryParse(isKeyedText, out var isKeyed) ||
            isKeyed ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.RegistrationLifetimePropertyName,
                out var lifetimeText) ||
            !Enum.TryParse(lifetimeText, out lifetime) ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.DependencySourceKindPropertyName,
                out var sourceKindText) ||
            !Enum.TryParse(sourceKindText, out DependencySourceKind sourceKind) ||
            sourceKind != DependencySourceKind.ConstructorParameter)
        {
            return false;
        }

        missingDependencyTypeName = typeName!;
        return true;
    }

    private static bool IsSafeRegistrationSite(
        InvocationExpressionSyntax? invocation,
        SemanticModel semanticModel)
    {
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression is not IdentifierNameSyntax receiver ||
            !TryGetSupportedRegistrationLifetime(memberAccess.Name, out _) ||
            invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>()?.Parent is not BlockSyntax)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(receiver).Symbol is not (ILocalSymbol or IParameterSymbol))
        {
            return false;
        }

        return ImplementsIServiceCollection(semanticModel.GetTypeInfo(receiver).Type);
    }

    private static async Task<Document> AddMissingRegistrationAsync(
        Document document,
        RegistrationFixTarget fixTarget,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var node = root.FindNode(fixTarget.InvocationSpan, getInnermostNodeForTie: true);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression is not IdentifierNameSyntax receiver ||
            invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax registrationStatement ||
            registrationStatement.Parent is not BlockSyntax block ||
            !IsSafeRegistrationSite(invocation, semanticModel))
        {
            return document;
        }

        var insertedStatement = CreateRegistrationStatement(receiver, fixTarget);
        var statementIndex = block.Statements.IndexOf(registrationStatement);
        if (statementIndex < 0)
        {
            return document;
        }

        insertedStatement = insertedStatement.WithLeadingTrivia(registrationStatement.GetLeadingTrivia());

        var newBlock = block.WithStatements(block.Statements.Insert(statementIndex, insertedStatement));
        var newRoot = root.ReplaceNode(block, newBlock);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionStatementSyntax CreateRegistrationStatement(
        IdentifierNameSyntax receiver,
        RegistrationFixTarget fixTarget)
    {
        var methodName = fixTarget.Lifetime switch
        {
            ServiceLifetime.Singleton => "AddSingleton",
            ServiceLifetime.Transient => "AddTransient",
            _ => "AddScoped"
        };

        var typeSyntax = SyntaxFactory.ParseTypeName(fixTarget.MissingDependencyTypeName)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(receiver.Identifier),
                SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(methodName),
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(typeSyntax)))),
            SyntaxFactory.ArgumentList());

        return SyntaxFactory.ExpressionStatement(invocation)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static bool TryGetSupportedRegistrationLifetime(
        SimpleNameSyntax methodNameSyntax,
        out ServiceLifetime lifetime)
    {
        var methodName = methodNameSyntax switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            _ => string.Empty
        };

        if (methodName.StartsWith("AddSingleton", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddSingleton", StringComparison.Ordinal) ||
            methodName.StartsWith("AddKeyedSingleton", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddKeyedSingleton", StringComparison.Ordinal))
        {
            lifetime = ServiceLifetime.Singleton;
            return true;
        }

        if (methodName.StartsWith("AddScoped", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddScoped", StringComparison.Ordinal) ||
            methodName.StartsWith("AddKeyedScoped", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddKeyedScoped", StringComparison.Ordinal))
        {
            lifetime = ServiceLifetime.Scoped;
            return true;
        }

        if (methodName.StartsWith("AddTransient", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddTransient", StringComparison.Ordinal) ||
            methodName.StartsWith("AddKeyedTransient", StringComparison.Ordinal) ||
            methodName.StartsWith("TryAddKeyedTransient", StringComparison.Ordinal))
        {
            lifetime = ServiceLifetime.Transient;
            return true;
        }

        lifetime = ServiceLifetime.Scoped;
        return false;
    }

    private static bool ImplementsIServiceCollection(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol.Name == "IServiceCollection" &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            return true;
        }

        return typeSymbol.AllInterfaces.Any(interfaceType =>
            interfaceType.Name == "IServiceCollection" &&
            interfaceType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");
    }
}
