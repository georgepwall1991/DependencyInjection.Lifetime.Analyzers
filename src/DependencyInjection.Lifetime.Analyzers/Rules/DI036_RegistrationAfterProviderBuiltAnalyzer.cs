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
        "Microsoft.Extensions.Hosting.HostApplicationBuilder",
        "Microsoft.AspNetCore.Builder.WebApplicationBuilder"
    );

    /// <summary>
    /// Namespaces whose <c>AddXxx</c> extensions follow the registration convention closely
    /// enough that a builder-shaped return value is still a registration. A third-party
    /// extension returning something other than the collection may be building a provider of
    /// its own and handing it back inside a wrapper, which this rule cannot see into.
    /// </summary>
    private static readonly ImmutableArray<string> ConventionalExtensionNamespacePrefixes =
        ImmutableArray.Create("Microsoft.Extensions.", "Microsoft.AspNetCore.");

    /// <summary>Ways a service collection is turned into a container.</summary>
    private static readonly ImmutableHashSet<string> ProviderBuildingMethodNames =
        ImmutableHashSet.Create("BuildServiceProvider", "CreateServiceProvider", "CreateBuilder");

    /// <summary>
    /// Namespaces whose collection members are the framework's own. A user type implementing
    /// <c>IServiceCollection</c> may declare an <c>Add</c> that forwards the descriptor straight
    /// into a live container, which this rule cannot see.
    /// </summary>
    private static readonly ImmutableArray<string> FrameworkMemberNamespacePrefixes =
        ImmutableArray.Create("System.", "Microsoft.Extensions.", "Microsoft.AspNetCore.");

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
        var classifiedReceivers = new HashSet<SyntaxNode>();

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
                classifiedReceivers.Add(collectionEvent.Receiver);
            }
        }

        if (builds.Count == 0 || registrations.Count == 0)
        {
            return;
        }

        var otherReads = GetOtherCollectionReads(
            codeBlock,
            semanticModel,
            knownTypes,
            reassignedSymbols,
            classifiedReceivers
        );

        // A registration answers with its collection. Where that answer goes somewhere this rule
        // does not follow — a caller, a local, an argument — the collection outlives the block
        // and can be built again, so nothing registered on it is provably lost.
        otherReads.AddRange(
            registrations
                .Where(registration =>
                    registration.Invocation.Parent is not ExpressionStatementSyntax
                    && !classifiedReceivers.Contains(registration.Invocation)
                )
                .Select(registration => new CollectionRead(
                    registration.Key,
                    registration.Invocation.SpanStart
                ))
        );

        foreach (var registration in registrations)
        {
            // A later build on the same collection picks the registration up, so nothing is
            // lost. A build inside a lambda or local function has no fixed position at all — the
            // delegate can be invoked after the registration wherever it was declared.
            if (
                builds.Any(build =>
                    CollectionKey.Equal(build.Key, registration.Key)
                    && (
                        EvaluatesAfter(build.Invocation, registration.Invocation)
                        || IsInsideNestedFunction(build.Invocation, codeBlock)
                    )
                )
            )
            {
                continue;
            }

            // Anything that touches the collection other than a call this rule understands can
            // carry it somewhere that builds it: an argument, a return, a tuple, a captured
            // lambda, a yield, a field it is saved into. Position does not matter — a reference
            // handed out before the build is still live when the registration runs.
            if (otherReads.Any(read => CollectionKey.Equal(read.Key, registration.Key)))
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
    /// True when the later invocation is evaluated after the earlier one. A statement further
    /// down runs later, and so does an invocation the earlier one is chained into: a receiver is
    /// evaluated before the call it carries.
    /// </summary>
    private static bool EvaluatesAfter(
        InvocationExpressionSyntax later,
        InvocationExpressionSyntax earlier
    ) =>
        later.Span.Contains(earlier.Span) || later.SpanStart > earlier.SpanStart;

    /// <summary>
    /// True when the node sits inside a lambda, a local function, or a query clause declared
    /// within the code block, so its execution order relative to the surrounding statements is
    /// not fixed: the delegate or the query can run whenever its consumer chooses.
    /// </summary>
    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode codeBlock)
    {
        for (var current = node; current is not null && current != codeBlock; current = current.Parent)
        {
            if (
                current
                is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or QueryExpressionSyntax
            )
            {
                return true;
            }
        }

        return false;
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
                    knownTypes,
                    reassignedSymbols,
                    out var buildKey
                )
            )
            {
                return false;
            }

            collectionEvent = new CollectionEvent(invocation, buildReceiver, buildKey, method.Name);
            isBuild = true;
            return true;
        }

        // `builder.Build()` on a framework host builder freezes `builder.Services`.
        if (method.Name == "Build" && method.Parameters.Length == 0)
        {
            var hostReceiver = GetReceiverExpression(invocation, method);

            // The Build that snapshots must be the framework builder's own. A type may implement
            // a builder contract and still declare an unrelated Build alongside it.
            if (hostReceiver is null || !IsHostBuilder(definition.ContainingType, knownTypes))
            {
                return false;
            }

            var servicesProperty = FindServicesProperty(definition.ContainingType, knownTypes);

            if (
                servicesProperty is null
                || !CollectionKey.TryCreate(
                    hostReceiver,
                    semanticModel,
                    knownTypes,
                    reassignedSymbols,
                    out var hostKey
                )
            )
            {
                return false;
            }

            collectionEvent = new CollectionEvent(
                invocation,
                hostReceiver,
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
        else if (
            !IsServiceCollection(semanticModel.GetTypeInfo(receiver).Type, knownTypes)
            || !TakesServiceDescriptor(definition, knownTypes)
            || !IsInNamespaceStartingWith(
                definition.ContainingType,
                FrameworkMemberNamespacePrefixes
            )
        )
        {
            // `services.Add(descriptor)` and friends are `ICollection<ServiceDescriptor>`
            // members reached through the collection itself. An implementation is free to add
            // same-named overloads of its own that touch no descriptor at all.
            return false;
        }

        // Every registration path — extension or member — ends at the collection's own `Add`.
        // A custom implementation is free to forward that descriptor straight into a container
        // that is already running, in which case nothing is lost at all.
        if (!IsFrameworkOwnedCollection(receiver, semanticModel, knownTypes, reassignedSymbols, 0))
        {
            return false;
        }

        if (
            !CollectionKey.TryCreate(
                receiver,
                semanticModel,
                knownTypes,
                reassignedSymbols,
                out var registrationKey
            )
        )
        {
            return false;
        }

        collectionEvent = new CollectionEvent(invocation, receiver, registrationKey, method.Name);
        return true;
    }

    /// <summary>
    /// True when the collection an invocation acts on is provably one the framework implements:
    /// a framework collection class, or a framework host builder's own <c>Services</c>. A local
    /// is followed to what it was initialised from, and a fluent registration to its receiver.
    /// </summary>
    private static bool IsFrameworkOwnedCollection(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        KnownTypes knownTypes,
        ImmutableHashSet<ISymbol> reassignedSymbols,
        int depth
    )
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return IsFrameworkOwnedCollection(
                    parenthesized.Expression,
                    semanticModel,
                    knownTypes,
                    reassignedSymbols,
                    depth
                );

            case InvocationExpressionSyntax fluent
                when IsFrameworkFluentRegistration(fluent, semanticModel, knownTypes)
                    is { } fluentReceiver:
                return IsFrameworkOwnedCollection(
                    fluentReceiver,
                    semanticModel,
                    knownTypes,
                    reassignedSymbols,
                    depth
                );
        }

        // The declared type settles it whenever it names a class: an interface says nothing
        // about whose `Add` runs.
        if (
            semanticModel.GetTypeInfo(expression).Type is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class
            } collectionType
        )
        {
            return IsServiceCollection(collectionType, knownTypes)
                && IsInNamespaceStartingWith(collectionType, FrameworkMemberNamespacePrefixes);
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;

        // A framework host builder creates and owns the collection behind `Services`.
        if (
            symbol is IPropertySymbol property
            && CollectionKey.IsBuilderServicesProperty(property, knownTypes)
        )
        {
            return true;
        }

        return symbol is ILocalSymbol
            && depth < MaxAliasDepth
            && !reassignedSymbols.Contains(symbol)
            && CollectionKey.GetSingleAssignmentInitializer(symbol) is { } initializer
            && IsFrameworkOwnedCollection(
                initializer,
                semanticModel,
                knownTypes,
                reassignedSymbols,
                depth + 1
            );
    }

    private static bool TakesServiceDescriptor(IMethodSymbol definition, KnownTypes knownTypes) =>
        knownTypes.ServiceDescriptor is not null
        && definition.Parameters.Any(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, knownTypes.ServiceDescriptor)
        );

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

        if (!MutationMethodNames.Contains(method.Name) && !HasMutationPrefix(method.Name))
        {
            return false;
        }

        if (!BuildsNoProviderOfItsOwn(definition))
        {
            return false;
        }

        // An `AddXxx` extension that answers with a scalar is a query over the collection, not a
        // registration: `services.AddCount()` reports a count, it does not register anything.
        if (definition.ReturnsVoid || IsServiceCollection(definition.ReturnType, knownTypes))
        {
            return true;
        }

        if (
            definition.ReturnType.IsValueType
            || definition.ReturnType.SpecialType == SpecialType.System_String
        )
        {
            return false;
        }

        // A third-party extension answering with something other than the collection may have
        // built a provider of its own and wrapped it in the result, which this rule cannot see.
        return IsConventionalExtension(definition);
    }

    /// <summary>
    /// True when a registration extension is known not to build a provider of its own. Framework
    /// and framework-shaped extensions are trusted by namespace; anything else is trusted only
    /// when its body is visible here and contains no <c>BuildServiceProvider</c> call, because a
    /// helper that builds and stores its own provider does see what it just registered.
    /// </summary>
    private static bool BuildsNoProviderOfItsOwn(IMethodSymbol definition)
    {
        if (!definition.IsExtensionMethod)
        {
            return true;
        }

        if (definition.DeclaringSyntaxReferences.Length == 0)
        {
            return IsConventionalExtension(definition);
        }

        var collectionParameterName = definition.Parameters.Length > 0
            ? definition.Parameters[0].Name
            : null;

        foreach (var reference in definition.DeclaringSyntaxReferences)
        {
            foreach (var node in reference.GetSyntax().DescendantNodes())
            {
                if (node is not SimpleNameSyntax name)
                {
                    continue;
                }

                // Building a container is spelled more than one way, and a method group defers
                // the call without ever writing an invocation around the name.
                if (ProviderBuildingMethodNames.Contains(name.Identifier.ValueText))
                {
                    return false;
                }

                // Anywhere the helper hands its collection on — an argument, a constructor, a
                // field it is stored in — something else can build it. Calling through it and
                // handing it back to the caller are the two uses that keep it here.
                if (
                    collectionParameterName is not null
                    && name.Identifier.ValueText == collectionParameterName
                    && !IsContainedUseOfCollection(name)
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// True when a mention of a helper's collection parameter cannot let the collection out:
    /// calling a member on it, or answering the caller with it.
    /// </summary>
    private static bool IsContainedUseOfCollection(SimpleNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == name,
            ReturnStatementSyntax => true,
            ArrowExpressionClauseSyntax => true,
            _ => false,
        };

    /// <summary>
    /// The receiver of an invocation that provably answers with the collection it was given: a
    /// registration extension declared by the framework itself. Returns <see langword="null"/>
    /// for every other invocation.
    /// </summary>
    private static ExpressionSyntax? IsFrameworkFluentRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        KnownTypes knownTypes
    )
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var definition = method.ReducedFrom ?? method;

        if (
            !definition.IsExtensionMethod
            || !IsConventionalExtension(definition)
            || !IsServiceCollection(definition.ReturnType, knownTypes)
            || !IsRegistrationMethod(method, definition, knownTypes)
        )
        {
            return null;
        }

        return GetReceiverExpression(invocation, method);
    }

    private static bool IsConventionalExtension(IMethodSymbol definition) =>
        IsInNamespaceStartingWith(definition.ContainingType, ConventionalExtensionNamespacePrefixes);

    private static bool IsInNamespaceStartingWith(
        ISymbol? symbol,
        ImmutableArray<string> prefixes
    )
    {
        var containingNamespace = symbol?.ContainingNamespace?.ToDisplayString();

        return containingNamespace is not null
            && prefixes.Any(prefix =>
                containingNamespace.StartsWith(prefix, StringComparison.Ordinal)
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
    /// Every place the collection is read other than as the receiver of a call this rule
    /// already understands. Such a read can carry the collection anywhere — into an argument, a
    /// return value, a tuple, a captured lambda, an iterator — where something builds it.
    /// </summary>
    private static List<CollectionRead> GetOtherCollectionReads(
        SyntaxNode codeBlock,
        SemanticModel semanticModel,
        KnownTypes knownTypes,
        ImmutableHashSet<ISymbol> reassignedSymbols,
        HashSet<SyntaxNode> classifiedReceivers
    )
    {
        var reads = new List<CollectionRead>();

        foreach (var node in codeBlock.DescendantNodes())
        {
            if (
                node is not ExpressionSyntax expression
                || node is not (SimpleNameSyntax or MemberAccessExpressionSyntax)
                || classifiedReceivers.Contains(node)
            )
            {
                continue;
            }

            if (!IsServiceCollection(semanticModel.GetTypeInfo(expression).Type, knownTypes))
            {
                continue;
            }

            if (
                CollectionKey.TryCreate(
                    expression,
                    semanticModel,
                    knownTypes,
                    reassignedSymbols,
                    out var key
                )
            )
            {
                reads.Add(new CollectionRead(key, expression.SpanStart));
            }
        }

        return reads;
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
                    AddAssignmentTarget(assignment.Left);
                    break;

                case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                    AddSymbol(argument.Expression);
                    break;

                // `var services = Shared = new ServiceCollection()` stores the collection in two
                // places at once, so the local no longer stands for a collection only this
                // block can reach.
                case VariableDeclaratorSyntax { Initializer.Value: { } initializer } declarator
                    when Unparenthesize(initializer) is AssignmentExpressionSyntax:
                    if (semanticModel.GetDeclaredSymbol(declarator) is { } declaredLocal)
                    {
                        builder.Add(declaredLocal);
                    }

                    break;
            }
        }

        return builder.ToImmutable();

        static ExpressionSyntax Unparenthesize(ExpressionSyntax expression) =>
            expression is ParenthesizedExpressionSyntax parenthesized
                ? Unparenthesize(parenthesized.Expression)
                : expression;

        void AddSymbol(ExpressionSyntax expression)
        {
            if (semanticModel.GetSymbolInfo(expression).Symbol is { } symbol)
            {
                builder.Add(symbol);
            }
        }

        // `(services, fresh) = (fresh, services)` writes each element, not the tuple.
        void AddAssignmentTarget(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case TupleExpressionSyntax tuple:
                    foreach (var element in tuple.Arguments)
                    {
                        AddAssignmentTarget(element.Expression);
                    }

                    return;

                case DeclarationExpressionSyntax declaration
                    when semanticModel.GetSymbolInfo(declaration.Designation).Symbol
                        is { } declared:
                    builder.Add(declared);
                    return;

                default:
                    AddSymbol(expression);
                    return;
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
            return GetStaticSpellingReceiverArgument(invocation, method)?.Expression;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
    }

    /// <summary>
    /// The argument bound to an extension's <c>this</c> parameter when it is called in its
    /// static spelling. Named arguments can reorder the list, so the parameter name decides.
    /// </summary>
    private static ArgumentSyntax? GetStaticSpellingReceiverArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0 || method.Parameters.Length == 0)
        {
            return null;
        }

        var receiverParameterName = method.Parameters[0].Name;

        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == receiverParameterName)
            {
                return argument;
            }
        }

        // No argument names the receiver parameter, so the first positional one fills it.
        return arguments[0].NameColon is null ? arguments[0] : null;
    }

    private static bool IsHostBuilder(ITypeSymbol? type, KnownTypes knownTypes) =>
        type is not null
        && knownTypes.HostBuilderTypes.Any(hostBuilderType =>
            SymbolEqualityComparer.Default.Equals(type, hostBuilderType)
        );

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
            if (
                !ReferenceEquals(buildStatement, buildChain[0])
                || IsConditionallyEvaluated(build, buildStatement)
            )
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

    /// <summary>
    /// True when the build sits under an operator that can skip it even though its statement
    /// runs: a ternary arm, a short-circuit or null-coalescing right operand, a conditional
    /// access, or a switch-expression arm.
    /// </summary>
    private static bool IsConditionallyEvaluated(SyntaxNode build, StatementSyntax statement)
    {
        for (
            var current = build;
            current is not null && current != statement;
            current = current.Parent
        )
        {
            switch (current.Parent)
            {
                case ConditionalExpressionSyntax conditional when conditional.Condition != current:
                case ConditionalAccessExpressionSyntax conditionalAccess
                    when conditionalAccess.Expression != current:
                case SwitchExpressionArmSyntax:
                    return true;

                case BinaryExpressionSyntax binary
                    when binary.Right == current
                        && (
                            binary.IsKind(SyntaxKind.LogicalAndExpression)
                            || binary.IsKind(SyntaxKind.LogicalOrExpression)
                            || binary.IsKind(SyntaxKind.CoalesceExpression)
                        ):
                    return true;
            }
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
            INamedTypeSymbol? serviceDescriptor,
            ImmutableArray<INamedTypeSymbol> hostBuilderTypes
        )
        {
            ServiceCollection = serviceCollection;
            ContainerBuilderExtensions = containerBuilderExtensions;
            ServiceProvider = serviceProvider;
            ServiceDescriptor = serviceDescriptor;
            HostBuilderTypes = hostBuilderTypes;
        }

        public INamedTypeSymbol ServiceCollection { get; }

        public INamedTypeSymbol ContainerBuilderExtensions { get; }

        public INamedTypeSymbol? ServiceProvider { get; }

        public INamedTypeSymbol? ServiceDescriptor { get; }

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
                compilation.GetTypeByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.ServiceDescriptor"
                ),
                hostBuilderTypes.ToImmutable()
            );
        }
    }

    private sealed class CollectionRead
    {
        public CollectionRead(CollectionKey key, int position)
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
            ExpressionSyntax receiver,
            CollectionKey key,
            string methodName
        )
        {
            Invocation = invocation;
            Receiver = receiver;
            Key = key;
            MethodName = methodName;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public ExpressionSyntax Receiver { get; }

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
            KnownTypes knownTypes,
            ImmutableHashSet<ISymbol> reassignedSymbols,
            out CollectionKey key
        )
        {
            key = null!;
            var path = ImmutableArray.CreateBuilder<ISymbol>();

            if (!TryBuild(expression, semanticModel,
                        knownTypes, reassignedSymbols, path, 0))
            {
                return false;
            }

            // Only a collection rooted in a local of this code block is provably finished with.
            // A parameter is the caller's, and a field or property is reachable from every other
            // member of the type — either can be built again outside this block.
            if (path.Count == 0 || path[0] is not ILocalSymbol)
            {
                return false;
            }

            key = new CollectionKey(path.ToImmutable());
            return true;
        }

        private static bool TryBuild(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            KnownTypes knownTypes,
            ImmutableHashSet<ISymbol> reassignedSymbols,
            ImmutableArray<ISymbol>.Builder path,
            int depth
        )
        {
            switch (expression)
            {
                // A framework registration extension answers with the very collection it was
                // handed, so `services.AddSingleton<T>()` names the same collection `services`
                // does. Only the framework's own extensions are folded this way: a third-party
                // `AddXxx` returning a collection is free to return a different one.
                case InvocationExpressionSyntax fluent
                    when IsFrameworkFluentRegistration(fluent, semanticModel, knownTypes)
                        is { } fluentReceiver:
                    return TryBuild(
                        fluentReceiver,
                        semanticModel,
                        knownTypes,
                        reassignedSymbols,
                        path,
                        depth
                    );

                // A user-defined conversion is a method call in disguise and may hand back a
                // different collection on each evaluation.
                case CastExpressionSyntax cast
                    when semanticModel.GetSymbolInfo(cast).Symbol is not IMethodSymbol:
                    return TryBuild(
                        cast.Expression,
                        semanticModel,
                        knownTypes,
                        reassignedSymbols,
                        path,
                        depth
                    );

                case PostfixUnaryExpressionSyntax suppression
                    when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    return TryBuild(
                        suppression.Operand,
                        semanticModel,
                        knownTypes,
                        reassignedSymbols,
                        path,
                        depth
                    );

                case BinaryExpressionSyntax asCast when asCast.IsKind(SyntaxKind.AsExpression):
                    return TryBuild(
                        asCast.Left,
                        semanticModel,
                        knownTypes,
                        reassignedSymbols,
                        path,
                        depth
                    );

                case ParenthesizedExpressionSyntax parenthesized:
                    return TryBuild(
                        parenthesized.Expression,
                        semanticModel,
                        knownTypes,
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
                        knownTypes,
                            reassignedSymbols,
                            path,
                            depth
                        )
                        && TryAppend(
                            memberAccess.Name,
                            semanticModel,
                    knownTypes,
                            reassignedSymbols,
                            path,
                            depth
                        );

                case SimpleNameSyntax simpleName:
                    return TryAppend(simpleName, semanticModel,
                    knownTypes, reassignedSymbols, path, depth);

                default:
                    return false;
            }
        }

        private static bool TryAppend(
            SimpleNameSyntax name,
            SemanticModel semanticModel,
            KnownTypes knownTypes,
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

            // A computed getter can hand back a different collection on each access, so it does
            // not identify one. Auto-properties and a framework builder's Services do.
            if (
                symbol is IPropertySymbol property
                && !IsStableProperty(property, knownTypes)
            )
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
                if (TryBuild(initializer, semanticModel,
                        knownTypes, reassignedSymbols, path, depth + 1))
                {
                    // Folding through a stable auto-property is what lets a later
                    // `builder.Build()` cancel a finding on a local that captured
                    // `builder.Services`; unstable properties never reach a path at all.
                    return true;
                }

                path.Count = restorePoint;
            }

            path.Add(symbol);
            return true;
        }

        public static bool IsBuilderServicesProperty(
            IPropertySymbol property,
            KnownTypes knownTypes
        ) =>
            property.Name == "Services"
            && knownTypes.HostBuilderTypes.Any(hostBuilderType =>
                SymbolEqualityComparer.Default.Equals(property.ContainingType, hostBuilderType)
            );

        /// <summary>
        /// A property identifies one collection only when reading it twice is guaranteed to
        /// yield the same object: an auto-property backed by a field, or a framework builder's
        /// <c>Services</c>.
        /// </summary>
        private static bool IsStableProperty(IPropertySymbol property, KnownTypes knownTypes) =>
            IsBuilderServicesProperty(property, knownTypes)
            || property
                .ContainingType.GetMembers()
                .OfType<IFieldSymbol>()
                .Any(field =>
                    SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property)
                );

        /// <summary>
        /// The initializer of a local that is written exactly once, at its declaration. Callers
        /// have already excluded locals reassigned anywhere in the block.
        /// </summary>
        public static ExpressionSyntax? GetSingleAssignmentInitializer(ISymbol symbol)
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
