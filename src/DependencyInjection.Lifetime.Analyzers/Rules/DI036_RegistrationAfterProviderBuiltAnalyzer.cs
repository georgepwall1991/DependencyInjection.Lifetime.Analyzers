using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects a service registration executed after the provider was already built
/// from the same <c>IServiceCollection</c>. <c>BuildServiceProvider</c> snapshots the descriptor
/// list, so anything registered afterwards never reaches the container that is actually in use.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI036_RegistrationAfterProviderBuiltAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Mutating <c>IServiceCollection</c> members whose effect is lost once the provider exists.
    /// <c>Clear</c> is deliberately absent: clearing a collection after a build is the reuse
    /// idiom of test fixtures, not a lost registration.
    /// </summary>
    private static readonly ImmutableHashSet<string> MutationMethodNames = ImmutableHashSet.Create(
        "Add",
        "AddKeyedScoped",
        "AddKeyedSingleton",
        "AddKeyedTransient",
        "AddScoped",
        "AddSingleton",
        "AddTransient",
        "Configure",
        "ConfigureAll",
        "Decorate",
        "Insert",
        "PostConfigure",
        "PostConfigureAll",
        "Remove",
        "RemoveAll",
        "RemoveAllKeyed",
        "Replace",
        "TryAdd",
        "TryAddEnumerable",
        "TryAddKeyedScoped",
        "TryAddKeyedSingleton",
        "TryAddKeyedTransient",
        "TryAddScoped",
        "TryAddSingleton",
        "TryAddTransient"
    );

    /// <summary>
    /// Prefixes that make an unrecognised <c>IServiceCollection</c> extension a registration.
    /// The framework and every community package follow the <c>AddXxx</c> convention.
    /// </summary>
    private static readonly ImmutableArray<string> MutationMethodPrefixes = ImmutableArray.Create(
        "Add",
        "TryAdd"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RegistrationAfterProviderBuilt);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var serviceCollectionType = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            );

            if (serviceCollectionType is null)
            {
                return;
            }

            compilationContext.RegisterCodeBlockAction(codeBlockContext =>
                AnalyzeCodeBlock(codeBlockContext, serviceCollectionType)
            );
        });
    }

    private static void AnalyzeCodeBlock(
        CodeBlockAnalysisContext context,
        INamedTypeSymbol serviceCollectionType
    )
    {
        var codeBlock = context.CodeBlock;

        // An arbitrary jump can put a registration back before the build at run time, so the
        // source order this rule reasons about no longer describes execution order.
        if (codeBlock.DescendantNodes().OfType<GotoStatementSyntax>().Any())
        {
            return;
        }

        var invocations = codeBlock.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        if (invocations.Count == 0)
        {
            return;
        }

        var semanticModel = context.SemanticModel;

        var builds = new List<CollectionEvent>();
        var registrations = new List<CollectionEvent>();

        foreach (var invocation in invocations)
        {
            if (
                TryClassify(
                    invocation,
                    semanticModel,
                    serviceCollectionType,
                    out var collectionEvent,
                    out var isBuild
                )
            )
            {
                (isBuild ? builds : registrations).Add(collectionEvent);
            }
        }

        if (builds.Count == 0 || registrations.Count == 0)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            // A later build on the same collection picks the registration up, so nothing is lost.
            if (
                builds.Any(build =>
                    CollectionKey.Equal(build.Key, registration.Key)
                    && build.Invocation.SpanStart > registration.Invocation.SpanStart
                )
            )
            {
                continue;
            }

            var dominatingBuild = builds.FirstOrDefault(build =>
                CollectionKey.Equal(build.Key, registration.Key)
                && DefinitelyExecutesBefore(build.Invocation, registration.Invocation)
            );

            if (dominatingBuild is null)
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.RegistrationAfterProviderBuilt,
                    registration.Invocation.GetLocation(),
                    registration.MethodName
                )
            );
        }
    }

    /// <summary>
    /// Recognises an invocation as either building a provider from a service collection or
    /// mutating one, and resolves the collection it acts on.
    /// </summary>
    private static bool TryClassify(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        INamedTypeSymbol serviceCollectionType,
        out CollectionEvent collectionEvent,
        out bool isBuild
    )
    {
        collectionEvent = null!;
        isBuild = false;

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var definition = method.ReducedFrom ?? method;

        if (method.Name == "BuildServiceProvider")
        {
            if (
                !definition.IsExtensionMethod
                || definition.Parameters.Length == 0
                || !IsServiceCollection(definition.Parameters[0].Type, serviceCollectionType)
            )
            {
                return false;
            }

            var buildReceiver = GetReceiverExpression(invocation, method);
            if (
                buildReceiver is null
                || !CollectionKey.TryCreate(buildReceiver, semanticModel, out var buildKey)
            )
            {
                return false;
            }

            collectionEvent = new CollectionEvent(invocation, buildKey, method.Name);
            isBuild = true;
            return true;
        }

        // `builder.Build()` on a host or web-application builder freezes `builder.Services`.
        if (method.Name == "Build" && method.Parameters.Length == 0)
        {
            var hostReceiver = GetReceiverExpression(invocation, method);
            if (hostReceiver is null)
            {
                return false;
            }

            var servicesProperty = FindServicesProperty(
                method.ReceiverType ?? method.ContainingType,
                serviceCollectionType
            );

            if (
                servicesProperty is null
                || !CollectionKey.TryCreate(hostReceiver, semanticModel, out var hostKey)
            )
            {
                return false;
            }

            collectionEvent = new CollectionEvent(
                invocation,
                hostKey.Append(servicesProperty),
                method.Name
            );
            isBuild = true;
            return true;
        }

        if (!IsMutationName(method.Name))
        {
            return false;
        }

        ExpressionSyntax? receiver;

        if (definition.IsExtensionMethod)
        {
            if (
                definition.Parameters.Length == 0
                || !IsServiceCollection(definition.Parameters[0].Type, serviceCollectionType)
            )
            {
                return false;
            }

            receiver = GetReceiverExpression(invocation, method);
        }
        else
        {
            // `services.Add(descriptor)` and friends are `ICollection<ServiceDescriptor>` members
            // reached through the collection itself.
            receiver = GetReceiverExpression(invocation, method);
            if (
                receiver is null
                || !IsServiceCollection(
                    semanticModel.GetTypeInfo(receiver).Type,
                    serviceCollectionType
                )
            )
            {
                return false;
            }
        }

        if (
            receiver is null
            || !CollectionKey.TryCreate(receiver, semanticModel, out var registrationKey)
        )
        {
            return false;
        }

        collectionEvent = new CollectionEvent(invocation, registrationKey, method.Name);
        return true;
    }

    private static bool IsMutationName(string name)
    {
        if (MutationMethodNames.Contains(name))
        {
            return true;
        }

        foreach (var prefix in MutationMethodPrefixes)
        {
            if (
                name.Length > prefix.Length
                && name.StartsWith(prefix, System.StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The expression the invocation acts on: the member-access receiver in the usual reduced
    /// form, or the first argument when an extension is called in its static spelling.
    /// </summary>
    private static ExpressionSyntax? GetReceiverExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method
    )
    {
        if (method.IsExtensionMethod && method.ReducedFrom is null)
        {
            return invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : null;
        }

        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => null,
            _ => null,
        };
    }

    private static IPropertySymbol? FindServicesProperty(
        ITypeSymbol? receiverType,
        INamedTypeSymbol serviceCollectionType
    )
    {
        if (receiverType is null)
        {
            return null;
        }

        var candidates = receiverType
            .GetMembers("Services")
            .OfType<IPropertySymbol>()
            .Concat(
                receiverType.AllInterfaces.SelectMany(@interface =>
                    @interface.GetMembers("Services").OfType<IPropertySymbol>()
                )
            );

        return candidates.FirstOrDefault(property =>
            !property.IsStatic && IsServiceCollection(property.Type, serviceCollectionType)
        );
    }

    private static bool IsServiceCollection(
        ITypeSymbol? type,
        INamedTypeSymbol serviceCollectionType
    )
    {
        if (type is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(type, serviceCollectionType)
            || type.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface, serviceCollectionType)
            );
    }

    /// <summary>
    /// True when <paramref name="build"/> is guaranteed to have run by the time
    /// <paramref name="registration"/> runs: both sit in one statement list inside the same
    /// function, the build's own statement is a direct member of that list (so no branch can skip
    /// it), it comes first, and no loop can carry the registration back around to a later build.
    /// </summary>
    private static bool DefinitelyExecutesBefore(SyntaxNode build, SyntaxNode registration)
    {
        var buildChain = GetStatementChain(build);
        var registrationChain = GetStatementChain(registration);

        if (buildChain.Count == 0 || registrationChain.Count == 0)
        {
            return false;
        }

        var buildStatementsByOwner = new Dictionary<SyntaxNode, StatementSyntax>();
        foreach (var statement in buildChain)
        {
            var owner = GetStatementListOwner(statement);
            if (owner is not null && !buildStatementsByOwner.ContainsKey(owner))
            {
                buildStatementsByOwner[owner] = statement;
            }
        }

        foreach (var registrationStatement in registrationChain)
        {
            var owner = GetStatementListOwner(registrationStatement);
            if (owner is null || !buildStatementsByOwner.TryGetValue(owner, out var buildStatement))
            {
                continue;
            }

            // The build must be unconditional within the shared list; a build nested inside a
            // branch under that list may never have run.
            if (!ReferenceEquals(buildStatement, buildChain[0]))
            {
                return false;
            }

            if (buildStatement.SpanStart >= registrationStatement.SpanStart)
            {
                return false;
            }

            return !IsInsideLoop(owner);
        }

        return false;
    }

    /// <summary>Enclosing statements from innermost outwards, stopping at the function boundary.</summary>
    private static List<StatementSyntax> GetStatementChain(SyntaxNode node)
    {
        var chain = new List<StatementSyntax>();

        for (var current = node; current is not null; current = current.Parent)
        {
            if (IsFunctionBoundary(current))
            {
                break;
            }

            if (current is StatementSyntax statement)
            {
                chain.Add(statement);
            }
        }

        return chain;
    }

    /// <summary>
    /// The node that owns the statement list a statement belongs to. Top-level statements are
    /// wrapped one-per-<see cref="GlobalStatementSyntax"/>, so they normalise to the
    /// compilation unit that sequences them.
    /// </summary>
    private static SyntaxNode? GetStatementListOwner(StatementSyntax statement)
    {
        var parent = statement.Parent;
        return parent is GlobalStatementSyntax globalStatement ? globalStatement.Parent : parent;
    }

    private static bool IsInsideLoop(SyntaxNode listOwner)
    {
        for (var current = listOwner; current is not null; current = current.Parent)
        {
            if (IsFunctionBoundary(current))
            {
                return false;
            }

            if (
                current
                is ForStatementSyntax
                    or ForEachStatementSyntax
                    or ForEachVariableStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFunctionBoundary(SyntaxNode node) =>
        node
            is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or PropertyDeclarationSyntax
                or IndexerDeclarationSyntax
                or CompilationUnitSyntax;

    private sealed class CollectionEvent
    {
        public CollectionEvent(
            InvocationExpressionSyntax invocation,
            CollectionKey key,
            string methodName
        )
        {
            Invocation = invocation;
            Key = key;
            MethodName = methodName;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public CollectionKey Key { get; }

        public string MethodName { get; }
    }

    /// <summary>
    /// Identity of the service collection an invocation acts on, as the symbol path that reaches
    /// it (<c>services</c>, <c>builder.Services</c>, <c>this._services</c>). Two events refer to
    /// the same collection when their paths are symbol-wise equal.
    /// </summary>
    private sealed class CollectionKey
    {
        private CollectionKey(ImmutableArray<ISymbol> path)
        {
            Path = path;
        }

        private ImmutableArray<ISymbol> Path { get; }

        public CollectionKey Append(ISymbol symbol) => new(Path.Add(symbol));

        public static bool Equal(CollectionKey left, CollectionKey right)
        {
            if (left.Path.Length != right.Path.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Path.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(left.Path[index], right.Path[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryCreate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out CollectionKey key
        )
        {
            key = null!;
            var path = ImmutableArray.CreateBuilder<ISymbol>();

            if (!TryBuild(expression, semanticModel, path))
            {
                return false;
            }

            key = new CollectionKey(path.ToImmutable());
            return true;
        }

        private static bool TryBuild(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            ImmutableArray<ISymbol>.Builder path
        )
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryBuild(parenthesized.Expression, semanticModel, path);

                case ThisExpressionSyntax:
                    return true;

                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    return TryBuild(memberAccess.Expression, semanticModel, path)
                        && TryAppend(memberAccess.Name, semanticModel, path);

                case SimpleNameSyntax simpleName:
                    return TryAppend(simpleName, semanticModel, path);

                default:
                    return false;
            }
        }

        private static bool TryAppend(
            SimpleNameSyntax name,
            SemanticModel semanticModel,
            ImmutableArray<ISymbol>.Builder path
        )
        {
            var symbol = semanticModel.GetSymbolInfo(name).Symbol;

            // Only stable storage locations identify a collection. A method call in the path
            // could return a different collection on every evaluation.
            if (
                symbol
                is not ILocalSymbol
                    and not IParameterSymbol
                    and not IFieldSymbol
                    and not IPropertySymbol
            )
            {
                return false;
            }

            path.Add(symbol);
            return true;
        }
    }
}
