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
            ServiceLifetime lifetime,
            bool isKeyed,
            string? keyLiteral)
        {
            InvocationSpan = invocationSpan;
            MissingDependencyTypeName = missingDependencyTypeName;
            Lifetime = lifetime;
            IsKeyed = isKeyed;
            KeyLiteral = keyLiteral;
        }

        public TextSpan InvocationSpan { get; }

        public string MissingDependencyTypeName { get; }

        public ServiceLifetime Lifetime { get; }

        public bool IsKeyed { get; }

        public string? KeyLiteral { get; }
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
        if (!TryParseDiagnosticProperties(
                diagnostic,
                out var missingDependencyTypeName,
                out var lifetime,
                out var isKeyed,
                out var keyLiteral,
                out var sourceKind))
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
        var invocation = FindRegistrationInvocation(node, sourceKind, semanticModel);
        if (!IsSafeRegistrationSite(invocation, semanticModel) ||
            (isKeyed && string.IsNullOrWhiteSpace(keyLiteral)))
        {
            return null;
        }

        return new RegistrationFixTarget(invocation!.Span, missingDependencyTypeName, lifetime, isKeyed, keyLiteral);
    }

    private static bool TryParseDiagnosticProperties(
        Diagnostic diagnostic,
        out string missingDependencyTypeName,
        out ServiceLifetime lifetime,
        out bool isKeyed,
        out string? keyLiteral,
        out DependencySourceKind sourceKind)
    {
        missingDependencyTypeName = string.Empty;
        lifetime = ServiceLifetime.Scoped;
        isKeyed = false;
        keyLiteral = null;
        sourceKind = DependencySourceKind.ConstructorParameter;

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
            !bool.TryParse(isKeyedText, out isKeyed) ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.RegistrationLifetimePropertyName,
                out var lifetimeText) ||
            !Enum.TryParse(lifetimeText, out lifetime) ||
            !diagnostic.Properties.TryGetValue(
                DI015_UnresolvableDependencyAnalyzer.DependencySourceKindPropertyName,
                out var sourceKindText) ||
            !Enum.TryParse(sourceKindText, out sourceKind) ||
            sourceKind is not (
                DependencySourceKind.ConstructorParameter or
                DependencySourceKind.RequiredServiceCall))
        {
            return false;
        }

        diagnostic.Properties.TryGetValue(
            DI015_UnresolvableDependencyAnalyzer.MissingDependencyKeyLiteralPropertyName,
            out keyLiteral);
        missingDependencyTypeName = typeName!;
        return true;
    }

    private static InvocationExpressionSyntax? FindRegistrationInvocation(
        SyntaxNode node,
        DependencySourceKind sourceKind,
        SemanticModel semanticModel)
    {
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (sourceKind == DependencySourceKind.ConstructorParameter)
        {
            return invocation;
        }

        return invocation?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(candidate => IsSafeRegistrationSite(candidate, semanticModel));
    }

    private readonly struct RegistrationCallShape
    {
        public RegistrationCallShape(
            IdentifierNameSyntax receiver,
            SimpleNameSyntax methodName,
            bool isConditionalAccess)
        {
            Receiver = receiver;
            MethodName = methodName;
            IsConditionalAccess = isConditionalAccess;
        }

        public IdentifierNameSyntax Receiver { get; }

        public SimpleNameSyntax MethodName { get; }

        public bool IsConditionalAccess { get; }
    }

    private static bool TryGetRegistrationCallShape(
        InvocationExpressionSyntax? invocation,
        out RegistrationCallShape shape)
    {
        shape = default;

        if (invocation is null)
        {
            return false;
        }

        // Direct: services.AddSingleton<...>(...)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax directReceiver)
        {
            shape = new RegistrationCallShape(directReceiver, memberAccess.Name, isConditionalAccess: false);
            return true;
        }

        // Conditional access: services?.AddSingleton<...>(...). The invocation's
        // Expression is a MemberBindingExpressionSyntax and the receiver is the
        // enclosing ConditionalAccessExpressionSyntax's Expression.
        if (invocation.Expression is MemberBindingExpressionSyntax memberBinding &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull == invocation &&
            conditionalAccess.Expression is IdentifierNameSyntax conditionalReceiver)
        {
            shape = new RegistrationCallShape(conditionalReceiver, memberBinding.Name, isConditionalAccess: true);
            return true;
        }

        return false;
    }

    private static bool IsSafeRegistrationSite(
        InvocationExpressionSyntax? invocation,
        SemanticModel semanticModel)
    {
        if (!TryGetRegistrationCallShape(invocation, out var shape) ||
            !TryGetSupportedRegistrationLifetime(shape.MethodName, out _))
        {
            return false;
        }

        // For direct calls the registration statement is the invocation's enclosing
        // expression statement; for conditional-access calls it's the conditional
        // access's enclosing expression statement.
        var registrationStatement = invocation!.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (registrationStatement?.Parent is not BlockSyntax)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(shape.Receiver).Symbol is not (ILocalSymbol or IParameterSymbol))
        {
            return false;
        }

        return ImplementsIServiceCollection(semanticModel.GetTypeInfo(shape.Receiver).Type);
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
        if (!TryGetRegistrationCallShape(invocation, out var shape) ||
            invocation!.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax registrationStatement ||
            registrationStatement.Parent is not BlockSyntax block ||
            !IsSafeRegistrationSite(invocation, semanticModel))
        {
            return document;
        }

        var insertedStatement = CreateRegistrationStatement(shape.Receiver, fixTarget, shape.IsConditionalAccess);
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
        RegistrationFixTarget fixTarget,
        bool conditionalAccess)
    {
        var methodName = fixTarget.Lifetime switch
        {
            ServiceLifetime.Singleton => fixTarget.IsKeyed ? "AddKeyedSingleton" : "AddSingleton",
            ServiceLifetime.Transient => fixTarget.IsKeyed ? "AddKeyedTransient" : "AddTransient",
            _ => fixTarget.IsKeyed ? "AddKeyedScoped" : "AddScoped"
        };

        var typeSyntax = SyntaxFactory.ParseTypeName(fixTarget.MissingDependencyTypeName)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier(methodName),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList(typeSyntax)));

        var argumentList = fixTarget.IsKeyed
            ? SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.ParseExpression(fixTarget.KeyLiteral!))))
            : SyntaxFactory.ArgumentList();

        ExpressionSyntax registrationExpression;
        if (conditionalAccess)
        {
            // Mirror the trigger registration's `services?.AddXxx(...)` shape so the
            // inserted statement stays null-safe under the same receiver guarantees.
            var innerInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberBindingExpression(genericName),
                argumentList);
            registrationExpression = SyntaxFactory.ConditionalAccessExpression(
                SyntaxFactory.IdentifierName(receiver.Identifier),
                innerInvocation);
        }
        else
        {
            registrationExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(receiver.Identifier),
                    genericName),
                argumentList);
        }

        return SyntaxFactory.ExpressionStatement(registrationExpression)
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
