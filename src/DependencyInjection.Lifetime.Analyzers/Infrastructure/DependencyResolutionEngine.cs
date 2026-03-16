using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Resolves DI dependency graphs conservatively and reports only high-confidence missing paths.
/// </summary>
internal sealed class DependencyResolutionEngine
{
    private readonly struct ServiceLookupKey : IEquatable<ServiceLookupKey>
    {
        public ServiceLookupKey(ITypeSymbol type, object? key, bool isKeyed)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
        }

        public ITypeSymbol Type { get; }

        public object? Key { get; }

        public bool IsKeyed { get; }

        public bool Equals(ServiceLookupKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed;
        }

        public override bool Equals(object? obj)
        {
            return obj is ServiceLookupKey other && Equals(other);
        }

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

    private readonly RegistrationCollector _registrationCollector;
    private readonly WellKnownTypes? _wellKnownTypes;

    public DependencyResolutionEngine(
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes)
    {
        _registrationCollector = registrationCollector;
        _wellKnownTypes = wellKnownTypes;
    }

    public ResolutionResult ResolveRegistration(
        ServiceRegistration registration,
        bool assumeFrameworkServicesRegistered)
    {
        if (registration.HasImplementationInstance)
        {
            return ResolutionResult.Resolvable(ResolutionConfidence.High);
        }

        if (registration.ImplementationType is null ||
            !IsServiceImplementationCompatible(registration.ServiceType, registration.ImplementationType))
        {
            return ResolutionResult.Resolvable(ResolutionConfidence.High);
        }

        var resolutionCache = new Dictionary<ServiceLookupKey, ResolutionResult>();
        var resolutionPath = new HashSet<ServiceLookupKey>
        {
            new(registration.ServiceType, registration.Key, registration.IsKeyed)
        };

        return ResolveImplementationType(
            registration.ImplementationType,
            assumeFrameworkServicesRegistered,
            resolutionCache,
            resolutionPath);
    }

    public ResolutionResult ResolveFactoryRequest(
        ServiceRegistration registration,
        DependencyRequest request,
        bool assumeFrameworkServicesRegistered)
    {
        var resolutionCache = new Dictionary<ServiceLookupKey, ResolutionResult>();
        var resolutionPath = new HashSet<ServiceLookupKey>
        {
            new(registration.ServiceType, registration.Key, registration.IsKeyed)
        };

        return request.SourceKind == DependencySourceKind.ActivatorUtilitiesConstruction &&
               request.Type is INamedTypeSymbol implementationType
            ? ResolveImplementationType(
                implementationType,
                assumeFrameworkServicesRegistered,
                resolutionCache,
                resolutionPath)
            : ResolveDependency(
                request,
                assumeFrameworkServicesRegistered,
                resolutionCache,
                resolutionPath);
    }

    internal static string FormatDependencyName(ITypeSymbol dependencyType, object? key, bool isKeyed)
    {
        var typeName = dependencyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (!isKeyed)
        {
            return typeName;
        }

        return $"{typeName} (key: {key ?? "null"})";
    }

