using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Finds scoped registrations reachable from a requested service activation graph.
/// </summary>
internal sealed class ScopedDependencyGraph
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

        public override bool Equals(object? obj) =>
            obj is ServiceLookupKey other && Equals(other);

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

    public readonly struct ScopedDependencyMatch
    {
        public ScopedDependencyMatch(ITypeSymbol requestedType, ITypeSymbol scopedType, object? key, bool isKeyed)
            : this(requestedType, scopedType, key, isKeyed, ImmutableArray<ITypeSymbol>.Empty)
        {
        }

        private ScopedDependencyMatch(
            ITypeSymbol requestedType,
            ITypeSymbol scopedType,
            object? key,
            bool isKeyed,
            ImmutableArray<ITypeSymbol> path)
        {
            RequestedType = requestedType;
            ScopedType = scopedType;
            Key = key;
            IsKeyed = isKeyed;
            Path = path.IsDefault ? ImmutableArray<ITypeSymbol>.Empty : path;
        }

        public ITypeSymbol RequestedType { get; }

        public ITypeSymbol ScopedType { get; }

        public object? Key { get; }

        public bool IsKeyed { get; }

        /// <summary>
        /// The activation path from the requested service to the scoped service, ordered
        /// root-first (requested service) to leaf-last (the scoped service). Each node is a
        /// service type the container resolves on the way to the captured scoped dependency.
        /// </summary>
        public ImmutableArray<ITypeSymbol> Path { get; }

        /// <summary>
        /// Returns a copy of this match with <paramref name="node"/> prepended to the resolution
        /// path. As the depth-first search unwinds, each enclosing requested type prepends itself,
        /// reconstructing the full root-to-scoped chain without threading extra state through the walk.
        /// </summary>
        internal ScopedDependencyMatch PrependToPath(ITypeSymbol node) =>
            new(RequestedType, ScopedType, Key, IsKeyed, Path.Insert(0, node));
    }

    private readonly RegistrationCollector _registrationCollector;
    private readonly WellKnownTypes? _wellKnownTypes;
    private readonly KnownServiceLifetimeClassifier _lifetimeClassifier;
    private readonly Compilation _compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = new();
    private readonly Dictionary<ServiceLookupKey, ScopedDependencyMatch?> _cache = new();

    public ScopedDependencyGraph(
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        Compilation compilation)
    {
        _registrationCollector = registrationCollector;
        _wellKnownTypes = wellKnownTypes;
        _lifetimeClassifier = new KnownServiceLifetimeClassifier(wellKnownTypes);
        _compilation = compilation;
    }

    public bool TryFindScopedDependency(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed,
        bool isEnumerableRequest,
        out ScopedDependencyMatch match)
    {
        var visited = new HashSet<ServiceLookupKey>();
        if (isEnumerableRequest)
        {
            return TryFindScopedDependencyInEnumerable(
                requestedType,
                key,
                isKeyed,
                visited,
                out match);
        }

        return TryFindScopedDependency(
            requestedType,
            key,
            isKeyed,
            visited,
            out match);
    }

    private bool TryFindScopedDependencyInEnumerable(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed,
        HashSet<ServiceLookupKey> visited,
        out ScopedDependencyMatch match)
    {
        if (TryCreateKnownScopedMatch(requestedType, key, isKeyed, out match))
        {
            match = match.PrependToPath(requestedType);
            return true;
        }

        foreach (var registration in GetAllMatchingRegistrations(requestedType, key, isKeyed))
        {
            if (TryFindScopedDependencyInRegistration(
                    requestedType,
                    registration,
                    visited,
                    out match))
            {
                match = match.PrependToPath(requestedType);
                return true;
            }
        }

        match = default;
        return false;
    }

    private bool TryFindScopedDependency(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed,
        HashSet<ServiceLookupKey> visited,
        out ScopedDependencyMatch match)
    {
        if (TryCreateKnownScopedMatch(requestedType, key, isKeyed, out match))
        {
            match = match.PrependToPath(requestedType);
            return true;
        }

        var lookupKey = new ServiceLookupKey(requestedType, key, isKeyed);
        if (_cache.TryGetValue(lookupKey, out var cached))
        {
            // Cached matches already include their own node at the head of the path, so the
            // suffix is reusable regardless of which ancestor route reached this node.
            match = cached.GetValueOrDefault();
            return cached.HasValue;
        }

        if (!visited.Add(lookupKey))
        {
            match = default;
            return false;
        }

        foreach (var registration in GetEffectiveMatchingRegistrations(requestedType, key, isKeyed))
        {
            if (TryFindScopedDependencyInRegistration(
                    requestedType,
                    registration,
                    visited,
                    out match))
            {
                visited.Remove(lookupKey);
                match = match.PrependToPath(requestedType);
                _cache[lookupKey] = match;
                return true;
            }
        }

        visited.Remove(lookupKey);
        _cache[lookupKey] = null;
        match = default;
        return false;
    }

    private bool TryFindScopedDependencyInRegistration(
        ITypeSymbol requestedType,
        ServiceRegistration registration,
        HashSet<ServiceLookupKey> visited,
        out ScopedDependencyMatch match)
    {
        if (registration.Lifetime == ServiceLifetime.Scoped)
        {
            match = new ScopedDependencyMatch(
                requestedType,
                registration.ServiceType,
                registration.Key,
                registration.IsKeyed);
            return true;
        }

        if (registration.HasImplementationInstance)
        {
            match = default;
            return false;
        }

        if (registration.FactoryExpression is not null)
        {
            return TryFindScopedDependencyInFactory(
                registration,
                visited,
                out match);
        }

        if (registration.ImplementationType is null)
        {
            match = default;
            return false;
        }

        var implementationType = TryGetClosedImplementationTypeForDependency(
            requestedType,
            registration.ServiceType,
            registration.ImplementationType);
        if (implementationType is null)
        {
            match = default;
            return false;
        }

        return TryFindScopedDependencyInImplementation(
            implementationType,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null,
            visited,
            out match);
    }

    private bool TryFindScopedDependencyInFactory(
        ServiceRegistration registration,
        HashSet<ServiceLookupKey> visited,
        out ScopedDependencyMatch match)
    {
        if (registration.FactoryExpression is null)
        {
            match = default;
            return false;
        }

        var semanticModel = GetSemanticModel(registration.FactoryExpression.SyntaxTree);
        var requests = FactoryDependencyAnalysis.GetDependencyRequests(
            registration.FactoryExpression,
            semanticModel,
            _wellKnownTypes,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);

        foreach (var request in requests)
        {
            if (request.SourceKind == DependencySourceKind.ActivatorUtilitiesConstruction &&
                request.Type is INamedTypeSymbol implementationType)
            {
                if (TryFindScopedDependencyInImplementation(
                        implementationType,
                        inheritedKey: null,
                        hasInheritedKey: false,
                        inheritedKeyLiteral: null,
                        visited,
                        out match))
                {
                    return true;
                }

                continue;
            }

            if (TryFindScopedDependency(
                    request.Type,
                    request.Key,
                    request.IsKeyed,
                    visited,
                    out match))
            {
                return true;
            }
        }

        match = default;
        return false;
    }

    private bool TryFindScopedDependencyInImplementation(
        INamedTypeSymbol implementationType,
        object? inheritedKey,
        bool hasInheritedKey,
        string? inheritedKeyLiteral,
        HashSet<ServiceLookupKey> visited,
        out ScopedDependencyMatch match)
    {
        foreach (var constructor in ConstructorSelection.GetConstructorsToAnalyze(implementationType))
        {
            foreach (var parameter in constructor.Parameters)
            {
                var dependencyType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerable);
                var serviceKey = GetServiceKey(parameter, inheritedKey, hasInheritedKey, inheritedKeyLiteral);
                if (serviceKey.IsUnknown)
                {
                    continue;
                }

                if (ShouldSkipParameter(
                        parameter,
                        dependencyType,
                        serviceKey.Key,
                        serviceKey.IsKeyed))
                {
                    continue;
                }

                if (isEnumerable)
                {
                    if (TryFindScopedDependencyInEnumerable(
                            dependencyType,
                            serviceKey.Key,
                            serviceKey.IsKeyed,
                            visited,
                            out match))
                    {
                        return true;
                    }

                    continue;
                }

                if (TryFindScopedDependency(
                        dependencyType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        visited,
                        out match))
                {
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    private IEnumerable<ServiceRegistration> GetEffectiveMatchingRegistrations(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed)
    {
        if (requestedType is not INamedTypeSymbol namedType)
        {
            yield break;
        }

        if (_registrationCollector.TryGetRegistration(namedType, key, isKeyed, out var registration) &&
            registration is not null)
        {
            yield return registration;
            yield break;
        }

        if (!namedType.IsGenericType || namedType.IsUnboundGenericType)
        {
            var nonGenericAnyKeyRegistration = GetLatestAnyKeyFallbackRegistration(requestedType, key, isKeyed);
            if (nonGenericAnyKeyRegistration is not null)
            {
                yield return nonGenericAnyKeyRegistration;
            }

            yield break;
        }

        var openGenericType = namedType.ConstructUnboundGenericType();
        if (_registrationCollector.TryGetRegistration(openGenericType, key, isKeyed, out registration) &&
            registration is not null)
        {
            yield return registration;
            yield break;
        }

        var anyKeyRegistration = GetLatestAnyKeyFallbackRegistration(requestedType, key, isKeyed);
        if (anyKeyRegistration is not null)
        {
            yield return anyKeyRegistration;
        }
    }

    private ServiceRegistration? GetLatestAnyKeyFallbackRegistration(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed)
    {
        if (!isKeyed ||
            SyntaxValueHelpers.IsKeyedServiceAnyKey(key))
        {
            return null;
        }

        ServiceRegistration? latest = null;
        foreach (var registration in _registrationCollector.AllRegistrations)
        {
            if (!registration.IsKeyed ||
                !SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key) ||
                !IsMatchingServiceType(registration.ServiceType, requestedType))
            {
                continue;
            }

            if (latest is null || registration.Order > latest.Order)
            {
                latest = registration;
            }
        }

        return latest;
    }

    private IEnumerable<ServiceRegistration> GetAllMatchingRegistrations(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed)
    {
        foreach (var registration in _registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !IsMatchingKey(registration.Key, key))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, requestedType))
            {
                yield return registration;
                continue;
            }

            if (requestedType is not INamedTypeSymbol namedType ||
                !namedType.IsGenericType ||
                namedType.IsUnboundGenericType)
            {
                continue;
            }

            var openGenericType = namedType.ConstructUnboundGenericType();
            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, openGenericType))
            {
                yield return registration;
            }
        }
    }

    private static bool IsMatchingKey(object? registrationKey, object? requestedKey)
    {
        if (Equals(registrationKey, requestedKey))
        {
            return true;
        }

        return !SyntaxValueHelpers.IsKeyedServiceAnyKey(requestedKey) &&
               SyntaxValueHelpers.IsKeyedServiceAnyKey(registrationKey);
    }

    private static bool IsMatchingServiceType(
        INamedTypeSymbol registeredType,
        ITypeSymbol requestedType)
    {
        if (SymbolEqualityComparer.Default.Equals(registeredType, requestedType))
        {
            return true;
        }

        if (requestedType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.IsUnboundGenericType)
        {
            return false;
        }

        var openGenericType = namedType.ConstructUnboundGenericType();
        return SymbolEqualityComparer.Default.Equals(registeredType, openGenericType);
    }

    private bool ShouldSkipParameter(
        IParameterSymbol parameter,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        if (parameter.HasExplicitDefaultValue ||
            KeyedServiceHelpers.IsServiceKeyParameter(parameter))
        {
            return true;
        }

        if (_wellKnownTypes is not null &&
            _wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(parameter.Type))
        {
            return true;
        }

        if (GetEffectiveMatchingRegistrations(dependencyType, key, isKeyed).Any())
        {
            return false;
        }

        if (_lifetimeClassifier.TryGetLifetime(parameter.Type, isKeyed, out var knownLifetime))
        {
            return knownLifetime != ServiceLifetime.Scoped;
        }

        return IsFrameworkProvidedDependency(parameter.Type, isKeyed);
    }

    private bool TryCreateKnownScopedMatch(
        ITypeSymbol requestedType,
        object? key,
        bool isKeyed,
        out ScopedDependencyMatch match)
    {
        if (_lifetimeClassifier.TryGetLifetime(requestedType, isKeyed, out var knownLifetime) &&
            knownLifetime == ServiceLifetime.Scoped)
        {
            match = new ScopedDependencyMatch(requestedType, requestedType, key, isKeyed);
            return true;
        }

        match = default;
        return false;
    }

    private bool IsFrameworkProvidedDependency(ITypeSymbol dependencyType, bool isKeyed)
    {
        return _lifetimeClassifier.TryGetLifetime(
                   dependencyType,
                   isKeyed,
                   out var knownLifetime) &&
               knownLifetime != ServiceLifetime.Scoped;
    }

    private static ITypeSymbol UnwrapEnumerableDependency(ITypeSymbol type, out bool isEnumerable)
    {
        isEnumerable = false;
        if (type is INamedTypeSymbol
            {
                Name: "IEnumerable",
                IsGenericType: true
            } namedType &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
        {
            isEnumerable = true;
            return namedType.TypeArguments[0];
        }

        return type;
    }

    private static (object? Key, bool IsKeyed, string? KeyLiteral, bool IsUnknown) GetServiceKey(
        IParameterSymbol parameter,
        object? inheritedKey,
        bool hasInheritedKey,
        string? inheritedKeyLiteral)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var key = attribute.ConstructorArguments[0].Value;
                    return (key, true, SyntaxValueHelpers.TryFormatCSharpLiteral(key, out var literal) ? literal : null, false);
                }

                if (!hasInheritedKey)
                {
                    return (null, true, null, true);
                }

                return (inheritedKey, true, inheritedKeyLiteral, false);
            }
        }

        return (null, false, null, false);
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

    private SemanticModel GetSemanticModel(SyntaxTree tree)
    {
        if (_semanticModels.TryGetValue(tree, out var semanticModel))
        {
            return semanticModel;
        }

        semanticModel = _compilation.GetSemanticModel(tree);
        _semanticModels[tree] = semanticModel;
        return semanticModel;
    }
}
