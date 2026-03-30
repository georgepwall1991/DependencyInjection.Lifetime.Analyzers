using System;
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
/// Analyzer that detects circular dependencies in constructor injection chains.
/// Only reports high-confidence cycles where all nodes are non-factory registered types.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI017_CircularDependencyAnalyzer : DiagnosticAnalyzer
{
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

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeCycles(endContext, registrationCollector, wellKnownTypes));
        });
    }

    private static void AnalyzeCycles(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes)
    {
        var registrations = registrationCollector.AllRegistrations.ToList();
        if (registrations.Count == 0)
        {
            return;
        }

        // Build a keyed lookup of (service type, key, isKeyed) -> registrations
        var registrationsByService = BuildServiceLookup(registrations);

        // Track reported cycles to avoid duplicates
        var reportedCycles = new HashSet<string>();
        // Memoize nodes known to be cycle-free to avoid re-exploring
        var knownNoCycle = new HashSet<ServiceLookupKey>();

        foreach (var registration in registrations)
        {
            if (registration.ImplementationType is null || registration.FactoryExpression is not null)
            {
                continue;
            }

            var rootKey = new ServiceLookupKey(registration.ServiceType, registration.Key, registration.IsKeyed);
            if (knownNoCycle.Contains(rootKey))
            {
                continue;
            }

            var cyclePath = new List<INamedTypeSymbol>();
            var visiting = new HashSet<ServiceLookupKey>();

            if (DetectCycle(
                    rootKey,
                    registration.ImplementationType,
                    registrationsByService,
                    wellKnownTypes,
                    visiting,
                    cyclePath,
                    knownNoCycle))
            {
                var cycleKey = GetCanonicalCycleKey(cyclePath);
                if (!reportedCycles.Add(cycleKey))
                {
                    continue;
                }

                // Report on the canonical (lexicographically smallest) registration in the cycle
                var canonicalRegistration = FindCanonicalRegistration(cyclePath, registrations, registration);

                var cycleDescription = FormatCycleFromRegistration(cyclePath, canonicalRegistration.ServiceType);

                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.CircularDependency,
                    canonicalRegistration.Location,
                    canonicalRegistration.ImplementationType?.Name ?? canonicalRegistration.ServiceType.Name,
                    cycleDescription);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool DetectCycle(
        ServiceLookupKey serviceKey,
        INamedTypeSymbol implementationType,
        Dictionary<ServiceLookupKey, List<ServiceRegistration>> registrationsByService,
        WellKnownTypes? wellKnownTypes,
        HashSet<ServiceLookupKey> visiting,
        List<INamedTypeSymbol> cyclePath,
        HashSet<ServiceLookupKey> knownNoCycle)
    {
        cyclePath.Add(serviceKey.Type);

        if (!visiting.Add(serviceKey))
        {
            // Found cycle — cyclePath contains the full cycle
            return true;
        }

        if (knownNoCycle.Contains(serviceKey))
        {
            visiting.Remove(serviceKey);
            cyclePath.RemoveAt(cyclePath.Count - 1);
            return false;
        }

        // Walk constructors that DI would consider (respects [ActivatorUtilitiesConstructor]).
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);

        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (ShouldSkipParameter(parameter, wellKnownTypes))
                {
                    continue;
                }

                if (parameter.Type is not INamedTypeSymbol depType)
                {
                    continue;
                }

                // Extract keyed service info from parameter attributes
                var (key, isKeyed) = GetServiceKey(parameter);

                // Look up registration for this dependency (keyed-aware)
                var depLookupKey = new ServiceLookupKey(depType, key, isKeyed);
                var depRegistration = FindRegistration(depLookupKey, registrationsByService);
                if (depRegistration is null)
                {
                    // Not registered — not a cycle concern (DI015 handles this)
                    continue;
                }

                if (depRegistration.FactoryExpression is not null)
                {
                    // Factory makes this node opaque — conservatively skip
                    continue;
                }

                if (depRegistration.ImplementationType is null)
                {
                    continue;
                }

                var depServiceKey = new ServiceLookupKey(
                    depRegistration.ServiceType, depRegistration.Key, depRegistration.IsKeyed);

                if (DetectCycle(
                        depServiceKey,
                        depRegistration.ImplementationType,
                        registrationsByService,
                        wellKnownTypes,
                        visiting,
                        cyclePath,
                        knownNoCycle))
                {
                    return true;
                }
            }
        }

        visiting.Remove(serviceKey);
        cyclePath.RemoveAt(cyclePath.Count - 1);
        knownNoCycle.Add(serviceKey);
        return false;
    }

    private static bool ShouldSkipParameter(IParameterSymbol parameter, WellKnownTypes? wellKnownTypes)
    {
        // Skip optional parameters — they break cycles
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        var type = parameter.Type;

        // Skip IEnumerable<T> — always resolvable (empty collection)
        if (type is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        // Skip value types, primitives
        if (type.IsValueType || type.SpecialType != SpecialType.None)
        {
            return true;
        }

        // Skip IServiceProvider, IServiceScopeFactory, etc.
        if (wellKnownTypes is not null && wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            return true;
        }

        return false;
    }

    private static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return (attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value
                    : null, true);
            }
        }

        return (null, false);
    }

    private static ServiceRegistration? FindRegistration(
        ServiceLookupKey lookupKey,
        Dictionary<ServiceLookupKey, List<ServiceRegistration>> registrationsByService)
    {
        if (registrationsByService.TryGetValue(lookupKey, out var registrations))
        {
            // Return the last registration (which is what the DI container uses)
            return registrations[registrations.Count - 1];
        }

        // Try open generic match
        if (lookupKey.Type.IsGenericType && !lookupKey.Type.IsUnboundGenericType)
        {
            var openType = lookupKey.Type.ConstructUnboundGenericType();
            var openKey = new ServiceLookupKey(openType, lookupKey.Key, lookupKey.IsKeyed);
            if (registrationsByService.TryGetValue(openKey, out var openRegistrations))
            {
                return openRegistrations[openRegistrations.Count - 1];
            }
        }

        return null;
    }

    private static Dictionary<ServiceLookupKey, List<ServiceRegistration>> BuildServiceLookup(
        List<ServiceRegistration> registrations)
    {
        var lookup = new Dictionary<ServiceLookupKey, List<ServiceRegistration>>();

        foreach (var registration in registrations)
        {
            var key = new ServiceLookupKey(registration.ServiceType, registration.Key, registration.IsKeyed);
            if (!lookup.TryGetValue(key, out var list))
            {
                list = new List<ServiceRegistration>();
                lookup[key] = list;
            }

            list.Add(registration);
        }

        return lookup;
    }

    /// <summary>
    /// Finds the registration in the cycle with the lexicographically smallest
    /// service type name, ensuring deterministic reporting order.
    /// </summary>
    private static ServiceRegistration FindCanonicalRegistration(
        List<INamedTypeSymbol> cyclePath,
        List<ServiceRegistration> allRegistrations,
        ServiceRegistration fallback)
    {
        if (cyclePath.Count < 2)
        {
            return fallback;
        }

        // Extract the cycle members (excluding the repeated tail element)
        var lastType = cyclePath[cyclePath.Count - 1];
        var cycleStart = -1;
        for (var i = 0; i < cyclePath.Count - 1; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(cyclePath[i], lastType))
            {
                cycleStart = i;
                break;
            }
        }

        if (cycleStart < 0)
        {
            return fallback;
        }

        // Find the registration with the smallest service type name in the cycle
        ServiceRegistration? best = null;
        var bestName = string.Empty;

        for (var i = cycleStart; i < cyclePath.Count - 1; i++)
        {
            var cycleType = cyclePath[i];
            var registration = allRegistrations.LastOrDefault(r =>
                SymbolEqualityComparer.Default.Equals(r.ServiceType, cycleType) &&
                r.ImplementationType is not null &&
                r.FactoryExpression is null);

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

    /// <summary>
    /// Formats the cycle description starting from the given service type.
    /// </summary>
    private static string FormatCycleFromRegistration(
        List<INamedTypeSymbol> cyclePath,
        INamedTypeSymbol startType)
    {
        if (cyclePath.Count < 2)
        {
            return string.Empty;
        }

        // Find the start of the cycle portion
        var lastType = cyclePath[cyclePath.Count - 1];
        var cycleStart = -1;
        for (var i = 0; i < cyclePath.Count - 1; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(cyclePath[i], lastType))
            {
                cycleStart = i;
                break;
            }
        }

        if (cycleStart < 0)
        {
            return string.Join(" -> ",
                cyclePath.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        // Extract cycle members
        var cycleMembers = new List<INamedTypeSymbol>();
        for (var i = cycleStart; i < cyclePath.Count - 1; i++)
        {
            cycleMembers.Add(cyclePath[i]);
        }

        // Rotate so the canonical start type is first
        var startIndex = cycleMembers.FindIndex(t =>
            SymbolEqualityComparer.Default.Equals(t, startType));
        if (startIndex > 0)
        {
            var rotated = cycleMembers.Skip(startIndex)
                .Concat(cycleMembers.Take(startIndex))
                .ToList();
            cycleMembers = rotated;
        }

        // Add the closing element to show the cycle
        cycleMembers.Add(cycleMembers[0]);

        return string.Join(" -> ",
            cycleMembers.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static string GetCanonicalCycleKey(List<INamedTypeSymbol> cyclePath)
    {
        // Find the cycle portion (from first repeated element to end)
        if (cyclePath.Count < 2)
        {
            return string.Empty;
        }

        var lastType = cyclePath[cyclePath.Count - 1];
        var cycleStart = -1;
        for (var i = 0; i < cyclePath.Count - 1; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(cyclePath[i], lastType))
            {
                cycleStart = i;
                break;
            }
        }

        if (cycleStart < 0)
        {
            return string.Empty;
        }

        // Extract cycle members and sort for canonical form
        var cycleMembers = new List<string>();
        for (var i = cycleStart; i < cyclePath.Count - 1; i++)
        {
            cycleMembers.Add(DependencyResolutionEngine.GetGlobalTypeDisplayName(cyclePath[i]));
        }

        cycleMembers.Sort();
        return string.Join("|", cycleMembers);
    }

    /// <summary>
    /// Composite key for service lookup that includes type, key, and keyed status.
    /// </summary>
    private readonly struct ServiceLookupKey : IEquatable<ServiceLookupKey>
    {
        public INamedTypeSymbol Type { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }

        public ServiceLookupKey(INamedTypeSymbol type, object? key, bool isKeyed)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
        }

        public bool Equals(ServiceLookupKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed;
        }

        public override bool Equals(object? obj) => obj is ServiceLookupKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Type);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                return hashCode;
            }
        }
    }
}