    internal static string GetGlobalTypeDisplayName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal static bool CanSelfBind(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (namedType.TypeKind is TypeKind.Interface or TypeKind.TypeParameter ||
            namedType.TypeKind == TypeKind.Delegate ||
            namedType.IsAbstract ||
            namedType.IsUnboundGenericType ||
            namedType.IsGenericType && namedType.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            return false;
        }

        if (namedType.TypeKind == TypeKind.Struct)
        {
            return true;
        }

        if (namedType.TypeKind != TypeKind.Class)
        {
            return false;
        }

        return namedType.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public);
    }

    private ResolutionResult ResolveImplementationType(
        INamedTypeSymbol implementationType,
        bool assumeFrameworkServicesRegistered,
        Dictionary<ServiceLookupKey, ResolutionResult> resolutionCache,
        HashSet<ServiceLookupKey> resolutionPath)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType).ToArray();
        if (constructors.Length == 0)
        {
            return ResolutionResult.Resolvable(ResolutionConfidence.High);
        }

        ImmutableArray<MissingDependency> bestMissingDependencies = ImmutableArray<MissingDependency>.Empty;
        var bestMissingCount = int.MaxValue;
        var bestParameterCount = -1;

        foreach (var constructor in constructors)
        {
            var missingDependencies = ImmutableArray.CreateBuilder<MissingDependency>();

            foreach (var parameter in constructor.Parameters)
            {
                if (ShouldSkipDependencyCheck(
                        parameter.Type,
                        parameter,
                        assumeFrameworkServicesRegistered))
                {
                    continue;
                }

                var (key, isKeyed) = GetServiceKey(parameter);
                var request = new DependencyRequest(
                    parameter.Type,
                    key,
                    isKeyed,
                    DependencySourceKind.ConstructorParameter,
                    parameter.Locations.FirstOrDefault() ?? Location.None,
                    FormatDependencyName(parameter.Type, key, isKeyed));

                var result = ResolveDependency(
                    request,
                    assumeFrameworkServicesRegistered,
                    resolutionCache,
                    resolutionPath);
                if (result.IsResolvable)
                {
                    continue;
                }

                foreach (var missingDependency in result.MissingDependencies)
                {
                    if (missingDependencies.Any(existing => Matches(existing, missingDependency)))
                    {
                        continue;
                    }

                    missingDependencies.Add(missingDependency);
                }
            }

            if (missingDependencies.Count == 0)
            {
                return ResolutionResult.Resolvable(ResolutionConfidence.High);
            }

            if (missingDependencies.Count < bestMissingCount ||
                (missingDependencies.Count == bestMissingCount &&
                 constructor.Parameters.Length > bestParameterCount))
            {
                bestMissingDependencies = missingDependencies.ToImmutable();
                bestMissingCount = missingDependencies.Count;
                bestParameterCount = constructor.Parameters.Length;
            }
        }

        return ResolutionResult.Missing(bestMissingDependencies);
    }

    private ResolutionResult ResolveDependency(
        DependencyRequest request,
        bool assumeFrameworkServicesRegistered,
        Dictionary<ServiceLookupKey, ResolutionResult> resolutionCache,
        HashSet<ServiceLookupKey> resolutionPath)
    {
        if (ShouldSkipDependencyCheck(request.Type, parameter: null, assumeFrameworkServicesRegistered))
        {
            return ResolutionResult.Resolvable(ResolutionConfidence.High);
        }

        var lookupKey = new ServiceLookupKey(request.Type, request.Key, request.IsKeyed);
        if (resolutionCache.TryGetValue(lookupKey, out var cachedResult))
        {
            return cachedResult;
        }

        if (!resolutionPath.Add(lookupKey))
        {
            var cycleResult = ResolutionResult.Resolvable(ResolutionConfidence.Unknown);
            resolutionCache[lookupKey] = cycleResult;
            return cycleResult;
        }

        var candidates = GetCandidateRegistrations(request.Type, request.Key, request.IsKeyed).ToArray();
        ImmutableArray<MissingDependency> bestMissingDependencies = ImmutableArray<MissingDependency>.Empty;
        var bestMissingCount = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.FactoryExpression is not null)
            {
                resolutionPath.Remove(lookupKey);
                var unknownFactoryResult = ResolutionResult.Resolvable(ResolutionConfidence.Unknown);
                resolutionCache[lookupKey] = unknownFactoryResult;
                return unknownFactoryResult;
            }

            if (candidate.HasImplementationInstance)
            {
                resolutionPath.Remove(lookupKey);
                var instanceResult = ResolutionResult.Resolvable(ResolutionConfidence.High);
                resolutionCache[lookupKey] = instanceResult;
                return instanceResult;
            }

            if (candidate.ImplementationType is null)
            {
                continue;
            }

            var implementationType = TryGetClosedImplementationTypeForDependency(
                request.Type,
                candidate.ServiceType,
                candidate.ImplementationType);
            if (implementationType is null)
            {
                continue;
            }

            var candidateResult = ResolveImplementationType(
                implementationType,
                assumeFrameworkServicesRegistered,
                resolutionCache,
                resolutionPath);
            if (candidateResult.IsResolvable)
            {
                resolutionPath.Remove(lookupKey);
                resolutionCache[lookupKey] = candidateResult;
                return candidateResult;
            }

            var prefixedMissingDependencies = candidateResult.MissingDependencies
                .Select(missingDependency => missingDependency.PrependProvenance(request.ProvenanceStep))
                .ToImmutableArray();

            if (prefixedMissingDependencies.Length < bestMissingCount)
            {
                bestMissingDependencies = prefixedMissingDependencies;
                bestMissingCount = prefixedMissingDependencies.Length;
            }
        }

        resolutionPath.Remove(lookupKey);

        if (bestMissingDependencies.IsDefaultOrEmpty)
        {
            var directMissingResult = ResolutionResult.Missing(
                ImmutableArray.Create(MissingDependency.CreateDirect(request)));
            resolutionCache[lookupKey] = directMissingResult;
            return directMissingResult;
        }

        var missingResult = ResolutionResult.Missing(bestMissingDependencies);
        resolutionCache[lookupKey] = missingResult;
        return missingResult;
    }

    private IEnumerable<ServiceRegistration> GetCandidateRegistrations(
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        foreach (var registration in _registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, dependencyType))
            {
                yield return registration;
                continue;
            }

            if (dependencyType is not INamedTypeSymbol namedDependencyType ||
                !namedDependencyType.IsGenericType ||
                namedDependencyType.IsUnboundGenericType)
            {
                continue;
            }

            var openDependencyType = namedDependencyType.ConstructUnboundGenericType();
            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, openDependencyType))
            {
                yield return registration;
            }
        }
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

    private bool ShouldSkipDependencyCheck(
        ITypeSymbol dependencyType,
        IParameterSymbol? parameter,
        bool assumeFrameworkServicesRegistered)
    {
        if (parameter?.HasExplicitDefaultValue == true)
        {
            return true;
        }

        if (_wellKnownTypes is not null &&
            _wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(dependencyType))
        {
            return true;
        }

        if (IsContainerProvidedDependency(dependencyType))
        {
            return true;
        }

        return assumeFrameworkServicesRegistered && IsFrameworkProvidedDependency(dependencyType);
    }

    private static bool IsContainerProvidedDependency(ITypeSymbol dependencyType)
    {
        if (dependencyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return namedType.Name == "IEnumerable" &&
               namedType.IsGenericType &&
               namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
    }

    private static bool IsFrameworkProvidedDependency(ITypeSymbol dependencyType)
    {
        if (dependencyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();

        if (namedType.Name == "IConfiguration" &&
            namespaceName == "Microsoft.Extensions.Configuration")
        {
            return true;
        }

        if (namedType.Name == "ILoggerFactory" &&
            namespaceName == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.Name is "IHostEnvironment" or "IWebHostEnvironment" &&
            (namespaceName == "Microsoft.Extensions.Hosting" ||
             namespaceName == "Microsoft.AspNetCore.Hosting"))
        {
            return true;
        }

        if (namedType.Name == "ILogger" &&
            namespaceName == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.IsGenericType &&
            namedType.ConstructedFrom.Name == "ILogger" &&
            namedType.ConstructedFrom.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.Name is "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor" &&
            namespaceName == "Microsoft.Extensions.Options")
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

    private static bool IsServiceImplementationCompatible(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType)
    {
        if (SymbolEqualityComparer.Default.Equals(serviceType, implementationType))
        {
            return true;
        }

        if (serviceType.IsUnboundGenericType)
        {
            if (!implementationType.IsGenericType)
            {
                return false;
            }

            var originalService = serviceType.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(implementationType.OriginalDefinition, originalService))
            {
                return true;
            }

            foreach (var iface in implementationType.OriginalDefinition.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, originalService))
                {
                    return true;
                }
            }

            var currentBaseType = implementationType.OriginalDefinition.BaseType;
            while (currentBaseType is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(currentBaseType.OriginalDefinition, originalService))
                {
                    return true;
                }

                currentBaseType = currentBaseType.BaseType;
            }

            return false;
        }

        foreach (var iface in implementationType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, serviceType))
            {
                return true;
            }
        }

        var baseType = implementationType.BaseType;
        while (baseType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, serviceType))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool Matches(MissingDependency left, MissingDependency right)
    {
        return SymbolEqualityComparer.Default.Equals(left.Type, right.Type) &&
               Equals(left.Key, right.Key) &&
               left.IsKeyed == right.IsKeyed;
    }
}
