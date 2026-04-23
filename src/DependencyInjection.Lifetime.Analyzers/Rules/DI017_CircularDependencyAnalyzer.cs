using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects high-confidence circular dependencies in DI activation graphs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI017_CircularDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string CycleLengthPropertyName = "CycleLength";
    private const string CycleServicesPropertyName = "CycleServices";
    private const string CycleKeysPropertyName = "CycleKeys";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CircularDependency);

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
            var invocationObservations = new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(invocation, syntaxContext.SemanticModel);
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
                endContext => AnalyzeCycles(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    invocationObservations,
                    semanticModelsByTree));
        });
    }

    private static void AnalyzeCycles(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation> invocationObservations,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var observedInvocations = invocationObservations.ToImmutableArray();
        var reachabilityAnalyzer = ServiceCollectionReachabilityAnalyzer.Create(
            context.Compilation,
            observedInvocations,
            registrationCollector.AllRegistrations);

        var registrations = GetEffectiveRegistrations(registrationCollector, reachabilityAnalyzer);
        if (registrations.IsDefaultOrEmpty)
        {
            return;
        }

        var graph = new CycleGraph(
            context.Compilation,
            wellKnownTypes,
            registrations,
            semanticModelsByTree);
        var reportedCycles = new HashSet<string>();
        var knownNoCycle = new HashSet<ServiceNodeKey>();

        foreach (var registration in registrations)
        {
            if (registration.HasImplementationInstance ||
                registration.ImplementationType is null && registration.FactoryExpression is null)
            {
                continue;
            }

            var rootType = NormalizeServiceTypeForPath(registration.ServiceType);
            var rootKey = new ServiceNodeKey(
                rootType,
                registration.Key,
                registration.IsKeyed,
                registration.FlowKey,
                registration.Order);

            if (knownNoCycle.Contains(rootKey))
            {
                continue;
            }

            var cyclePath = new List<ServiceNodeKey>();
            var visiting = new HashSet<ServiceNodeKey>();

            if (!DetectCycle(
                    rootKey,
                    registration,
                    graph,
                    visiting,
                    cyclePath,
                    knownNoCycle))
            {
                continue;
            }

            var cycleKey = GetCanonicalCycleKey(cyclePath);
            if (!reportedCycles.Add(cycleKey))
            {
                continue;
            }

            var cycleMembers = GetCycleMembers(cyclePath);
            var canonicalRegistration = FindCanonicalRegistration(cycleMembers, registrations, registration);
            var canonicalKey = FindCycleMemberForRegistration(cycleMembers, canonicalRegistration, out var member)
                ? member
                : new ServiceNodeKey(
                    NormalizeServiceTypeForPath(canonicalRegistration.ServiceType),
                    canonicalRegistration.Key,
                    canonicalRegistration.IsKeyed,
                    canonicalRegistration.FlowKey,
                    canonicalRegistration.Order);
            var cycleDescription = FormatCycleFromRegistration(cycleMembers, canonicalKey);
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.CircularDependency,
                canonicalRegistration.Location,
                additionalLocations: null,
                properties: CreateDiagnosticProperties(cycleMembers),
                canonicalRegistration.ImplementationType?.Name ?? canonicalRegistration.ServiceType.Name,
                cycleDescription);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool DetectCycle(
        ServiceNodeKey serviceKey,
        ServiceRegistration registration,
        CycleGraph graph,
        HashSet<ServiceNodeKey> visiting,
        List<ServiceNodeKey> cyclePath,
        HashSet<ServiceNodeKey> knownNoCycle)
    {
        cyclePath.Add(serviceKey);

        if (!visiting.Add(serviceKey))
        {
            return true;
        }

        if (knownNoCycle.Contains(serviceKey))
        {
            visiting.Remove(serviceKey);
            cyclePath.RemoveAt(cyclePath.Count - 1);
            return false;
        }

        var foundCycle = registration.FactoryExpression is not null
            ? DetectFactoryCycles(serviceKey, registration, graph, visiting, cyclePath, knownNoCycle)
            : DetectImplementationCycles(serviceKey, registration, graph, visiting, cyclePath, knownNoCycle);

        if (foundCycle)
        {
            return true;
        }

        visiting.Remove(serviceKey);
        cyclePath.RemoveAt(cyclePath.Count - 1);
        knownNoCycle.Add(serviceKey);
        return false;
    }

    private static bool DetectFactoryCycles(
        ServiceNodeKey serviceKey,
        ServiceRegistration registration,
        CycleGraph graph,
        HashSet<ServiceNodeKey> visiting,
        List<ServiceNodeKey> cyclePath,
        HashSet<ServiceNodeKey> knownNoCycle)
    {
        if (registration.FactoryExpression is null ||
            !graph.TryGetSemanticModel(registration.FactoryExpression.SyntaxTree, out var semanticModel))
        {
            return false;
        }

        var invocations = FactoryAnalysis.GetFactoryInvocations(
                registration.FactoryExpression,
                semanticModel)
            .ToImmutableArray();
        var requests = FactoryDependencyAnalysis.GetDependencyRequests(
            registration.FactoryExpression,
            semanticModel,
            graph.WellKnownTypes,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);

        if (requests.Length == 0)
        {
            return false;
        }

        // If the factory contains additional invocation-shaped behavior that we cannot
        // classify, keep DI017 quiet rather than guessing through an opaque factory.
        if (invocations.Length != requests.Length)
        {
            return false;
        }

        foreach (var request in requests)
        {
            if (request.SourceKind == DependencySourceKind.ActivatorUtilitiesConstruction &&
                request.Type is INamedTypeSymbol implementationType)
            {
                if (DetectImplementationCycles(
                        serviceKey,
                        registration,
                        graph,
                        visiting,
                        cyclePath,
                        knownNoCycle,
                        implementationType))
                {
                    return true;
                }

                continue;
            }

            if (request.Type is not INamedTypeSymbol requestedType)
            {
                continue;
            }

            var requestKey = new ServiceRequestKey(
                NormalizeServiceTypeForPath(requestedType),
                request.Key,
                request.IsKeyed,
                registration.FlowKey);
            var match = graph.FindSingleRegistration(requestKey);
            if (match is null ||
                !CanTraverseRegistration(match.Value.Registration))
            {
                continue;
            }

            if (DetectCycle(
                    CreateNodeKey(match.Value),
                    match.Value.Registration,
                    graph,
                    visiting,
                    cyclePath,
                    knownNoCycle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DetectImplementationCycles(
        ServiceNodeKey serviceKey,
        ServiceRegistration registration,
        CycleGraph graph,
        HashSet<ServiceNodeKey> visiting,
        List<ServiceNodeKey> cyclePath,
        HashSet<ServiceNodeKey> knownNoCycle,
        INamedTypeSymbol? explicitImplementationType = null)
    {
        var implementationType = explicitImplementationType ??
                                 TryGetClosedImplementationTypeForDependency(
                                     serviceKey.Type,
                                     registration.ServiceType,
                                     registration.ImplementationType!) ??
                                 registration.ImplementationType;
        if (implementationType?.IsUnboundGenericType == true)
        {
            implementationType = implementationType.OriginalDefinition;
        }

        if (implementationType is null)
        {
            return false;
        }

        var constructors = GetLikelyCycleConstructors(implementationType, registration, graph);
        foreach (var constructor in constructors)
        {
            foreach (var edge in GetConstructorEdges(constructor, registration, graph))
            {
                foreach (var match in edge.Matches)
                {
                    if (!CanTraverseRegistration(match.Registration))
                    {
                        continue;
                    }

                    if (DetectCycle(
                            CreateNodeKey(match),
                            match.Registration,
                            graph,
                            visiting,
                            cyclePath,
                            knownNoCycle))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<DependencyEdge> GetConstructorEdges(
        IMethodSymbol constructor,
        ServiceRegistration registration,
        CycleGraph graph)
    {
        foreach (var parameter in constructor.Parameters)
        {
            if (ShouldSkipParameter(parameter, graph.WellKnownTypes))
            {
                continue;
            }

            var dependencyType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerable);
            if (dependencyType is not INamedTypeSymbol namedDependencyType)
            {
                continue;
            }

            var serviceKey = KeyedServiceHelpers.GetServiceKey(
                parameter,
                registration.IsKeyed ? registration.Key : null,
                registration.IsKeyed,
                registration.IsKeyed ? registration.KeyLiteral : null);
            if (serviceKey.IsUnknown)
            {
                yield break;
            }

            var requestKey = new ServiceRequestKey(
                NormalizeServiceTypeForPath(namedDependencyType),
                serviceKey.Key,
                serviceKey.IsKeyed,
                registration.FlowKey);
            var matches = isEnumerable
                ? graph.FindAllRegistrations(requestKey)
                : graph.FindSingleRegistration(requestKey) is { } match
                    ? ImmutableArray.Create(match)
                    : ImmutableArray<RegistrationMatch>.Empty;

            if (!matches.IsDefaultOrEmpty)
            {
                yield return new DependencyEdge(matches);
            }
        }
    }

    private static IEnumerable<IMethodSymbol> GetLikelyCycleConstructors(
        INamedTypeSymbol implementationType,
        ServiceRegistration registration,
        CycleGraph graph)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType).ToList();
        if (constructors.Count == 0)
        {
            return [];
        }

        var attributedConstructors = constructors
            .Where(HasActivatorUtilitiesConstructorAttribute)
            .ToList();
        if (attributedConstructors.Count > 1)
        {
            return [];
        }

        if (attributedConstructors.Count == 1)
        {
            return attributedConstructors;
        }

        if (constructors.Count == 1)
        {
            return constructors;
        }

        var resolvableConstructors = constructors
            .Where(constructor => constructor.Parameters.All(parameter =>
                IsDirectlyResolvableConstructorParameter(parameter, registration, graph)))
            .ToList();
        if (resolvableConstructors.Count == 0)
        {
            return [];
        }

        var selectedParameterCount = resolvableConstructors.Max(constructor => constructor.Parameters.Length);
        var selectedConstructors = resolvableConstructors
            .Where(constructor => constructor.Parameters.Length == selectedParameterCount)
            .ToList();

        return selectedConstructors.Count == 1
            ? selectedConstructors
            : [];
    }

    private static bool IsDirectlyResolvableConstructorParameter(
        IParameterSymbol parameter,
        ServiceRegistration registration,
        CycleGraph graph)
    {
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        if (KeyedServiceHelpers.IsServiceKeyParameter(parameter))
        {
            return true;
        }

        var type = parameter.Type;
        if (IsEnumerableDependency(type))
        {
            return true;
        }

        if (type.IsValueType || type.SpecialType != SpecialType.None)
        {
            return false;
        }

        if (graph.WellKnownTypes is not null && graph.WellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            return true;
        }

        if (IsFrameworkProvidedParameter(type, graph.WellKnownTypes))
        {
            return true;
        }

        if (type is not INamedTypeSymbol dependencyType)
        {
            return false;
        }

        var serviceKey = KeyedServiceHelpers.GetServiceKey(
            parameter,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);
        return !serviceKey.IsUnknown &&
               graph.FindSingleRegistration(
                   new ServiceRequestKey(
                       NormalizeServiceTypeForPath(dependencyType),
                       serviceKey.Key,
                       serviceKey.IsKeyed,
                       registration.FlowKey)) is not null;
    }

    private static bool ShouldSkipParameter(IParameterSymbol parameter, WellKnownTypes? wellKnownTypes)
    {
        if (parameter.HasExplicitDefaultValue ||
            parameter.IsOptional ||
            KeyedServiceHelpers.IsServiceKeyParameter(parameter))
        {
            return true;
        }

        var type = parameter.Type;
        if (type.IsValueType || type.SpecialType != SpecialType.None)
        {
            return true;
        }

        if (wellKnownTypes is not null && wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            return true;
        }

        return IsFrameworkProvidedParameter(type, wellKnownTypes);
    }

    private static bool IsFrameworkProvidedParameter(ITypeSymbol type, WellKnownTypes? wellKnownTypes) =>
        wellKnownTypes is not null &&
        (wellKnownTypes.IsConfiguration(type) ||
         wellKnownTypes.IsLogger(type) ||
         wellKnownTypes.IsOptionsAbstraction(type) ||
         IsFrameworkProvidedByName(type));

    private static bool IsFrameworkProvidedByName(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        return (namedType.Name == "ILoggerFactory" && namespaceName == "Microsoft.Extensions.Logging") ||
               (namedType.Name is "IHostEnvironment" or "IWebHostEnvironment" &&
                (namespaceName == "Microsoft.Extensions.Hosting" ||
                 namespaceName == "Microsoft.AspNetCore.Hosting"));
    }

    private static bool HasActivatorUtilitiesConstructorAttribute(IMethodSymbol constructor)
    {
        foreach (var attribute in constructor.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanTraverseRegistration(ServiceRegistration registration) =>
        !registration.HasImplementationInstance &&
        (registration.ImplementationType is not null || registration.FactoryExpression is not null);

    private static ImmutableArray<ServiceRegistration> GetEffectiveRegistrations(
        RegistrationCollector registrationCollector,
        ServiceCollectionReachabilityAnalyzer reachabilityAnalyzer)
    {
        var registrations = SortRegistrationsBySourceLocation(registrationCollector.AllRegistrations)
            .Where(registration =>
                reachabilityAnalyzer.IsReachable(registration.Location) &&
                !reachabilityAnalyzer.HasOpaquePredecessor(registration.Location))
            .ToImmutableArray();
        if (registrations.IsDefaultOrEmpty)
        {
            return ImmutableArray<ServiceRegistration>.Empty;
        }

        var tryAddByLocation = registrationCollector.OrderedRegistrations
            .Select(registration => new
            {
                LocationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location),
                registration.IsTryAdd
            })
            .Where(static item => item.LocationKey.HasValue)
            .GroupBy(static item => item.LocationKey.GetValueOrDefault())
            .ToDictionary(static group => group.Key, static group => group.First().IsTryAdd);
        var mutations = SortMutationsBySourceLocation(registrationCollector.OrderedMutations);
        var registrationItems = registrations
            .Select(registration => new RegistrationSourceItem(registration, SourceOrderKey.Create(registration.Location, registration.Order)))
            .ToImmutableArray();
        var mutationItems = mutations
            .Select(mutation => new MutationSourceItem(mutation, SourceOrderKey.Create(mutation.Location, mutation.Order)))
            .ToImmutableArray();
        var activeRegistrations = new List<ServiceRegistration>();
        var registrationIndex = 0;
        var mutationIndex = 0;

        while (registrationIndex < registrationItems.Length || mutationIndex < mutationItems.Length)
        {
            var hasRegistration = registrationIndex < registrationItems.Length;
            var hasMutation = mutationIndex < mutationItems.Length;

            if (hasRegistration &&
                (!hasMutation ||
                 registrationItems[registrationIndex].SortKey.CompareTo(mutationItems[mutationIndex].SortKey) < 0))
            {
                AddEffectiveRegistration(
                    registrationItems[registrationIndex].Registration,
                    tryAddByLocation,
                    activeRegistrations);
                registrationIndex++;
                continue;
            }

            ApplyMutation(mutationItems[mutationIndex].Mutation, activeRegistrations);
            mutationIndex++;
        }

        return activeRegistrations.ToImmutableArray();
    }

    private static void AddEffectiveRegistration(
        ServiceRegistration registration,
        Dictionary<ServiceCollectionReachabilityAnalyzer.LocationKey, bool> tryAddByLocation,
        List<ServiceRegistration> activeRegistrations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location);
        var isTryAdd = locationKey.HasValue &&
                       tryAddByLocation.TryGetValue(locationKey.Value, out var value) &&
                       value;
        if (isTryAdd &&
            activeRegistrations.Any(active => HasSameRegistrationSlot(active, registration)))
        {
            return;
        }

        activeRegistrations.Add(registration);
    }

    private static void ApplyMutation(
        OrderedRegistrationMutation mutation,
        List<ServiceRegistration> activeRegistrations)
    {
        if (mutation.Kind == RegistrationMutationKind.RemoveAll)
        {
            for (var i = activeRegistrations.Count - 1; i >= 0; i--)
            {
                if (CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
                {
                    activeRegistrations.RemoveAt(i);
                }
            }

            return;
        }

        for (var i = 0; i < activeRegistrations.Count; i++)
        {
            if (CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
            {
                activeRegistrations.RemoveAt(i);
                return;
            }
        }
    }

    private static bool CanMutationRemoveRegistration(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration) =>
        string.Equals(mutation.FlowKey, registration.FlowKey, StringComparison.Ordinal) &&
        SymbolEqualityComparer.Default.Equals(mutation.ServiceType, registration.ServiceType);

    private static bool HasSameRegistrationSlot(
        ServiceRegistration left,
        ServiceRegistration right) =>
        string.Equals(left.FlowKey, right.FlowKey, StringComparison.Ordinal) &&
        left.IsKeyed == right.IsKeyed &&
        Equals(left.Key, right.Key) &&
        SymbolEqualityComparer.Default.Equals(left.ServiceType, right.ServiceType);

    private static ImmutableArray<ServiceRegistration> SortRegistrationsBySourceLocation(
        IEnumerable<ServiceRegistration> registrations) =>
        registrations
            .Select(registration =>
            {
                var lineSpan = registration.Location.GetLineSpan();
                var path = lineSpan.Path;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = registration.Location.SourceTree?.FilePath ?? string.Empty;
                }

                return new
                {
                    Registration = registration,
                    Path = path ?? string.Empty,
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    registration.Order
                };
            })
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Order)
            .Select(item => item.Registration)
            .ToImmutableArray();

    private static ImmutableArray<OrderedRegistrationMutation> SortMutationsBySourceLocation(
        IEnumerable<OrderedRegistrationMutation> mutations) =>
        mutations
            .Select(mutation =>
            {
                var lineSpan = mutation.Location.GetLineSpan();
                var path = lineSpan.Path;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = mutation.Location.SourceTree?.FilePath ?? string.Empty;
                }

                return new
                {
                    Mutation = mutation,
                    Path = path ?? string.Empty,
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    mutation.Order
                };
            })
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Order)
            .Select(item => item.Mutation)
            .ToImmutableArray();

    private static ITypeSymbol UnwrapEnumerableDependency(ITypeSymbol type, out bool isEnumerable)
    {
        isEnumerable = false;
        if (type is INamedTypeSymbol namedType &&
            IsEnumerableDependency(namedType))
        {
            isEnumerable = true;
            return namedType.TypeArguments[0];
        }

        return type;
    }

    private static bool IsEnumerableDependency(ITypeSymbol type) =>
        type is INamedTypeSymbol
        {
            IsGenericType: true,
            ConstructedFrom.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T
        };

    private static INamedTypeSymbol NormalizeServiceTypeForPath(INamedTypeSymbol type)
    {
        if (type.IsGenericType &&
            !type.IsUnboundGenericType &&
            type.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            return type.ConstructUnboundGenericType();
        }

        return type;
    }

    private static ServiceNodeKey CreateNodeKey(RegistrationMatch match) =>
        new(
            NormalizeServiceTypeForPath(match.RequestedType),
            match.Registration.Key,
            match.Registration.IsKeyed,
            match.Registration.FlowKey,
            match.Registration.Order);

    private static ImmutableArray<ServiceNodeKey> GetCycleMembers(List<ServiceNodeKey> cyclePath)
    {
        if (cyclePath.Count < 2)
        {
            return ImmutableArray<ServiceNodeKey>.Empty;
        }

        var lastService = cyclePath[cyclePath.Count - 1];
        var cycleStart = -1;
        for (var i = 0; i < cyclePath.Count - 1; i++)
        {
            if (cyclePath[i].Equals(lastService))
            {
                cycleStart = i;
                break;
            }
        }

        if (cycleStart < 0)
        {
            return ImmutableArray<ServiceNodeKey>.Empty;
        }

        return cyclePath
            .Skip(cycleStart)
            .Take(cyclePath.Count - cycleStart - 1)
            .ToImmutableArray();
    }

    private static ServiceRegistration FindCanonicalRegistration(
        ImmutableArray<ServiceNodeKey> cycleMembers,
        ImmutableArray<ServiceRegistration> registrations,
        ServiceRegistration fallback)
    {
        ServiceRegistration? best = null;
        var bestName = string.Empty;

        foreach (var cycleService in cycleMembers)
        {
            var registration = registrations.LastOrDefault(r =>
                r.Order == cycleService.RegistrationOrder &&
                string.Equals(r.FlowKey, cycleService.FlowKey, StringComparison.Ordinal) &&
                Equals(r.Key, cycleService.Key) &&
                r.IsKeyed == cycleService.IsKeyed);
            if (registration is null)
            {
                continue;
            }

            var name = registration.ServiceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (best is null || string.Compare(name, bestName, StringComparison.Ordinal) < 0)
            {
                best = registration;
                bestName = name;
            }
        }

        return best ?? fallback;
    }

    private static bool FindCycleMemberForRegistration(
        ImmutableArray<ServiceNodeKey> cycleMembers,
        ServiceRegistration registration,
        out ServiceNodeKey member)
    {
        foreach (var candidate in cycleMembers)
        {
            if (candidate.RegistrationOrder == registration.Order &&
                string.Equals(candidate.FlowKey, registration.FlowKey, StringComparison.Ordinal) &&
                Equals(candidate.Key, registration.Key) &&
                candidate.IsKeyed == registration.IsKeyed)
            {
                member = candidate;
                return true;
            }
        }

        member = default;
        return false;
    }

    private static string FormatCycleFromRegistration(
        ImmutableArray<ServiceNodeKey> cycleMembers,
        ServiceNodeKey startService)
    {
        if (cycleMembers.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var members = cycleMembers.ToList();
        var startIndex = members.FindIndex(service => HasSameDisplayKey(service, startService));
        if (startIndex > 0)
        {
            members = members.Skip(startIndex)
                .Concat(members.Take(startIndex))
                .ToList();
        }

        members.Add(members[0]);
        return string.Join(" -> ", members.Select(FormatServiceLookupKey));
    }

    private static bool HasSameDisplayKey(ServiceNodeKey left, ServiceNodeKey right) =>
        SymbolEqualityComparer.Default.Equals(left.Type, right.Type) &&
        Equals(left.Key, right.Key) &&
        left.IsKeyed == right.IsKeyed;

    private static string GetCanonicalCycleKey(List<ServiceNodeKey> cyclePath)
    {
        var cycleMembers = GetCycleMembers(cyclePath);
        if (cycleMembers.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var memberNames = cycleMembers
            .Select(service => $"{service.FlowKey ?? string.Empty}|{service.RegistrationOrder}|{GetGlobalLookupKeyDisplayName(service)}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        return string.Join("|", memberNames);
    }

    private static ImmutableDictionary<string, string?> CreateDiagnosticProperties(
        ImmutableArray<ServiceNodeKey> cycleMembers)
    {
        var services = string.Join(";", cycleMembers.Select(GetGlobalLookupKeyDisplayName));
        var keys = string.Join(";", cycleMembers.Select(member =>
            member.IsKeyed
                ? $"{member.Key?.GetType().Name ?? "null"}:{member.Key ?? "null"}"
                : string.Empty));

        return ImmutableDictionary<string, string?>.Empty
            .Add(CycleLengthPropertyName, cycleMembers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Add(CycleServicesPropertyName, services)
            .Add(CycleKeysPropertyName, keys);
    }

    private static string FormatServiceLookupKey(ServiceNodeKey serviceLookupKey) =>
        DependencyResolutionEngine.FormatDependencyName(
            serviceLookupKey.Type,
            serviceLookupKey.Key,
            serviceLookupKey.IsKeyed);

    private static string GetGlobalLookupKeyDisplayName(ServiceNodeKey serviceLookupKey)
    {
        var typeName = DependencyResolutionEngine.GetGlobalTypeDisplayName(serviceLookupKey.Type);
        if (!serviceLookupKey.IsKeyed)
        {
            return typeName;
        }

        var keyValue = serviceLookupKey.Key;
        var keyTypeName = keyValue?.GetType().Name ?? "null";
        return $"{typeName}|key({keyTypeName}):{keyValue ?? "null"}";
    }

    private static INamedTypeSymbol? TryGetClosedImplementationTypeForDependency(
        ITypeSymbol dependencyType,
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType)
    {
        if (!implementationType.IsUnboundGenericType)
        {
            return implementationType;
        }

        if (dependencyType is INamedTypeSymbol { IsUnboundGenericType: true } &&
            serviceType.IsUnboundGenericType)
        {
            return implementationType.OriginalDefinition;
        }

        if (dependencyType is not INamedTypeSymbol namedDependencyType ||
            !namedDependencyType.IsGenericType ||
            namedDependencyType.IsUnboundGenericType ||
            !serviceType.IsUnboundGenericType)
        {
            return null;
        }

        var implementationDefinition = implementationType.OriginalDefinition;
        if (implementationDefinition.Arity != namedDependencyType.Arity)
        {
            return null;
        }

        try
        {
            return implementationDefinition.Construct(namedDependencyType.TypeArguments.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private readonly struct DependencyEdge
    {
        public DependencyEdge(ImmutableArray<RegistrationMatch> matches)
        {
            Matches = matches;
        }

        public ImmutableArray<RegistrationMatch> Matches { get; }
    }

    private readonly struct RegistrationMatch
    {
        public RegistrationMatch(ServiceRegistration registration, INamedTypeSymbol requestedType)
        {
            Registration = registration;
            RequestedType = requestedType;
        }

        public ServiceRegistration Registration { get; }

        public INamedTypeSymbol RequestedType { get; }
    }

    private readonly struct RegistrationSourceItem
    {
        public RegistrationSourceItem(ServiceRegistration registration, SourceOrderKey sortKey)
        {
            Registration = registration;
            SortKey = sortKey;
        }

        public ServiceRegistration Registration { get; }

        public SourceOrderKey SortKey { get; }
    }

    private readonly struct MutationSourceItem
    {
        public MutationSourceItem(OrderedRegistrationMutation mutation, SourceOrderKey sortKey)
        {
            Mutation = mutation;
            SortKey = sortKey;
        }

        public OrderedRegistrationMutation Mutation { get; }

        public SourceOrderKey SortKey { get; }
    }

    private readonly struct SourceOrderKey : IComparable<SourceOrderKey>
    {
        private SourceOrderKey(string path, int line, int column, int sourceStart, int order)
        {
            Path = path;
            Line = line;
            Column = column;
            SourceStart = sourceStart;
            Order = order;
        }

        public string Path { get; }

        public int Line { get; }

        public int Column { get; }

        public int SourceStart { get; }

        public int Order { get; }

        public static SourceOrderKey Create(Location location, int order)
        {
            var lineSpan = location.GetLineSpan();
            var path = lineSpan.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = location.SourceTree?.FilePath ?? string.Empty;
            }

            return new SourceOrderKey(
                path ?? string.Empty,
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character,
                location.SourceSpan.Start,
                order);
        }

        public int CompareTo(SourceOrderKey other)
        {
            var pathComparison = string.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase);
            if (pathComparison != 0)
            {
                return pathComparison;
            }

            var lineComparison = Line.CompareTo(other.Line);
            if (lineComparison != 0)
            {
                return lineComparison;
            }

            var columnComparison = Column.CompareTo(other.Column);
            if (columnComparison != 0)
            {
                return columnComparison;
            }

            var startComparison = SourceStart.CompareTo(other.SourceStart);
            return startComparison != 0
                ? startComparison
                : Order.CompareTo(other.Order);
        }
    }

    private sealed class CycleGraph
    {
        private readonly Compilation _compilation;
        private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _semanticModelsByTree;
        private readonly Dictionary<ServiceRequestKey, List<ServiceRegistration>> _registrationsByService;
        private readonly ImmutableArray<ServiceRegistration> _registrations;

        public CycleGraph(
            Compilation compilation,
            WellKnownTypes? wellKnownTypes,
            ImmutableArray<ServiceRegistration> registrations,
            ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
        {
            _compilation = compilation;
            WellKnownTypes = wellKnownTypes;
            _registrations = registrations;
            _semanticModelsByTree = semanticModelsByTree;
            _registrationsByService = BuildServiceLookup(registrations);
        }

        public WellKnownTypes? WellKnownTypes { get; }

        public RegistrationMatch? FindSingleRegistration(ServiceRequestKey request, bool allowAnyFlow = false)
        {
            var matches = FindAllRegistrations(request, allowAnyFlow);
            return matches.IsDefaultOrEmpty
                ? null
                : matches[matches.Length - 1];
        }

        public ImmutableArray<RegistrationMatch> FindAllRegistrations(
            ServiceRequestKey request,
            bool allowAnyFlow = false)
        {
            var matches = ImmutableArray.CreateBuilder<RegistrationMatch>();
            AddMatches(request, allowAnyFlow, matches);

            if (request.Type.IsGenericType && !request.Type.IsUnboundGenericType)
            {
                var openType = request.Type.ConstructUnboundGenericType();
                AddMatches(
                    new ServiceRequestKey(openType, request.Key, request.IsKeyed, request.FlowKey),
                    allowAnyFlow,
                    matches,
                    requestedType: request.Type);
            }

            return matches.ToImmutable();
        }

        public bool TryGetSemanticModel(SyntaxTree syntaxTree, out SemanticModel semanticModel)
        {
            if (_semanticModelsByTree.TryGetValue(syntaxTree, out semanticModel!))
            {
                return true;
            }

            semanticModel = _compilation.GetSemanticModel(syntaxTree);
            _semanticModelsByTree.TryAdd(syntaxTree, semanticModel);
            return true;
        }

        private void AddMatches(
            ServiceRequestKey request,
            bool allowAnyFlow,
            ImmutableArray<RegistrationMatch>.Builder matches,
            INamedTypeSymbol? requestedType = null)
        {
            requestedType ??= request.Type;
            if (_registrationsByService.TryGetValue(request, out var exactRegistrations))
            {
                foreach (var registration in exactRegistrations)
                {
                    matches.Add(new RegistrationMatch(registration, requestedType));
                }
            }

            foreach (var registration in _registrations)
            {
                if (!allowAnyFlow &&
                    !string.Equals(registration.FlowKey, request.FlowKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (allowAnyFlow &&
                    request.FlowKey is not null &&
                    !string.Equals(registration.FlowKey, request.FlowKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (registration.IsKeyed != request.IsKeyed ||
                    !IsMatchingKey(registration.Key, request.Key) ||
                    !SymbolEqualityComparer.Default.Equals(registration.ServiceType, request.Type))
                {
                    continue;
                }

                if (matches.Any(existing => ReferenceEquals(existing.Registration, registration)))
                {
                    continue;
                }

                matches.Add(new RegistrationMatch(registration, requestedType));
            }
        }

        private static Dictionary<ServiceRequestKey, List<ServiceRegistration>> BuildServiceLookup(
            ImmutableArray<ServiceRegistration> registrations)
        {
            var lookup = new Dictionary<ServiceRequestKey, List<ServiceRegistration>>();
            foreach (var registration in registrations)
            {
                var key = new ServiceRequestKey(
                    NormalizeServiceTypeForPath(registration.ServiceType),
                    registration.Key,
                    registration.IsKeyed,
                    registration.FlowKey);
                if (!lookup.TryGetValue(key, out var list))
                {
                    list = new List<ServiceRegistration>();
                    lookup[key] = list;
                }

                list.Add(registration);
            }

            return lookup;
        }

        private static bool IsMatchingKey(object? registrationKey, object? requestKey)
        {
            if (Equals(registrationKey, requestKey))
            {
                return true;
            }

            return !SyntaxValueHelpers.IsKeyedServiceAnyKey(requestKey) &&
                   SyntaxValueHelpers.IsKeyedServiceAnyKey(registrationKey);
        }
    }

    private readonly struct ServiceRequestKey : IEquatable<ServiceRequestKey>
    {
        public ServiceRequestKey(INamedTypeSymbol type, object? key, bool isKeyed, string? flowKey)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
            FlowKey = flowKey;
        }

        public INamedTypeSymbol Type { get; }

        public object? Key { get; }

        public bool IsKeyed { get; }

        public string? FlowKey { get; }

        public bool Equals(ServiceRequestKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed &&
                   string.Equals(FlowKey, other.FlowKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is ServiceRequestKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Type);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                hashCode = (hashCode * 397) ^ (FlowKey?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }

    private readonly struct ServiceNodeKey : IEquatable<ServiceNodeKey>
    {
        public ServiceNodeKey(
            INamedTypeSymbol type,
            object? key,
            bool isKeyed,
            string? flowKey,
            int registrationOrder)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
            FlowKey = flowKey;
            RegistrationOrder = registrationOrder;
        }

        public INamedTypeSymbol Type { get; }

        public object? Key { get; }

        public bool IsKeyed { get; }

        public string? FlowKey { get; }

        public int RegistrationOrder { get; }

        public bool Equals(ServiceNodeKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed &&
                   string.Equals(FlowKey, other.FlowKey, StringComparison.Ordinal) &&
                   RegistrationOrder == other.RegistrationOrder;
        }

        public override bool Equals(object? obj) => obj is ServiceNodeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Type);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                hashCode = (hashCode * 397) ^ (FlowKey?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ RegistrationOrder;
                return hashCode;
            }
        }
    }
}
