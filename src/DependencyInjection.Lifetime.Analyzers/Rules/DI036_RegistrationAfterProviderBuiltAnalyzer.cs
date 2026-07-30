using System;
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
    private const int MaxAliasDepth = 8;

    /// <summary>
    /// Mutating <c>IServiceCollection</c> members whose effect is lost once the provider exists.
    /// Removal verbs (<c>Clear</c>, <c>Remove</c>, <c>RemoveAll</c>) are deliberately absent:
    /// stripping a collection back after a build is the reuse idiom of test fixtures, and this
    /// rule's claim is that a registration you expected to take effect did not.
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

    /// <summary>
    /// Builder types whose <c>Build()</c> freezes the descriptor list behind their
    /// <c>Services</c> property. Restricted to the framework contracts, so an unrelated
    /// <c>Build()</c> on a type that merely exposes a service collection is never a snapshot.
    /// </summary>
    private static readonly ImmutableArray<string> HostBuilderMetadataNames = ImmutableArray.Create(
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.Extensions.Hosting.HostApplicationBuilder",
        "Microsoft.AspNetCore.Builder.WebApplicationBuilder"
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
            var knownTypes = KnownTypes.Create(compilationContext.Compilation);
            if (knownTypes is null)
            {
                return;
            }

            compilationContext.RegisterCodeBlockAction(codeBlockContext =>
                AnalyzeCodeBlock(codeBlockContext, knownTypes)
            );
        });
    }

    private static void AnalyzeCodeBlock(CodeBlockAnalysisContext context, KnownTypes knownTypes)
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
        var reassignedSymbols = GetReassignedSymbols(codeBlock, semanticModel);

        var builds = new List<CollectionEvent>();
        var registrations = new List<CollectionEvent>();
        var classified = new HashSet<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            if (
                TryClassify(
                    invocation,
                    semanticModel,
                    knownTypes,
                    reassignedSymbols,
                    out var collectionEvent,
                    out var isBuild
                )
            )
            {
                (isBuild ? builds : registrations).Add(collectionEvent);
                classified.Add(invocation);
            }
        }

        if (builds.Count == 0 || registrations.Count == 0)
        {
            return;
        }

        var handOffs = GetCollectionHandOffs(
            codeBlock,
            semanticModel,
            knownTypes,
            reassignedSymbols,
            classified
        );

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

            // The collection is handed to something else afterwards, which may build it again.
            if (
                handOffs.Any(handOff =>
                    CollectionKey.Equal(handOff.Key, registration.Key)
                    && handOff.Position > registration.Invocation.SpanStart
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
        KnownTypes knownTypes,
        ImmutableHashSet<ISymbol> reassignedSymbols,
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

        // Only the framework's own BuildServiceProvider snapshots the descriptor list; a
        // same-named community extension may hand back a live view of the collection.
        if (
            method.Name == "BuildServiceProvider"
            && SymbolEqualityComparer.Default.Equals(
                definition.ContainingType,
                knownTypes.ContainerBuilderExtensions
            )
        )
        {
            var buildReceiver = GetReceiverExpression(invocation, method);
            if (
                buildReceiver is null
                || !CollectionKey.TryCreate(
                    buildReceiver,
                    semanticModel,
                    reassignedSymbols,
                    out var buildKey
                )
            )
            {
                return false;
            }

            collectionEvent = new CollectionEvent(invocation, buildKey, method.Name);
            isBuild = true;
            return true;
        }

        // `builder.Build()` on a framework host builder freezes `builder.Services`.
        if (method.Name == "Build" && method.Parameters.Length == 0)
        {
            var hostReceiver = GetReceiverExpression(invocation, method);
            var receiverType = method.ReceiverType ?? method.ContainingType;

            if (hostReceiver is null || !IsHostBuilder(receiverType, knownTypes))
            {
                return false;
            }

            var servicesProperty = FindServicesProperty(receiverType, knownTypes);

            if (
                servicesProperty is null
                || !CollectionKey.TryCreate(
                    hostReceiver,
                    semanticModel,
                    reassignedSymbols,
                    out var hostKey
                )
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

        if (!IsRegistrationMethod(method, definition, knownTypes))
        {
            return false;
        }

        ExpressionSyntax? receiver = GetReceiverExpression(invocation, method);

        if (receiver is null)
        {
            return false;
        }

        if (definition.IsExtensionMethod)
        {
            if (
                definition.Parameters.Length == 0
                || !IsServiceCollection(definition.Parameters[0].Type, knownTypes)
            )
            {
                return false;
            }
        }
        else if (!IsServiceCollection(semanticModel.GetTypeInfo(receiver).Type, knownTypes))
        {
            // `services.Add(descriptor)` and friends are `ICollection<ServiceDescriptor>`
            // members reached through the collection itself.
            return false;
        }

        if (
            !CollectionKey.TryCreate(
                receiver,
                semanticModel,
                reassignedSymbols,
                out var registrationKey
            )
        )
        {
            return false;
        }

        collectionEvent = new CollectionEvent(invocation, registrationKey, method.Name);
        return true;
    }

    private static bool IsRegistrationMethod(
        IMethodSymbol method,
        IMethodSymbol definition,
        KnownTypes knownTypes
    )
    {
        // A call that hands back a provider builds one itself, so its own registrations reach it.
        if (IsServiceProvider(method.ReturnType, knownTypes))
        {
            return false;
        }

        if (MutationMethodNames.Contains(method.Name))
        {
            return true;
        }

        if (!HasMutationPrefix(method.Name))
        {
            return false;
        }

        // An `AddXxx` extension that answers with a scalar is a query over the collection, not a
        // registration: `services.AddCount()` reports a count, it does not register anything.
        return definition.ReturnsVoid
            || (
                !definition.ReturnType.IsValueType
                && definition.ReturnType.SpecialType != SpecialType.System_String
            );
    }

    private static bool HasMutationPrefix(string name)
    {
        foreach (var prefix in MutationMethodPrefixes)
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Positions at which a service collection is handed to another method or constructor, which
    /// may build a provider from it out of sight of this code block.
    /// </summary>
    private static List<CollectionHandOff> GetCollectionHandOffs(
        SyntaxNode codeBlock,
        SemanticModel semanticModel,
        KnownTypes knownTypes,
        ImmutableHashSet<ISymbol> reassignedSymbols,
        HashSet<InvocationExpressionSyntax> classified
    )
    {
        var handOffs = new List<CollectionHandOff>();

        foreach (var argumentList in codeBlock.DescendantNodes().OfType<ArgumentListSyntax>())
        {
            // Arguments of the calls this rule already understands are not hand-offs; the static
            // spelling of a registration extension passes the collection to itself.
            if (
                argumentList.Parent is InvocationExpressionSyntax invocation
                && classified.Contains(invocation)
            )
            {
                continue;
            }

            foreach (var argument in argumentList.Arguments)
            {
                if (
                    !IsServiceCollection(
                        semanticModel.GetTypeInfo(argument.Expression).Type,
                        knownTypes
                    )
                )
                {
                    continue;
                }

                if (
                    CollectionKey.TryCreate(
                        argument.Expression,
                        semanticModel,
                        reassignedSymbols,
                        out var key
                    )
                )
                {
                    handOffs.Add(new CollectionHandOff(key, argument.SpanStart));
                }
            }
        }

        return handOffs;
    }

    /// <summary>
    /// Symbols written anywhere in the code block. A collection reached through one of them is
    /// not stable enough to reason about: the name may denote a different collection at the
    /// build than it does at the registration.
    /// </summary>
    private static ImmutableHashSet<ISymbol> GetReassignedSymbols(
        SyntaxNode codeBlock,
        SemanticModel semanticModel
    )
    {
        var builder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var node in codeBlock.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment:
                    AddSymbol(assignment.Left);
                    break;

                case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                    AddSymbol(argument.Expression);
                    break;
            }
        }

        return builder.ToImmutable();

        void AddSymbol(ExpressionSyntax expression)
        {
            if (semanticModel.GetSymbolInfo(expression).Symbol is { } symbol)
            {
                builder.Add(symbol);
            }
        }
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

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
    }

    private static bool IsHostBuilder(ITypeSymbol? type, KnownTypes knownTypes)
    {
        if (type is null)
        {
            return false;
        }

        foreach (var hostBuilderType in knownTypes.HostBuilderTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(type, hostBuilderType))
            {
                return true;
            }

            if (
                type.AllInterfaces.Any(@interface =>
                    SymbolEqualityComparer.Default.Equals(@interface, hostBuilderType)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IPropertySymbol? FindServicesProperty(
        ITypeSymbol? receiverType,
        KnownTypes knownTypes
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
            !property.IsStatic && IsServiceCollection(property.Type, knownTypes)
        );
    }

    private static bool IsServiceCollection(ITypeSymbol? type, KnownTypes knownTypes) =>
        Implements(type, knownTypes.ServiceCollection);

    private static bool IsServiceProvider(ITypeSymbol? type, KnownTypes knownTypes) =>
        Implements(type, knownTypes.ServiceProvider);

    private static bool Implements(ITypeSymbol? type, INamedTypeSymbol? target)
    {
        if (type is null || target is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(type, target)
            || type.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface, target)
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

    private sealed class KnownTypes
    {
        private KnownTypes(
            INamedTypeSymbol serviceCollection,
            INamedTypeSymbol containerBuilderExtensions,
            INamedTypeSymbol? serviceProvider,
            ImmutableArray<INamedTypeSymbol> hostBuilderTypes
        )
        {
            ServiceCollection = serviceCollection;
            ContainerBuilderExtensions = containerBuilderExtensions;
            ServiceProvider = serviceProvider;
            HostBuilderTypes = hostBuilderTypes;
        }

        public INamedTypeSymbol ServiceCollection { get; }

        public INamedTypeSymbol ContainerBuilderExtensions { get; }

        public INamedTypeSymbol? ServiceProvider { get; }

        public ImmutableArray<INamedTypeSymbol> HostBuilderTypes { get; }

        public static KnownTypes? Create(Compilation compilation)
        {
            var serviceCollection = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            );

            var containerBuilderExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions"
            );

            if (serviceCollection is null || containerBuilderExtensions is null)
            {
                return null;
            }

            var hostBuilderTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var metadataName in HostBuilderMetadataNames)
            {
                if (compilation.GetTypeByMetadataName(metadataName) is { } hostBuilderType)
                {
                    hostBuilderTypes.Add(hostBuilderType);
                }
            }

            return new KnownTypes(
                serviceCollection,
                containerBuilderExtensions,
                compilation.GetTypeByMetadataName("System.IServiceProvider"),
                hostBuilderTypes.ToImmutable()
            );
        }
    }

    private sealed class CollectionHandOff
    {
        public CollectionHandOff(CollectionKey key, int position)
        {
            Key = key;
            Position = position;
        }

        public CollectionKey Key { get; }

        public int Position { get; }
    }

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
    /// the same collection when their paths are symbol-wise equal. A local initialised from
    /// another path is folded into that path, so an alias and its source compare equal.
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
            ImmutableHashSet<ISymbol> reassignedSymbols,
            out CollectionKey key
        )
        {
            key = null!;
            var path = ImmutableArray.CreateBuilder<ISymbol>();

            if (!TryBuild(expression, semanticModel, reassignedSymbols, path, 0))
            {
                return false;
            }

            key = new CollectionKey(path.ToImmutable());
            return true;
        }

        private static bool TryBuild(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            ImmutableHashSet<ISymbol> reassignedSymbols,
            ImmutableArray<ISymbol>.Builder path,
            int depth
        )
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryBuild(
                        parenthesized.Expression,
                        semanticModel,
                        reassignedSymbols,
                        path,
                        depth
                    );

                case ThisExpressionSyntax:
                    return true;

                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    return TryBuild(
                            memberAccess.Expression,
                            semanticModel,
                            reassignedSymbols,
                            path,
                            depth
                        )
                        && TryAppend(
                            memberAccess.Name,
                            semanticModel,
                            reassignedSymbols,
                            path,
                            depth
                        );

                case SimpleNameSyntax simpleName:
                    return TryAppend(simpleName, semanticModel, reassignedSymbols, path, depth);

                default:
                    return false;
            }
        }

        private static bool TryAppend(
            SimpleNameSyntax name,
            SemanticModel semanticModel,
            ImmutableHashSet<ISymbol> reassignedSymbols,
            ImmutableArray<ISymbol>.Builder path,
            int depth
        )
        {
            var symbol = semanticModel.GetSymbolInfo(name).Symbol;

            // Only stable storage locations identify a collection. A method call in the path
            // could return a different collection on every evaluation, and a symbol written
            // somewhere in this block can denote two different collections across the events.
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

            if (reassignedSymbols.Contains(symbol))
            {
                return false;
            }

            if (
                symbol is ILocalSymbol
                && depth < MaxAliasDepth
                && GetSingleAssignmentInitializer(symbol) is { } initializer
            )
            {
                var restorePoint = path.Count;
                if (TryBuild(initializer, semanticModel, reassignedSymbols, path, depth + 1))
                {
                    return true;
                }

                path.Count = restorePoint;
            }

            path.Add(symbol);
            return true;
        }

        /// <summary>
        /// The initializer of a local that is written exactly once, at its declaration. Callers
        /// have already excluded locals reassigned anywhere in the block.
        /// </summary>
        private static ExpressionSyntax? GetSingleAssignmentInitializer(ISymbol symbol)
        {
            if (symbol.DeclaringSyntaxReferences.Length != 1)
            {
                return null;
            }

            return
                symbol.DeclaringSyntaxReferences[0].GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: { } value }
                ? value
                : null;
        }
    }
}
