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

        // Build a lookup of service type -> registrations for efficient resolution
        var registrationsByService = BuildServiceLookup(registrations);

        // Track reported cycles to avoid duplicates
        var reportedCycles = new HashSet<string>();
        // Memoize nodes known to be cycle-free to avoid re-exploring
        var knownNoCycle = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            if (registration.ImplementationType is null || registration.FactoryExpression is not null)
            {
                continue;
            }

            if (knownNoCycle.Contains(registration.ServiceType))
            {
                continue;
            }

            var cyclePath = new List<INamedTypeSymbol>();
            var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            if (DetectCycle(
                    registration.ServiceType,
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

                var cycleDescription = string.Join(" -> ",
                    cyclePath.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.CircularDependency,
                    registration.Location,
                    registration.ImplementationType.Name,
                    cycleDescription);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool DetectCycle(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType,
        Dictionary<INamedTypeSymbol, List<ServiceRegistration>> registrationsByService,
        WellKnownTypes? wellKnownTypes,
        HashSet<INamedTypeSymbol> visiting,
        List<INamedTypeSymbol> cyclePath,
        HashSet<INamedTypeSymbol> knownNoCycle)
    {
        cyclePath.Add(serviceType);

        if (!visiting.Add(serviceType))
        {
            // Found cycle — cyclePath contains the full cycle
            return true;
        }

        if (knownNoCycle.Contains(serviceType))
        {
            visiting.Remove(serviceType);
            cyclePath.RemoveAt(cyclePath.Count - 1);
            return false;
        }

        // Walk constructors that DI would consider (respects [ActivatorUtilitiesConstructor]).
        // Report a cycle if ANY constructor path leads to a cycle back to a visiting node.
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

                // Look up registration for this dependency
                var depRegistration = FindRegistration(depType, registrationsByService);
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

                if (DetectCycle(
                        depRegistration.ServiceType,
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

        visiting.Remove(serviceType);
        cyclePath.RemoveAt(cyclePath.Count - 1);
        knownNoCycle.Add(serviceType);
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

    private static ServiceRegistration? FindRegistration(
        INamedTypeSymbol serviceType,
        Dictionary<INamedTypeSymbol, List<ServiceRegistration>> registrationsByService)
    {
        if (registrationsByService.TryGetValue(serviceType, out var registrations))
        {
            // Return the last registration (which is what the DI container uses)
            return registrations[registrations.Count - 1];
        }

        // Try open generic match
        if (serviceType.IsGenericType && !serviceType.IsUnboundGenericType)
        {
            var openType = serviceType.ConstructUnboundGenericType();
            if (registrationsByService.TryGetValue(openType, out var openRegistrations))
            {
                return openRegistrations[openRegistrations.Count - 1];
            }
        }

        return null;
    }

    private static Dictionary<INamedTypeSymbol, List<ServiceRegistration>> BuildServiceLookup(
        List<ServiceRegistration> registrations)
    {
        var lookup = new Dictionary<INamedTypeSymbol, List<ServiceRegistration>>(
            SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            if (!lookup.TryGetValue(registration.ServiceType, out var list))
            {
                list = new List<ServiceRegistration>();
                lookup[registration.ServiceType] = list;
            }

            list.Add(registration);
        }

        return lookup;
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
}
