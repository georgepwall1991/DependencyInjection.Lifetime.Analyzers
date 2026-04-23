using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects registered services with dependencies that are not registered.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI015_UnresolvableDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string AssumeFrameworkServicesRegisteredOption =
        "dotnet_code_quality.DI015.assume_framework_services_registered";

    internal const string MissingDependencyTypeNamePropertyName = "MissingDependencyTypeName";
    internal const string MissingDependencyCanSelfBindPropertyName = "MissingDependencyCanSelfBind";
    internal const string MissingDependencyPathLengthPropertyName = "MissingDependencyPathLength";
    internal const string MissingDependencyCountPropertyName = "MissingDependencyCount";
    internal const string MissingDependencyIsKeyedPropertyName = "MissingDependencyIsKeyed";
    internal const string MissingDependencyKeyLiteralPropertyName = "MissingDependencyKeyLiteral";
    internal const string RegistrationLifetimePropertyName = "RegistrationLifetime";
    internal const string DependencySourceKindPropertyName = "DependencySourceKind";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UnresolvableDependency);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            var assumeFrameworkServicesRegisteredResolver = CreateAssumeFrameworkServicesRegisteredResolver(
                compilationContext.Options.AnalyzerConfigOptionsProvider);
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
            var invocationObservations = new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel);

                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);

                    if (ServiceCollectionReachabilityAnalyzer.IsPotentialServiceCollectionWrapperInvocation(
                            invocation,
                            syntaxContext.SemanticModel))
                    {
                        invocationObservations.Enqueue(
                            new ServiceCollectionReachabilityAnalyzer.InvocationObservation(
                                invocation,
                                syntaxContext.SemanticModel));
                    }
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeRegistrations(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    assumeFrameworkServicesRegisteredResolver,
                    invocationObservations,
                    semanticModelsByTree));
        });
    }

    private static void AnalyzeRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation> invocationObservations,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var observedInvocations = invocationObservations.ToImmutableArray();
        var reachabilityAnalyzer = ServiceCollectionReachabilityAnalyzer.Create(
            context.Compilation,
            observedInvocations,
            registrationCollector.AllRegistrations);
        var observationsByTree = observedInvocations
            .GroupBy(observation => observation.Invocation.SyntaxTree)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());
        var allRegistrationLocations = new HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey>(
            registrationCollector.AllRegistrations
                .Select(registration => ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location))
                .Where(static key => key.HasValue)
                .Select(static key => key!.Value));
        var fallbackOpaquePredecessors = BuildEarlierPotentialWrapperLookup(
            observationsByTree,
            allRegistrationLocations);
        var definitelyRemovedRegistrations = BuildDefinitelyRemovedRegistrationLookup(
            registrationCollector.AllRegistrations,
            registrationCollector.OrderedMutations);
        var resolutionEngine = new DependencyResolutionEngine(
            registrationCollector,
            wellKnownTypes,
            registration => reachabilityAnalyzer.IsReachable(registration.Location) &&
                            !IsDefinitelyRemoved(registration, definitelyRemovedRegistrations));

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (!reachabilityAnalyzer.IsReachable(registration.Location))
            {
                continue;
            }

            if (IsDefinitelyRemoved(registration, definitelyRemovedRegistrations))
            {
                continue;
            }

            if (registration.FactoryExpression is not null)
            {
                AnalyzeFactoryRegistration(
                    context,
                    registration,
                    wellKnownTypes,
                    resolutionEngine,
                    assumeFrameworkServicesRegisteredResolver,
                    reachabilityAnalyzer,
                    fallbackOpaquePredecessors,
                    semanticModelsByTree);
                continue;
            }

            if (registration.ImplementationType is null)
            {
                continue;
            }

            var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(
                registration.Location.SourceTree);
            var resolutionResult = resolutionEngine.ResolveRegistration(
                registration,
                assumeFrameworkServicesRegistered);

            ReportMissingDependencies(
                context,
                registration.Location,
                registration.ServiceType.Name,
                registration.Lifetime,
                DependencySourceKind.ConstructorParameter,
                HasOpaquePredecessor(
                    registration.Location,
                    reachabilityAnalyzer,
                    fallbackOpaquePredecessors),
                resolutionResult);
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        WellKnownTypes? wellKnownTypes,
        DependencyResolutionEngine resolutionEngine,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ServiceCollectionReachabilityAnalyzer reachabilityAnalyzer,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> fallbackOpaquePredecessors,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (!semanticModelsByTree.TryGetValue(registration.FactoryExpression!.SyntaxTree, out var semanticModel))
        {
            return;
        }

        var requests = FactoryDependencyAnalysis.GetDependencyRequests(
            registration.FactoryExpression,
            semanticModel,
            wellKnownTypes,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);

        foreach (var request in requests)
        {
            var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(
                request.SourceLocation.SourceTree);
            var resolutionResult = resolutionEngine.ResolveFactoryRequest(
                registration,
                request,
                assumeFrameworkServicesRegistered);

            ReportMissingDependencies(
                context,
                request.SourceLocation,
                registration.ServiceType.Name,
                registration.Lifetime,
                request.SourceKind,
                HasOpaquePredecessor(
                    registration.Location,
                    reachabilityAnalyzer,
                    fallbackOpaquePredecessors),
                resolutionResult);
        }
    }

    private static void ReportMissingDependencies(
        CompilationAnalysisContext context,
        Location location,
        string serviceTypeName,
        ServiceLifetime registrationLifetime,
        DependencySourceKind sourceKind,
        bool hasOpaquePredecessor,
        ResolutionResult resolutionResult)
    {
        if (resolutionResult.IsResolvable ||
            resolutionResult.Confidence != ResolutionConfidence.High ||
            hasOpaquePredecessor ||
            resolutionResult.MissingDependencies.IsDefaultOrEmpty)
        {
            return;
        }

        var missingDependencyCount = resolutionResult.MissingDependencies.Length.ToString(CultureInfo.InvariantCulture);

        foreach (var missingDependency in resolutionResult.MissingDependencies)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(
                    MissingDependencyTypeNamePropertyName,
                    DependencyResolutionEngine.GetGlobalTypeDisplayName(missingDependency.Type))
                .Add(
                    MissingDependencyCanSelfBindPropertyName,
                    DependencyResolutionEngine.CanSelfBind(missingDependency.Type)
                        .ToString(CultureInfo.InvariantCulture))
                .Add(
                    MissingDependencyPathLengthPropertyName,
                    missingDependency.PathLength.ToString(CultureInfo.InvariantCulture))
                .Add(MissingDependencyCountPropertyName, missingDependencyCount)
                .Add(
                    MissingDependencyIsKeyedPropertyName,
                    missingDependency.IsKeyed.ToString(CultureInfo.InvariantCulture))
                .Add(MissingDependencyKeyLiteralPropertyName, missingDependency.KeyLiteral)
                .Add(RegistrationLifetimePropertyName, registrationLifetime.ToString())
                .Add(DependencySourceKindPropertyName, sourceKind.ToString());

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UnresolvableDependency,
                location,
                properties,
                serviceTypeName,
                DependencyResolutionEngine.FormatDependencyName(
                    missingDependency.Type,
                    missingDependency.Key,
                    missingDependency.IsKeyed));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static Func<SyntaxTree?, bool> CreateAssumeFrameworkServicesRegisteredResolver(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var valuesByTree = new ConcurrentDictionary<SyntaxTree, bool>();
        var hasGlobalValue = TryParseAssumeFrameworkServicesRegistered(
            optionsProvider.GlobalOptions,
            out var globalValue);

        return syntaxTree =>
        {
            if (syntaxTree is null)
            {
                return hasGlobalValue ? globalValue : true;
            }

            return valuesByTree.GetOrAdd(syntaxTree, tree =>
            {
                if (TryParseAssumeFrameworkServicesRegistered(
                        optionsProvider.GetOptions(tree),
                        out var treeValue))
                {
                    return treeValue;
                }

                return hasGlobalValue ? globalValue : true;
            });
        };
    }

    private static bool TryParseAssumeFrameworkServicesRegistered(
        AnalyzerConfigOptions options,
        out bool value)
    {
        value = true;

        if (!options.TryGetValue(AssumeFrameworkServicesRegisteredOption, out var optionValue))
        {
            return false;
        }

        if (bool.TryParse(optionValue, out value))
        {
            return true;
        }

        switch (optionValue)
        {
            case "1":
                value = true;
                return true;
            case "0":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool HasOpaquePredecessor(
        Location registrationLocation,
        ServiceCollectionReachabilityAnalyzer reachabilityAnalyzer,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> fallbackOpaquePredecessors)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registrationLocation);
        return reachabilityAnalyzer.HasOpaquePredecessor(registrationLocation) ||
               (locationKey.HasValue && fallbackOpaquePredecessors.Contains(locationKey.Value));
    }

    private static HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> BuildDefinitelyRemovedRegistrationLookup(
        IEnumerable<ServiceRegistration> registrations,
        IEnumerable<OrderedRegistrationMutation> mutations)
    {
        var removedRegistrations = new HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey>();
        var registrationsByTree = registrations
            .Where(static registration => registration.FlowKey is not null &&
                                          registration.Location.SourceTree is not null)
            .GroupBy(static registration => registration.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static registration => registration.Location.SourceSpan.Start).ToArray());
        var mutationsByTree = mutations
            .Where(static mutation => mutation.FlowKey is not null &&
                                      mutation.Location.SourceTree is not null)
            .GroupBy(static mutation => mutation.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static mutation => mutation.Location.SourceSpan.Start).ToArray());

        foreach (var tree in registrationsByTree.Keys.Union(mutationsByTree.Keys))
        {
            registrationsByTree.TryGetValue(tree, out var orderedRegistrations);
            mutationsByTree.TryGetValue(tree, out var orderedMutations);
            orderedRegistrations ??= [];
            orderedMutations ??= [];

            var activeRegistrations = new List<ServiceRegistration>();
            var registrationIndex = 0;
            var mutationIndex = 0;

            while (registrationIndex < orderedRegistrations.Length ||
                   mutationIndex < orderedMutations.Length)
            {
                var nextRegistrationStart = registrationIndex < orderedRegistrations.Length
                    ? orderedRegistrations[registrationIndex].Location.SourceSpan.Start
                    : int.MaxValue;
                var nextMutationStart = mutationIndex < orderedMutations.Length
                    ? orderedMutations[mutationIndex].Location.SourceSpan.Start
                    : int.MaxValue;

                if (nextRegistrationStart < nextMutationStart)
                {
                    activeRegistrations.Add(orderedRegistrations[registrationIndex]);
                    registrationIndex++;
                    continue;
                }

                ApplyMutation(
                    orderedMutations[mutationIndex],
                    activeRegistrations,
                    removedRegistrations);
                mutationIndex++;
            }
        }

        return removedRegistrations;
    }

    private static void ApplyMutation(
        OrderedRegistrationMutation mutation,
        List<ServiceRegistration> activeRegistrations,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> removedRegistrations)
    {
        if (mutation.Kind == RegistrationMutationKind.RemoveAll)
        {
            for (var i = activeRegistrations.Count - 1; i >= 0; i--)
            {
                if (!CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
                {
                    continue;
                }

                AddRemovedRegistration(activeRegistrations[i], removedRegistrations);
                activeRegistrations.RemoveAt(i);
            }

            return;
        }

        for (var i = 0; i < activeRegistrations.Count; i++)
        {
            if (!CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
            {
                continue;
            }

            AddRemovedRegistration(activeRegistrations[i], removedRegistrations);
            activeRegistrations.RemoveAt(i);
            return;
        }
    }

    private static bool CanMutationRemoveRegistration(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration) =>
        string.Equals(mutation.FlowKey, registration.FlowKey, StringComparison.Ordinal) &&
        SymbolEqualityComparer.Default.Equals(mutation.ServiceType, registration.ServiceType);

    private static void AddRemovedRegistration(
        ServiceRegistration registration,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> removedRegistrations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location);
        if (locationKey.HasValue)
        {
            removedRegistrations.Add(locationKey.Value);
        }
    }

    private static bool IsDefinitelyRemoved(
        ServiceRegistration registration,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> definitelyRemovedRegistrations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location);
        return locationKey.HasValue &&
               definitelyRemovedRegistrations.Contains(locationKey.Value);
    }

    // Source-defined wrappers are handled by ServiceCollectionReachabilityAnalyzer.
    // This fallback specifically covers earlier opaque/source-less wrapper calls that
    // share the same IServiceCollection flow as the later registration.
    private static HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> BuildEarlierPotentialWrapperLookup(
        Dictionary<SyntaxTree, ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation>> observationsByTree,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> allRegistrationLocations)
    {
        var fallbackOpaquePredecessors = new HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey>();
        var opaqueWrapperMethods = BuildOpaqueWrapperMethodLookup(
            observationsByTree,
            allRegistrationLocations);

        foreach (var pair in observationsByTree)
        {
            var hasOpaqueByFlow = new Dictionary<RegistrationFlowKey, bool>(RegistrationFlowKeyComparer.Instance);
            var orderedObservations = pair.Value
                .OrderBy(observation => observation.Invocation.SpanStart);

            foreach (var observation in orderedObservations)
            {
                var observationMethod = ServiceCollectionReachabilityAnalyzer.NormalizeContainingMethod(
                    observation.SemanticModel.GetEnclosingSymbol(observation.Invocation.SpanStart) as IMethodSymbol);
                var observationReceiverKey = ServiceCollectionReachabilityAnalyzer.GetServiceCollectionReceiverKey(
                    observation.Invocation,
                    observation.SemanticModel);
                if (observationMethod is null || observationReceiverKey is null)
                {
                    continue;
                }

                var flowKey = new RegistrationFlowKey(observationMethod, observationReceiverKey);
                var observationLocationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(
                    observation.Invocation.GetLocation());
                var isRegistration = observationLocationKey.HasValue &&
                    allRegistrationLocations.Contains(observationLocationKey.Value);

                if (!isRegistration)
                {
                    if (ServiceCollectionReachabilityAnalyzer.TryGetInvocationTarget(
                            observation.Invocation,
                            observation.SemanticModel,
                            out var targetMethod))
                    {
                        var normalizedTarget = ServiceCollectionReachabilityAnalyzer.NormalizeContainingMethod(targetMethod);
                        if (normalizedTarget is not null &&
                            opaqueWrapperMethods.Contains(normalizedTarget))
                        {
                            hasOpaqueByFlow[flowKey] = true;
                            continue;
                        }
                    }

                    if (IsPotentialOpaqueServiceCollectionWrapperInvocation(
                            observation.Invocation,
                            observation.SemanticModel))
                    {
                        hasOpaqueByFlow[flowKey] = true;
                    }

                    continue;
                }

                if (observationLocationKey.HasValue &&
                    hasOpaqueByFlow.TryGetValue(flowKey, out var hasOpaquePredecessor) &&
                    hasOpaquePredecessor)
                {
                    fallbackOpaquePredecessors.Add(observationLocationKey.Value);
                }
            }
        }

        return fallbackOpaquePredecessors;
    }

    private static HashSet<IMethodSymbol> BuildOpaqueWrapperMethodLookup(
        Dictionary<SyntaxTree, ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation>> observationsByTree,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> allRegistrationLocations)
    {
        var wrapperObservationsByMethod =
            new Dictionary<IMethodSymbol, ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation>>(
                SymbolEqualityComparer.Default);

        foreach (var observation in observationsByTree.Values.SelectMany(static observations => observations))
        {
            var containingMethod = ServiceCollectionReachabilityAnalyzer.NormalizeContainingMethod(
                observation.SemanticModel.GetEnclosingSymbol(observation.Invocation.SpanStart) as IMethodSymbol);
            if (containingMethod is null ||
                !ServiceCollectionReachabilityAnalyzer.IsSourceDefinedCustomServiceCollectionWrapper(containingMethod))
            {
                continue;
            }

            if (!wrapperObservationsByMethod.TryGetValue(containingMethod, out var existing))
            {
                wrapperObservationsByMethod[containingMethod] =
                    ImmutableArray.Create(observation);
                continue;
            }

            wrapperObservationsByMethod[containingMethod] = existing.Add(observation);
        }

        var opaqueWrapperMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var cache = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

        foreach (var wrapperMethod in wrapperObservationsByMethod.Keys)
        {
            if (ContainsOpaqueWrapperInvocation(
                    wrapperMethod,
                    wrapperObservationsByMethod,
                    allRegistrationLocations,
                    cache,
                    new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)))
            {
                opaqueWrapperMethods.Add(wrapperMethod);
            }
        }

        return opaqueWrapperMethods;
    }

    private static bool ContainsOpaqueWrapperInvocation(
        IMethodSymbol wrapperMethod,
        Dictionary<IMethodSymbol, ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation>> wrapperObservationsByMethod,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> allRegistrationLocations,
        Dictionary<IMethodSymbol, bool> cache,
        HashSet<IMethodSymbol> wrapperStack)
    {
        if (cache.TryGetValue(wrapperMethod, out var isOpaque))
        {
            return isOpaque;
        }

        if (!wrapperStack.Add(wrapperMethod))
        {
            return true;
        }

        if (!wrapperObservationsByMethod.TryGetValue(wrapperMethod, out var observations))
        {
            wrapperStack.Remove(wrapperMethod);
            cache[wrapperMethod] = false;
            return false;
        }

        foreach (var observation in observations.OrderBy(static observation => observation.Invocation.SpanStart))
        {
            var observationLocationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(
                observation.Invocation.GetLocation());
            if (observationLocationKey.HasValue &&
                allRegistrationLocations.Contains(observationLocationKey.Value))
            {
                continue;
            }

            if (ServiceCollectionReachabilityAnalyzer.TryGetInvocationTarget(
                    observation.Invocation,
                    observation.SemanticModel,
                    out var targetMethod))
            {
                var normalizedTarget = ServiceCollectionReachabilityAnalyzer.NormalizeContainingMethod(targetMethod);
                if (normalizedTarget is not null &&
                    ServiceCollectionReachabilityAnalyzer.IsSourceDefinedCustomServiceCollectionWrapper(normalizedTarget) &&
                    ContainsOpaqueWrapperInvocation(
                        normalizedTarget,
                        wrapperObservationsByMethod,
                        allRegistrationLocations,
                        cache,
                        wrapperStack))
                {
                    wrapperStack.Remove(wrapperMethod);
                    cache[wrapperMethod] = true;
                    return true;
                }
            }

            if (IsPotentialOpaqueServiceCollectionWrapperInvocation(
                    observation.Invocation,
                    observation.SemanticModel))
            {
                wrapperStack.Remove(wrapperMethod);
                cache[wrapperMethod] = true;
                return true;
            }
        }

        wrapperStack.Remove(wrapperMethod);
        cache[wrapperMethod] = false;
        return false;
    }

    private static bool IsPotentialOpaqueServiceCollectionWrapperInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (!ServiceCollectionReachabilityAnalyzer.IsPotentialServiceCollectionWrapperInvocation(
                invocation,
                semanticModel))
        {
            return false;
        }

        return !ServiceCollectionReachabilityAnalyzer.TryGetInvocationTarget(invocation, semanticModel, out var targetMethod) ||
               !ServiceCollectionReachabilityAnalyzer.IsSourceDefinedCustomServiceCollectionWrapper(targetMethod);
    }

    private readonly struct RegistrationFlowKey : IEquatable<RegistrationFlowKey>
    {
        public RegistrationFlowKey(IMethodSymbol method, string receiverKey)
        {
            Method = method;
            ReceiverKey = receiverKey;
        }

        public IMethodSymbol Method { get; }

        public string ReceiverKey { get; }

        public bool Equals(RegistrationFlowKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Method, other.Method) &&
                   string.Equals(ReceiverKey, other.ReceiverKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is RegistrationFlowKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Method);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ReceiverKey);
                return hashCode;
            }
        }
    }

    private sealed class RegistrationFlowKeyComparer : IEqualityComparer<RegistrationFlowKey>
    {
        public static readonly RegistrationFlowKeyComparer Instance = new RegistrationFlowKeyComparer();

        public bool Equals(RegistrationFlowKey x, RegistrationFlowKey y)
        {
            return x.Equals(y);
        }

        public int GetHashCode(RegistrationFlowKey obj)
        {
            return obj.GetHashCode();
        }
    }
}
