using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Collects service registrations from IServiceCollection extension method calls.
/// </summary>
public sealed class RegistrationCollector
{
    private readonly ConcurrentDictionary<ServiceIdentifier, ServiceRegistration> _registrations;
    private readonly ConcurrentBag<ServiceRegistration> _allRegistrations;
    private readonly ConcurrentBag<OrderedRegistration> _orderedRegistrations;
    private readonly ConcurrentBag<OrderedRegistrationMutation> _orderedMutations;
    private readonly INamedTypeSymbol? _serviceCollectionType;
    private readonly INamedTypeSymbol? _serviceCollectionServiceExtensionsType;
    private readonly INamedTypeSymbol? _serviceCollectionDescriptorExtensionsType;
    private readonly INamedTypeSymbol? _serviceCollectionHostedServiceExtensionsType;
    private readonly INamedTypeSymbol? _entityFrameworkServiceCollectionExtensionsType;
    private readonly INamedTypeSymbol? _serviceDescriptorType;
    private readonly INamedTypeSymbol? _hostedServiceType;
    private readonly INamedTypeSymbol? _dbContextOptionsOfT;
    private readonly INamedTypeSymbol? _dbContextFactoryOfT;
    private readonly INamedTypeSymbol? _httpClientFactoryType;
    private readonly INamedTypeSymbol? _httpContextAccessorType;
    private readonly INamedTypeSymbol? _memoryCacheType;
    private readonly INamedTypeSymbol? _loggerFactoryType;
    private readonly INamedTypeSymbol? _httpClientType;
    private int _registrationOrder;

    private RegistrationCollector(
        INamedTypeSymbol? serviceCollectionType,
        INamedTypeSymbol? serviceCollectionServiceExtensionsType,
        INamedTypeSymbol? serviceCollectionDescriptorExtensionsType,
        INamedTypeSymbol? serviceCollectionHostedServiceExtensionsType,
        INamedTypeSymbol? entityFrameworkServiceCollectionExtensionsType,
        INamedTypeSymbol? serviceDescriptorType,
        INamedTypeSymbol? hostedServiceType,
        INamedTypeSymbol? dbContextOptionsOfT,
        INamedTypeSymbol? dbContextFactoryOfT,
        INamedTypeSymbol? httpClientFactoryType,
        INamedTypeSymbol? httpContextAccessorType,
        INamedTypeSymbol? memoryCacheType,
        INamedTypeSymbol? loggerFactoryType,
        INamedTypeSymbol? httpClientType)
    {
        _serviceCollectionType = serviceCollectionType;
        _serviceCollectionServiceExtensionsType = serviceCollectionServiceExtensionsType;
        _serviceCollectionDescriptorExtensionsType = serviceCollectionDescriptorExtensionsType;
        _serviceCollectionHostedServiceExtensionsType = serviceCollectionHostedServiceExtensionsType;
        _entityFrameworkServiceCollectionExtensionsType = entityFrameworkServiceCollectionExtensionsType;
        _serviceDescriptorType = serviceDescriptorType;
        _hostedServiceType = hostedServiceType;
        _dbContextOptionsOfT = dbContextOptionsOfT;
        _dbContextFactoryOfT = dbContextFactoryOfT;
        _httpClientFactoryType = httpClientFactoryType;
        _httpContextAccessorType = httpContextAccessorType;
        _memoryCacheType = memoryCacheType;
        _loggerFactoryType = loggerFactoryType;
        _httpClientType = httpClientType;
        _registrations = new ConcurrentDictionary<ServiceIdentifier, ServiceRegistration>();
        _allRegistrations = new ConcurrentBag<ServiceRegistration>();
        _orderedRegistrations = new ConcurrentBag<OrderedRegistration>();
        _orderedMutations = new ConcurrentBag<OrderedRegistrationMutation>();
        _registrationOrder = 0;
    }

    private readonly struct ServiceIdentifier : System.IEquatable<ServiceIdentifier>
    {
        public INamedTypeSymbol Type { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }

        public ServiceIdentifier(INamedTypeSymbol type, object? key, bool isKeyed)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
        }

        public bool Equals(ServiceIdentifier other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) && Equals(Key, other.Key) && IsKeyed == other.IsKeyed;
        }

        public override bool Equals(object? obj)
        {
            return obj is ServiceIdentifier other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Type);
                hashCode = (hashCode * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                return hashCode;
            }
        }
    }

    private readonly struct ServiceImplementationIdentifier : System.IEquatable<ServiceImplementationIdentifier>
    {
        public ServiceImplementationIdentifier(
            INamedTypeSymbol serviceType,
            object? key,
            bool isKeyed,
            INamedTypeSymbol implementationType)
        {
            ServiceType = serviceType;
            Key = key;
            IsKeyed = isKeyed;
            ImplementationType = implementationType;
        }

        public INamedTypeSymbol ServiceType { get; }

        public object? Key { get; }

        public bool IsKeyed { get; }

        public INamedTypeSymbol ImplementationType { get; }

        public bool Equals(ServiceImplementationIdentifier other) =>
            SymbolEqualityComparer.Default.Equals(ServiceType, other.ServiceType) &&
            Equals(Key, other.Key) &&
            IsKeyed == other.IsKeyed &&
            SymbolEqualityComparer.Default.Equals(ImplementationType, other.ImplementationType);

        public override bool Equals(object? obj) =>
            obj is ServiceImplementationIdentifier other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(ServiceType);
                hashCode = (hashCode * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                hashCode = (hashCode * 397) ^ SymbolEqualityComparer.Default.GetHashCode(ImplementationType);
                return hashCode;
            }
        }
    }

    private sealed class ServiceIdentifierComparer : IEqualityComparer<ServiceIdentifier>
    {
        public static readonly ServiceIdentifierComparer Instance = new();

        public bool Equals(ServiceIdentifier x, ServiceIdentifier y) => x.Equals(y);

        public int GetHashCode(ServiceIdentifier obj) => obj.GetHashCode();
    }

    private sealed class ServiceImplementationIdentifierComparer : IEqualityComparer<ServiceImplementationIdentifier>
    {
        public static readonly ServiceImplementationIdentifierComparer Instance = new();

        public bool Equals(ServiceImplementationIdentifier x, ServiceImplementationIdentifier y) => x.Equals(y);

        public int GetHashCode(ServiceImplementationIdentifier obj) => obj.GetHashCode();
    }

    /// <summary>
    /// Creates a registration collector for the given compilation.
    /// Returns null if IServiceCollection is not available.
    /// </summary>
    public static RegistrationCollector? Create(Compilation compilation)
    {
        var serviceCollectionType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        if (serviceCollectionType is null)
        {
            return null;
        }

        var serviceCollectionServiceExtensionsType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions");

        // `ServiceCollectionDescriptorExtensions` has moved namespaces across package versions.
        var serviceCollectionDescriptorExtensionsType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions") ??
            compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions");

        var serviceCollectionHostedServiceExtensionsType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceCollectionHostedServiceExtensions");

        var entityFrameworkServiceCollectionExtensionsType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions");

        var serviceDescriptorType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceDescriptor");

        var hostedServiceType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Hosting.IHostedService");

        var dbContextOptionsOfT = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.DbContextOptions`1");

        var dbContextFactoryOfT = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.IDbContextFactory`1");

        var httpClientFactoryType = compilation.GetTypeByMetadataName(
            "System.Net.Http.IHttpClientFactory");

        var httpContextAccessorType = compilation.GetTypeByMetadataName(
            "Microsoft.AspNetCore.Http.IHttpContextAccessor");

        var memoryCacheType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Caching.Memory.IMemoryCache");

        var loggerFactoryType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Logging.ILoggerFactory");

        var httpClientType = compilation.GetTypeByMetadataName(
            "System.Net.Http.HttpClient");

        return new RegistrationCollector(
            serviceCollectionType,
            serviceCollectionServiceExtensionsType,
            serviceCollectionDescriptorExtensionsType,
            serviceCollectionHostedServiceExtensionsType,
            entityFrameworkServiceCollectionExtensionsType,
            serviceDescriptorType,
            hostedServiceType,
            dbContextOptionsOfT,
            dbContextFactoryOfT,
            httpClientFactoryType,
            httpContextAccessorType,
            memoryCacheType,
            loggerFactoryType,
            httpClientType);
    }

    /// <summary>
    /// Gets all collected registrations.
    /// </summary>
    public IEnumerable<ServiceRegistration> Registrations => GetSourceOrderedRegistrations()
        .GroupBy(
            registration => new ServiceIdentifier(registration.ServiceType, registration.Key, registration.IsKeyed),
            ServiceIdentifierComparer.Instance)
        .Select(group => group.Last());

    /// <summary>
    /// Gets all ordered registrations for analyzing registration order.
    /// </summary>
    public IEnumerable<OrderedRegistration> OrderedRegistrations => _orderedRegistrations;

    /// <summary>
    /// Gets source-ordered IServiceCollection mutations that remove earlier registrations.
    /// </summary>
    public IEnumerable<OrderedRegistrationMutation> OrderedMutations => _orderedMutations;

    /// <summary>
    /// Gets all collected Add* registrations (including duplicates) that include implementation metadata.
    /// </summary>
    public IEnumerable<ServiceRegistration> AllRegistrations => GetSourceOrderedRegistrations();

    /// <summary>
    /// Tries to get the registration for a specific service type and key.
    /// </summary>
    public bool TryGetRegistration(INamedTypeSymbol serviceType, object? key, bool isKeyed, out ServiceRegistration? registration)
    {
        registration = Registrations.LastOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.ServiceType, serviceType) &&
            Equals(candidate.Key, key) &&
            candidate.IsKeyed == isKeyed);
        return registration is not null;
    }

    /// <summary>
    /// Gets the lifetime for a service type, if registered.
    /// </summary>
    public ServiceLifetime? GetLifetime(ITypeSymbol? serviceType, object? key = null, bool isKeyed = false)
    {
        if (serviceType is INamedTypeSymbol namedType &&
            TryGetRegistration(namedType, key, isKeyed, out var registration) &&
            registration is not null)
        {
            return registration.Lifetime;
        }

        if (serviceType is INamedTypeSymbol closedGenericType &&
            closedGenericType.IsGenericType &&
            !closedGenericType.IsUnboundGenericType)
        {
            var openGenericType = closedGenericType.ConstructUnboundGenericType();
            if (TryGetRegistration(openGenericType, key, isKeyed, out registration) &&
                registration is not null)
            {
                return registration.Lifetime;
            }
        }

        return null;
    }

    private IEnumerable<ServiceRegistration> GetSourceOrderedRegistrations()
    {
        var ordered = _allRegistrations
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
                    DiscoveryOrder = registration.Order
                };
            })
            .OrderBy(item => item.Path, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.DiscoveryOrder)
            .Select(item => item.Registration);

        var seen = new HashSet<ServiceIdentifier>(ServiceIdentifierComparer.Instance);
        var seenImplementations = new HashSet<ServiceImplementationIdentifier>(ServiceImplementationIdentifierComparer.Instance);
        foreach (var registration in ordered)
        {
            var identifier = new ServiceIdentifier(registration.ServiceType, registration.Key, registration.IsKeyed);
            if (registration.SkipIfSameImplementationAlreadyRegistered)
            {
                if (registration.ImplementationType is not null)
                {
                    var implementationIdentifier = new ServiceImplementationIdentifier(
                        registration.ServiceType,
                        registration.Key,
                        registration.IsKeyed,
                        registration.ImplementationType);
                    if (!seenImplementations.Add(implementationIdentifier))
                    {
                        continue;
                    }
                }

                seen.Add(identifier);
                yield return registration;
                continue;
            }

            if ((registration.SkipIfAlreadyRegistered || registration.IsTryAdd) &&
                seen.Contains(identifier))
            {
                continue;
            }

            seen.Add(identifier);
            if (registration.ImplementationType is not null)
            {
                seenImplementations.Add(
                    new ServiceImplementationIdentifier(
                        registration.ServiceType,
                        registration.Key,
                        registration.IsKeyed,
                        registration.ImplementationType));
            }

            yield return registration;
        }
    }

    /// <summary>
    /// Analyzes an invocation expression to detect and record service registrations.
    /// </summary>
    public void AnalyzeInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Get the method symbol
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol is null)
        {
            // In incomplete code or overload-resolution failures Roslyn can surface
            // candidates without a final symbol. Prefer DI registration candidates so
            // typeof-based Add* calls are still tracked when possible.
            methodSymbol = symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    IsServiceCollectionExtensionMethod(candidate) ||
                    IsServiceCollectionAddMethod(candidate));

            if (methodSymbol is null)
            {
                return;
            }
        }

        var isExtension = IsServiceCollectionExtensionMethod(methodSymbol);
        var isAddMethod = IsServiceCollectionAddMethod(methodSymbol);

        // Check if this is an extension method on IServiceCollection OR ICollection.Add
        if (!isExtension && !isAddMethod)
        {
            return;
        }

        // If it's the instance Add method, verify the receiver is IServiceCollection
        if (isAddMethod && !IsReceiverServiceCollection(invocation, semanticModel))
        {
            return;
        }

        var methodName = methodSymbol.Name;
        var isTryAdd = IsTryAddMethod(methodName);
        var isTryAddEnumerable = methodName == "TryAddEnumerable";

        if (TryAnalyzeRegistrationMutation(
                invocation,
                semanticModel,
                methodSymbol,
                methodName))
        {
            return;
        }

        // Parse the lifetime from method name
        var lifetime = GetLifetimeFromMethodName(methodName);

        INamedTypeSymbol? serviceType;
        INamedTypeSymbol? implementationType;
        ExpressionSyntax? factoryExpression;
        object? key = null;
        bool hasImplementationInstance = false;
        bool implementationInstanceTypeIsExact = true;
        bool isKeyed = IsKeyedMethod(methodName);
        bool skipPrimaryIfAlreadyRegistered = false;
        var factoryProvidedParameterTypes = ImmutableArray<ITypeSymbol>.Empty;
        var companionRegistrations = new List<CompanionRegistration>();

        if (TryExtractHostedServiceRegistration(invocation, methodSymbol, semanticModel, out var hostedServiceType, out var hostedImplementationType, out var hostedFactoryExpression))
        {
            serviceType = hostedServiceType;
            implementationType = hostedImplementationType;
            factoryExpression = hostedFactoryExpression;
            lifetime = ServiceLifetime.Singleton;
            isKeyed = false;
        }
        else if (TryExtractHttpClientRegistration(
                     methodSymbol,
                     out var httpClientServiceType,
                     out var httpClientImplementationType))
        {
            serviceType = httpClientServiceType;
            implementationType = httpClientImplementationType;
            factoryExpression = null;
            lifetime = ServiceLifetime.Transient;
            isKeyed = false;
            factoryProvidedParameterTypes = _httpClientType is null
                ? ImmutableArray<ITypeSymbol>.Empty
                : ImmutableArray.Create<ITypeSymbol>(_httpClientType);

            if (_httpClientFactoryType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        _httpClientFactoryType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        ServiceLifetime.Singleton,
                        skipIfAlreadyRegistered: true));
            }
        }
        else if (TryExtractFrameworkSingletonRegistration(methodSymbol, out var frameworkSingletonType))
        {
            serviceType = frameworkSingletonType;
            implementationType = null;
            factoryExpression = null;
            hasImplementationInstance = true;
            lifetime = ServiceLifetime.Singleton;
            isKeyed = false;
            skipPrimaryIfAlreadyRegistered = true;
        }
        else if (TryExtractDbContextRegistration(
                     invocation,
                     methodSymbol,
                     semanticModel,
                     out var dbContextServiceType,
                     out var dbContextImplementationType,
                     out var dbContextLifetime,
                     out var dbContextOptionsServiceType,
                     out var dbContextOptionsLifetime))
        {
            serviceType = dbContextServiceType;
            implementationType = dbContextImplementationType;
            factoryExpression = null;
            lifetime = dbContextLifetime;
            isKeyed = false;
            skipPrimaryIfAlreadyRegistered = true;

            if (dbContextOptionsServiceType is not null &&
                dbContextOptionsLifetime is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        dbContextOptionsServiceType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        dbContextOptionsLifetime.Value,
                        skipIfAlreadyRegistered: true));
            }

            if (dbContextLifetime is ServiceLifetime effectiveDbContextLifetime)
            {
                AddFactoryBackedImplementationSelfRegistration(
                    companionRegistrations,
                    dbContextServiceType,
                    dbContextImplementationType,
                    effectiveDbContextLifetime);
            }
        }
        else if (TryExtractDbContextFactoryRegistration(
                     invocation,
                     methodSymbol,
                     semanticModel,
                     out var dbContextFactoryContextType,
                     out var dbContextFactoryContextLifetime,
                     out var dbContextFactoryOptionsServiceType,
                     out var dbContextFactoryOptionsLifetime,
                     out var dbContextFactoryServiceType,
                     out var dbContextFactoryImplementationType,
                     out var dbContextFactoryLifetime))
        {
            serviceType = dbContextFactoryContextType;
            implementationType = dbContextFactoryImplementationType is null
                ? dbContextFactoryContextType
                : null;
            factoryExpression = null;
            hasImplementationInstance = dbContextFactoryImplementationType is not null;
            lifetime = dbContextFactoryContextLifetime;
            isKeyed = false;
            skipPrimaryIfAlreadyRegistered = true;

            if (dbContextFactoryOptionsServiceType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        dbContextFactoryOptionsServiceType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        dbContextFactoryOptionsLifetime,
                        skipIfAlreadyRegistered: true));
            }

            if (dbContextFactoryServiceType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        dbContextFactoryServiceType,
                        dbContextFactoryImplementationType,
                        hasImplementationInstance: dbContextFactoryImplementationType is null,
                        dbContextFactoryLifetime,
                        skipIfAlreadyRegistered: true));
            }
        }
        else if (TryExtractDbContextPoolRegistration(
                     invocation,
                     methodSymbol,
                     out var dbContextPoolServiceType,
                     out var dbContextPoolImplementationType,
                     out var dbContextPoolOptionsServiceType,
                     out var dbContextPoolOptionsLifetime))
        {
            serviceType = dbContextPoolServiceType;
            implementationType = dbContextPoolImplementationType;
            factoryExpression = null;
            lifetime = ServiceLifetime.Scoped;
            isKeyed = false;
            skipPrimaryIfAlreadyRegistered = true;

            if (dbContextPoolOptionsServiceType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        dbContextPoolOptionsServiceType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        dbContextPoolOptionsLifetime,
                        skipIfAlreadyRegistered: true));
            }

            AddFactoryBackedImplementationSelfRegistration(
                companionRegistrations,
                dbContextPoolServiceType,
                dbContextPoolImplementationType,
                ServiceLifetime.Scoped);
        }
        else if (TryExtractPooledDbContextFactoryRegistration(
                     methodSymbol,
                     out var pooledFactoryContextType,
                     out var pooledFactoryOptionsServiceType,
                     out var pooledFactoryOptionsLifetime,
                     out var pooledFactoryServiceType,
                     out var pooledFactoryLifetime))
        {
            // EF Core registers a scoped TContext convenience service for pooled factories.
            serviceType = pooledFactoryContextType;
            implementationType = pooledFactoryContextType;
            factoryExpression = null;
            lifetime = ServiceLifetime.Scoped;
            isKeyed = false;
            skipPrimaryIfAlreadyRegistered = true;

            if (pooledFactoryOptionsServiceType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        pooledFactoryOptionsServiceType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        pooledFactoryOptionsLifetime,
                        skipIfAlreadyRegistered: true));
            }

            if (pooledFactoryServiceType is not null)
            {
                companionRegistrations.Add(
                    new CompanionRegistration(
                        pooledFactoryServiceType,
                        implementationType: null,
                        hasImplementationInstance: true,
                        pooledFactoryLifetime,
                        skipIfAlreadyRegistered: true));
            }
        }
        else if (lifetime.HasValue)
        {
            // Extract service, implementation types, factory expression, and key from standard methods
            (serviceType, implementationType, factoryExpression, hasImplementationInstance, key, implementationInstanceTypeIsExact) = ExtractTypes(methodSymbol, invocation, semanticModel);
        }
        else if ((methodName == "Add" || methodName == "TryAdd" || methodName == "TryAddEnumerable") &&
                 (isExtension || isAddMethod))
        {
            // Handle Add(ServiceDescriptor)
            (serviceType, implementationType, factoryExpression, hasImplementationInstance, lifetime, key, isKeyed, implementationInstanceTypeIsExact) =
                ExtractFromServiceDescriptor(invocation, semanticModel);

            if (methodName == "TryAddEnumerable")
            {
                skipPrimaryIfAlreadyRegistered = true;
            }
        }
        else
        {
            return;
        }

        if (serviceType is null || lifetime is null)
        {
            return;
        }

        var order = Interlocked.Increment(ref _registrationOrder);
        var flowKey = ServiceCollectionReachabilityAnalyzer.GetServiceCollectionReceiverKey(invocation, semanticModel);
        var serviceIdentifier = new ServiceIdentifier(serviceType, key, isKeyed);
        var hasEffectiveRegistration = _registrations.ContainsKey(serviceIdentifier);
        var orderedRegistration = new OrderedRegistration(
            serviceType,
            key,
            isKeyed,
            lifetime.Value,
            invocation.GetLocation(),
            flowKey,
            order,
            isTryAdd,
            methodName,
            skipPrimaryIfAlreadyRegistered,
            implementationType: implementationType);
        _orderedRegistrations.Add(orderedRegistration);

        // Store registrations that can actually become effective at runtime. TryAdd* only
        // participates when no earlier effective registration exists for the same service/key.
        // Discovery order is still used here for the runtime-effective registration cache,
        // but DI012 now applies a stable source-location ordering when it evaluates duplicates.
        if (implementationType is not null || factoryExpression is not null || hasImplementationInstance)
        {
            if (isTryAdd && !isTryAddEnumerable && hasEffectiveRegistration)
            {
                return;
            }

            var keyLiteral = SyntaxValueHelpers.TryFormatCSharpLiteral(key, out var formattedKey)
                ? formattedKey
                : null;
            var registration = new ServiceRegistration(
                serviceType,
                implementationType,
                factoryExpression,
                hasImplementationInstance,
                key,
                isKeyed,
                lifetime.Value,
                invocation.GetLocation(),
                keyLiteral,
                flowKey,
                order,
                skipPrimaryIfAlreadyRegistered,
                isTryAdd,
                isTryAddEnumerable,
                implementationInstanceTypeIsExact,
                factoryProvidedParameterTypes);

            _allRegistrations.Add(registration);

            if (!skipPrimaryIfAlreadyRegistered || !hasEffectiveRegistration)
            {
                // Store by service type and key (later registrations override earlier ones, like DI container behavior)
                _registrations[serviceIdentifier] = registration;
            }
        }

        foreach (var companionRegistrationInfo in companionRegistrations)
        {
            var companionIdentifier = new ServiceIdentifier(companionRegistrationInfo.ServiceType, null, false);
            var companionRegistration = new ServiceRegistration(
                companionRegistrationInfo.ServiceType,
                companionRegistrationInfo.ImplementationType,
                factoryExpression: null,
                companionRegistrationInfo.HasImplementationInstance,
                key: null,
                isKeyed: false,
                companionRegistrationInfo.Lifetime,
                invocation.GetLocation(),
                keyLiteral: null,
                flowKey,
                order,
                companionRegistrationInfo.SkipIfAlreadyRegistered,
                isTryAdd: false);

            _allRegistrations.Add(companionRegistration);
            if (!companionRegistrationInfo.SkipIfAlreadyRegistered ||
                !_registrations.ContainsKey(companionIdentifier))
            {
                _registrations[companionIdentifier] = companionRegistration;
            }
        }
    }

    private static void AddFactoryBackedImplementationSelfRegistration(
        List<CompanionRegistration> companionRegistrations,
        INamedTypeSymbol? serviceType,
        INamedTypeSymbol? implementationType,
        ServiceLifetime lifetime)
    {
        if (serviceType is null ||
            implementationType is null ||
            SymbolEqualityComparer.Default.Equals(serviceType, implementationType))
        {
            return;
        }

        companionRegistrations.Add(
            new CompanionRegistration(
                implementationType,
                implementationType: null,
                hasImplementationInstance: true,
                lifetime,
                skipIfAlreadyRegistered: true));
    }

    private readonly struct CompanionRegistration
    {
        public CompanionRegistration(
            INamedTypeSymbol serviceType,
            INamedTypeSymbol? implementationType,
            bool hasImplementationInstance,
            ServiceLifetime lifetime,
            bool skipIfAlreadyRegistered = false)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            HasImplementationInstance = hasImplementationInstance;
            Lifetime = lifetime;
            SkipIfAlreadyRegistered = skipIfAlreadyRegistered;
        }

        public INamedTypeSymbol ServiceType { get; }

        public INamedTypeSymbol? ImplementationType { get; }

        public bool HasImplementationInstance { get; }

        public ServiceLifetime Lifetime { get; }

        public bool SkipIfAlreadyRegistered { get; }
    }

    private bool IsServiceCollectionExtensionMethod(IMethodSymbol method)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        var containingType = originalMethod.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        var isKnownContainingType = IsKnownServiceCollectionExtensionsType(containingType);
        if (!isKnownContainingType)
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return SymbolEqualityComparer.Default.Equals(firstParam.Type, _serviceCollectionType);
    }

    private bool IsServiceCollectionAddMethod(IMethodSymbol method)
    {
        if (method.Name != "Add") return false;
        if (method.Parameters.Length != 1) return false;

        var paramType = method.Parameters[0].Type;
        if (_serviceDescriptorType is not null)
        {
            return SymbolEqualityComparer.Default.Equals(paramType, _serviceDescriptorType);
        }

        return paramType.Name == "ServiceDescriptor" &&
               (paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private bool IsReceiverServiceCollection(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (_serviceCollectionType is null) return false;

        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }

        if (receiver is null) return false;

        var typeInfo = semanticModel.GetTypeInfo(receiver);
        var type = typeInfo.Type;

        if (type is null) return false;

        // Check if type equals or implements IServiceCollection
        return InheritsFromOrEquals(type, _serviceCollectionType);
    }

    private bool InheritsFromOrEquals(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, baseType))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ServiceLifetime? GetLifetimeFromMethodName(string methodName)
    {
        // Handle common registration patterns (both Add*, TryAdd*, and keyed variants)
        if (methodName.StartsWith("AddSingleton") || methodName.StartsWith("TryAddSingleton") ||
            methodName.StartsWith("AddKeyedSingleton") || methodName.StartsWith("TryAddKeyedSingleton"))
        {
            return ServiceLifetime.Singleton;
        }

        if (methodName.StartsWith("AddScoped") || methodName.StartsWith("TryAddScoped") ||
            methodName.StartsWith("AddKeyedScoped") || methodName.StartsWith("TryAddKeyedScoped"))
        {
            return ServiceLifetime.Scoped;
        }

        if (methodName.StartsWith("AddTransient") || methodName.StartsWith("TryAddTransient") ||
            methodName.StartsWith("AddKeyedTransient") || methodName.StartsWith("TryAddKeyedTransient"))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private static bool IsKeyedMethod(string methodName)
    {
        return methodName.Contains("Keyed");
    }

    private static bool IsTryAddMethod(string methodName)
    {
        return methodName.StartsWith("TryAdd");
    }

    private bool TryAnalyzeRegistrationMutation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol,
        string methodName)
    {
        INamedTypeSymbol? serviceType = null;
        object? key = null;
        bool isKeyed = false;
        RegistrationMutationKind kind;

        if (methodName == "RemoveAll")
        {
            kind = RegistrationMutationKind.RemoveAll;
            serviceType = ExtractRemoveAllServiceType(methodSymbol, invocation, semanticModel);
        }
        else if (methodName == "Replace")
        {
            kind = RegistrationMutationKind.Replace;
        }
        else
        {
            return false;
        }

        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        var hasImplementationInstance = false;
        var implementationInstanceTypeIsExact = true;
        ServiceLifetime? replacementLifetime = null;
        if (kind == RegistrationMutationKind.Replace)
        {
            (serviceType, implementationType, factoryExpression, hasImplementationInstance, replacementLifetime, key, isKeyed, implementationInstanceTypeIsExact) =
                ExtractFromServiceDescriptor(invocation, semanticModel);

            // new ServiceDescriptor(typeof(T), instance) carries no explicit lifetime — MEDI
            // registers the instance as a singleton.
            if (replacementLifetime is null && hasImplementationInstance)
            {
                replacementLifetime = ServiceLifetime.Singleton;
            }
        }

        if (serviceType is null)
        {
            return false;
        }

        var order = Interlocked.Increment(ref _registrationOrder);
        var flowKey = ServiceCollectionReachabilityAnalyzer.GetServiceCollectionReceiverKey(invocation, semanticModel);
        _orderedMutations.Add(
            new OrderedRegistrationMutation(
                serviceType,
                key,
                isKeyed,
                invocation.GetLocation(),
                flowKey,
                order,
                kind));

        // Replace removes the existing slot AND adds its own descriptor: record the replacement
        // as a registration so cycles, captives, and resolutions it introduces are visible. The
        // registration order comes after the removal's, mirroring runtime behavior.
        if (kind == RegistrationMutationKind.Replace &&
            replacementLifetime is { } descriptorLifetime &&
            (implementationType is not null || factoryExpression is not null || hasImplementationInstance))
        {
            var registrationOrder = Interlocked.Increment(ref _registrationOrder);
            var serviceIdentifier = new ServiceIdentifier(serviceType, key, isKeyed);
            _orderedRegistrations.Add(
                new OrderedRegistration(
                    serviceType,
                    key,
                    isKeyed,
                    descriptorLifetime,
                    invocation.GetLocation(),
                    flowKey,
                    registrationOrder,
                    isTryAdd: false,
                    methodName,
                    skipIfAlreadyRegistered: false,
                    implementationType: implementationType));

            var keyLiteral = SyntaxValueHelpers.TryFormatCSharpLiteral(key, out var formattedKey)
                ? formattedKey
                : null;
            var replacementRegistration = new ServiceRegistration(
                serviceType,
                implementationType,
                factoryExpression,
                hasImplementationInstance,
                key,
                isKeyed,
                descriptorLifetime,
                invocation.GetLocation(),
                keyLiteral,
                flowKey,
                registrationOrder,
                skipIfAlreadyRegistered: false,
                isTryAdd: false,
                implementationInstanceTypeIsExact: implementationInstanceTypeIsExact);
            _allRegistrations.Add(replacementRegistration);
            _registrations[serviceIdentifier] = replacementRegistration;
        }

        return true;
    }

    private static INamedTypeSymbol? ExtractRemoveAllServiceType(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            return method.TypeArguments[0] as INamedTypeSymbol;
        }

        var typeExpression =
            invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<TypeOfExpressionSyntax>()
                .FirstOrDefault();

        return typeExpression is null
            ? null
            : semanticModel.GetTypeInfo(typeExpression.Type).Type as INamedTypeSymbol;
    }

    private (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, bool hasImplementationInstance, ServiceLifetime? lifetime, object? key, bool isKeyed, bool implementationInstanceTypeIsExact) ExtractFromServiceDescriptor(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Look for ServiceDescriptor argument
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (TryResolveServiceDescriptorExpression(arg.Expression, semanticModel, out var descriptorExpression))
            {
                return ExtractFromResolvedServiceDescriptorExpression(descriptorExpression, semanticModel);
            }
        }

        return (null, null, null, false, null, null, false, true);
    }

    private bool TryResolveServiceDescriptorExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out ExpressionSyntax descriptorExpression)
    {
        return TryResolveServiceDescriptorExpression(
            expression,
            semanticModel,
            System.Array.Empty<ILocalSymbol>(),
            out descriptorExpression);
    }

    private bool TryResolveServiceDescriptorExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IReadOnlyList<ILocalSymbol> visitedLocals,
        out ExpressionSyntax descriptorExpression)
    {
        descriptorExpression = StripParentheses(expression);

        if (IsInlineServiceDescriptorExpression(descriptorExpression, semanticModel))
        {
            return true;
        }

        if (semanticModel.GetSymbolInfo(descriptorExpression).Symbol is not ILocalSymbol local ||
            !IsServiceDescriptorType(local.Type) ||
            visitedLocals.Any(visitedLocal => SymbolEqualityComparer.Default.Equals(visitedLocal, local)) ||
            !TryGetStableLocalServiceDescriptorValue(descriptorExpression, semanticModel, local, out var localValue))
        {
            return false;
        }

        var nextVisitedLocals = visitedLocals.Concat(new[] { local }).ToArray();
        return TryResolveServiceDescriptorExpression(
            localValue,
            semanticModel,
            nextVisitedLocals,
            out descriptorExpression);
    }

    private bool IsInlineServiceDescriptorExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is BaseObjectCreationExpressionSyntax creation)
        {
            return IsServiceDescriptorType(semanticModel.GetTypeInfo(creation).Type);
        }

        if (expression is InvocationExpressionSyntax descriptorInvocation &&
            semanticModel.GetSymbolInfo(descriptorInvocation).Symbol is IMethodSymbol descriptorMethod)
        {
            return IsServiceDescriptorType(descriptorMethod.ContainingType);
        }

        return false;
    }

    private static bool TryGetStableLocalServiceDescriptorValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ILocalSymbol local,
        out ExpressionSyntax value)
    {
        value = null!;

        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator ||
            declarator.Initializer?.Value is not { } initializer ||
            declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { } declarationStatement ||
            expression.FirstAncestorOrSelf<StatementSyntax>() is not { } usageStatement ||
            declarationStatement.Parent is not BlockSyntax block ||
            usageStatement.Parent != block ||
            declarationStatement.SpanStart >= expression.SpanStart)
        {
            return false;
        }

        var declarationIndex = block.Statements.IndexOf(declarationStatement);
        var usageIndex = block.Statements.IndexOf(usageStatement);
        if (declarationIndex < 0 || usageIndex <= declarationIndex)
        {
            return false;
        }

        for (var i = declarationIndex + 1; i < usageIndex; i++)
        {
            if (WritesLocal(block.Statements[i], local, semanticModel))
            {
                return false;
            }
        }

        value = initializer;
        return true;
    }

    private static bool WritesLocal(SyntaxNode node, ILocalSymbol local, SemanticModel semanticModel)
    {
        foreach (var assignment in node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (IsLocalReference(assignment.Left, local, semanticModel))
            {
                return true;
            }
        }

        foreach (var prefix in node.DescendantNodesAndSelf().OfType<PrefixUnaryExpressionSyntax>())
        {
            if ((prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                 prefix.IsKind(SyntaxKind.PreDecrementExpression)) &&
                IsLocalReference(prefix.Operand, local, semanticModel))
            {
                return true;
            }
        }

        foreach (var postfix in node.DescendantNodesAndSelf().OfType<PostfixUnaryExpressionSyntax>())
        {
            if ((postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                 postfix.IsKind(SyntaxKind.PostDecrementExpression)) &&
                IsLocalReference(postfix.Operand, local, semanticModel))
            {
                return true;
            }
        }

        foreach (var argument in node.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
        {
            if (argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) ||
                argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                if (IsLocalReference(argument.Expression, local, semanticModel))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsLocalReference(ExpressionSyntax expression, ILocalSymbol local, SemanticModel semanticModel) =>
        SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(StripParentheses(expression)).Symbol,
            local);

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, bool hasImplementationInstance, ServiceLifetime? lifetime, object? key, bool isKeyed, bool implementationInstanceTypeIsExact) ExtractFromResolvedServiceDescriptorExpression(
        ExpressionSyntax descriptorExpression,
        SemanticModel semanticModel)
    {
        if (descriptorExpression is BaseObjectCreationExpressionSyntax creation)
        {
            var typeSymbol = semanticModel.GetTypeInfo(creation).Type;
            if (IsServiceDescriptorType(typeSymbol))
            {
                return ExtractFromServiceDescriptorArguments(creation.ArgumentList, semanticModel);
            }
        }
        else if (descriptorExpression is InvocationExpressionSyntax describeInvocation)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(describeInvocation).Symbol as IMethodSymbol;
            if (methodSymbol is not null &&
                IsServiceDescriptorType(methodSymbol.ContainingType))
            {
                if (methodSymbol.Name == "Describe")
                {
                    return ExtractFromServiceDescriptorArguments(describeInvocation.ArgumentList, semanticModel);
                }

                var lifetime = GetLifetimeFromServiceDescriptorFactoryMethod(methodSymbol.Name);
                if (lifetime.HasValue)
                {
                    var (serviceType, implementationType, factoryExpression, hasImplementationInstance, key, implementationInstanceTypeIsExact) =
                        ExtractTypes(methodSymbol, describeInvocation, semanticModel);
                    return (serviceType, implementationType, factoryExpression, hasImplementationInstance, lifetime, key, IsKeyedMethod(methodSymbol.Name), implementationInstanceTypeIsExact);
                }
            }
        }

        return (null, null, null, false, null, null, false, true);
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, bool hasImplementationInstance, ServiceLifetime? lifetime, object? key, bool isKeyed, bool implementationInstanceTypeIsExact) ExtractFromServiceDescriptorArguments(
        ArgumentListSyntax? argumentList,
        SemanticModel semanticModel)
    {
        var args = argumentList?.Arguments;
        if (args is null || args.Value.Count < 2)
        {
            return (null, null, null, false, null, null, false, true);
        }

        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        bool hasImplementationInstance = false;
        ServiceLifetime? lifetime = null;
        object? key = null;
        bool isKeyed = false;
        bool implementationInstanceTypeIsExact = true;

        for (int i = 0; i < args.Value.Count; i++)
        {
            var arg = args.Value[i];
            var argName = arg.NameColon?.Name.Identifier.Text;
            var expr = arg.Expression;

            // 1. Service Type (Argument "serviceType" or index 0)
            if (argName == "serviceType" || (argName == null && i == 0))
            {
                if (expr is TypeOfExpressionSyntax serviceTypeOf)
                {
                    var typeInfo = semanticModel.GetTypeInfo(serviceTypeOf.Type);
                    serviceType = typeInfo.Type as INamedTypeSymbol;
                }
                continue;
            }

            // 2. Key (Argument "serviceKey")
            if (argName == "serviceKey")
            {
                key = ExtractConstantValue(expr, semanticModel);
                isKeyed = true;
                continue;
            }

            // 3. Lifetime (Argument "lifetime" or explicit ServiceLifetime enum/cast)
            if (argName == "lifetime" || IsServiceLifetimeExpression(expr, semanticModel))
            {
                lifetime = ExtractLifetime(expr, semanticModel);
                continue;
            }

            // 4. Implementation Type (Argument "implementationType")
            if (argName == "implementationType")
            {
                if (expr is TypeOfExpressionSyntax implTypeOf)
                {
                    var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                    implementationType = typeInfo.Type as INamedTypeSymbol;
                }
                continue;
            }

            // 5. Factory (Argument "factory")
            if (argName == "factory")
            {
                if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                {
                    factoryExpression = expr;
                }
                continue;
            }

            // 6. Instance (Argument "instance")
            if (argName == "instance")
            {
                if (ExtractImplementationInstanceType(expr, semanticModel, out var namedInstanceTypeIsExact) is { } namedInstanceType)
                {
                    implementationType = namedInstanceType;
                    hasImplementationInstance = true;
                    implementationInstanceTypeIsExact = namedInstanceTypeIsExact;
                }
                continue;
            }

            // Fallback for positional arguments
            if (argName == null)
            {
                if (i == 1)
                {
                    if (expr is TypeOfExpressionSyntax implTypeOf)
                    {
                        var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                        implementationType = typeInfo.Type as INamedTypeSymbol;
                    }
                    else if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                    {
                        factoryExpression = expr;
                    }
                    else
                    {
                        // Check if it's a key (constant) or instance
                        var val = ExtractConstantValue(expr, semanticModel);
                        if (val != null)
                        {
                            key = val;
                            isKeyed = true;
                        }
                        else if (ExtractImplementationInstanceType(expr, semanticModel, out var positionalInstanceTypeIsExact) is { } positionalInstanceType)
                        {
                            implementationType = positionalInstanceType;
                            hasImplementationInstance = true;
                            implementationInstanceTypeIsExact = positionalInstanceTypeIsExact;
                        }
                    }
                }
                else if (i == 2)
                {
                    // If we have a key (from i=1), this might be impl/factory
                    if (key != null || isKeyed)
                    {
                        if (expr is TypeOfExpressionSyntax implTypeOf)
                        {
                            var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                            implementationType = typeInfo.Type as INamedTypeSymbol;
                        }
                        else if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                        {
                            factoryExpression = expr;
                        }
                    }
                    else if (lifetime == null)
                    {
                        lifetime = ExtractLifetime(expr, semanticModel);
                    }
                }
            }
        }

        return (serviceType, implementationType, factoryExpression, hasImplementationInstance, lifetime, key, isKeyed, implementationInstanceTypeIsExact);
    }

    private static bool IsServiceLifetimeExpression(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expr);
        return typeInfo.Type?.Name == "ServiceLifetime" ||
               (typeInfo.ConvertedType?.Name == "ServiceLifetime");
    }

    private static ServiceLifetime? ExtractLifetime(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        // Handle Enum member access: ServiceLifetime.Scoped
        if (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var lifetimeName = memberAccess.Name.Identifier.Text;
            if (System.Enum.TryParse<ServiceLifetime>(lifetimeName, out var parsedLifetime))
            {
                return parsedLifetime;
            }
        }

        var symbol = semanticModel.GetSymbolInfo(expr).Symbol;
        if (symbol?.ContainingType.Name == "ServiceLifetime" &&
            System.Enum.TryParse<ServiceLifetime>(symbol.Name, out var symbolLifetime))
        {
            return symbolLifetime;
        }

        // Handle Cast: (ServiceLifetime)0
        if (expr is CastExpressionSyntax castExpr)
        {
            // We only handle constant values in casts for now
            var constantValue = semanticModel.GetConstantValue(castExpr);
            if (constantValue.HasValue && constantValue.Value is int intValue)
            {
                if (System.Enum.IsDefined(typeof(ServiceLifetime), intValue))
                {
                    return (ServiceLifetime)intValue;
                }
            }
        }

        return null;
    }

    private static ServiceLifetime? GetLifetimeFromServiceDescriptorFactoryMethod(string methodName)
    {
        if (methodName.EndsWith("Singleton"))
        {
            return ServiceLifetime.Singleton;
        }

        if (methodName.EndsWith("Scoped"))
        {
            return ServiceLifetime.Scoped;
        }

        if (methodName.EndsWith("Transient"))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private bool TryExtractDbContextRegistration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType,
        out ServiceLifetime? contextLifetime,
        out INamedTypeSymbol? optionsServiceType,
        out ServiceLifetime? optionsLifetime)
    {
        serviceType = null;
        implementationType = null;
        contextLifetime = null;
        optionsServiceType = null;
        optionsLifetime = null;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddDbContext" ||
            !IsKnownEntityFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType) ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length is not (1 or 2))
        {
            return false;
        }

        serviceType = method.TypeArguments[0] as INamedTypeSymbol;
        implementationType = method.TypeArguments.Length == 2
            ? method.TypeArguments[1] as INamedTypeSymbol
            : serviceType;

        if (serviceType is null || implementationType is null)
        {
            return false;
        }

        if (!TryGetLifetimeArgument(
                invocation,
                method,
                semanticModel,
                "contextLifetime",
                ServiceLifetime.Scoped,
                out var extractedContextLifetime) ||
            !TryGetLifetimeArgument(
                invocation,
                method,
                semanticModel,
                "optionsLifetime",
                ServiceLifetime.Scoped,
                out var extractedOptionsLifetime))
        {
            return false;
        }

        contextLifetime = extractedContextLifetime;
        optionsLifetime = extractedOptionsLifetime;
        optionsServiceType = TryConstructDbContextOptionsType(implementationType);
        return true;
    }

    private INamedTypeSymbol? TryConstructDbContextOptionsType(INamedTypeSymbol contextImplementationType)
    {
        return TryConstructGenericType(_dbContextOptionsOfT, contextImplementationType);
    }

    private INamedTypeSymbol? TryConstructDbContextFactoryType(INamedTypeSymbol contextImplementationType)
    {
        return TryConstructGenericType(_dbContextFactoryOfT, contextImplementationType);
    }

    private static INamedTypeSymbol? TryConstructGenericType(
        INamedTypeSymbol? genericType,
        INamedTypeSymbol typeArgument)
    {
        if (genericType is null ||
            typeArgument.IsUnboundGenericType)
        {
            return null;
        }

        try
        {
            return genericType.Construct(typeArgument);
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    private bool TryExtractDbContextFactoryRegistration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        out INamedTypeSymbol? contextType,
        out ServiceLifetime contextLifetime,
        out INamedTypeSymbol? optionsServiceType,
        out ServiceLifetime optionsLifetime,
        out INamedTypeSymbol? factoryServiceType,
        out INamedTypeSymbol? factoryImplementationType,
        out ServiceLifetime factoryLifetime)
    {
        contextType = null;
        contextLifetime = default;
        optionsServiceType = null;
        optionsLifetime = default;
        factoryServiceType = null;
        factoryImplementationType = null;
        factoryLifetime = default;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddDbContextFactory" ||
            !IsKnownEntityFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType) ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length is not (1 or 2))
        {
            return false;
        }

        contextType = method.TypeArguments[0] as INamedTypeSymbol;
        if (contextType is null)
        {
            return false;
        }

        if (!TryGetLifetimeArgument(
                invocation,
                method,
                semanticModel,
                "lifetime",
                ServiceLifetime.Singleton,
                out var extractedLifetime))
        {
            return false;
        }

        optionsLifetime = extractedLifetime;
        factoryLifetime = extractedLifetime;
        contextLifetime = extractedLifetime == ServiceLifetime.Transient
            ? ServiceLifetime.Transient
            : ServiceLifetime.Scoped;
        optionsServiceType = TryConstructDbContextOptionsType(contextType);
        factoryServiceType = TryConstructDbContextFactoryType(contextType);
        factoryImplementationType = method.TypeArguments.Length == 2
            ? method.TypeArguments[1] as INamedTypeSymbol
            : null;

        return true;
    }

    private bool TryExtractDbContextPoolRegistration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType,
        out INamedTypeSymbol? optionsServiceType,
        out ServiceLifetime optionsLifetime)
    {
        serviceType = null;
        implementationType = null;
        optionsServiceType = null;
        optionsLifetime = ServiceLifetime.Singleton;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddDbContextPool" ||
            !IsKnownEntityFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType) ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length is not (1 or 2))
        {
            return false;
        }

        serviceType = method.TypeArguments[0] as INamedTypeSymbol;
        implementationType = method.TypeArguments.Length == 2
            ? method.TypeArguments[1] as INamedTypeSymbol
            : serviceType;

        if (serviceType is null || implementationType is null)
        {
            return false;
        }

        optionsServiceType = TryConstructDbContextOptionsType(implementationType);
        return true;
    }

    private bool TryExtractPooledDbContextFactoryRegistration(
        IMethodSymbol method,
        out INamedTypeSymbol? contextType,
        out INamedTypeSymbol? optionsServiceType,
        out ServiceLifetime optionsLifetime,
        out INamedTypeSymbol? factoryServiceType,
        out ServiceLifetime factoryLifetime)
    {
        contextType = null;
        optionsServiceType = null;
        optionsLifetime = ServiceLifetime.Singleton;
        factoryServiceType = null;
        factoryLifetime = ServiceLifetime.Singleton;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddPooledDbContextFactory" ||
            !IsKnownEntityFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType) ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length != 1)
        {
            return false;
        }

        contextType = method.TypeArguments[0] as INamedTypeSymbol;
        if (contextType is null)
        {
            return false;
        }

        optionsServiceType = TryConstructDbContextOptionsType(contextType);
        factoryServiceType = TryConstructDbContextFactoryType(contextType);
        return true;
    }

    private static bool TryGetLifetimeArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        string parameterName,
        ServiceLifetime defaultLifetime,
        out ServiceLifetime lifetime)
    {
        var expression = GetInvocationArgumentExpression(invocation, method, parameterName);
        if (expression is null)
        {
            lifetime = defaultLifetime;
            return true;
        }

        var extractedLifetime = ExtractLifetime(expression, semanticModel);
        if (extractedLifetime is null)
        {
            lifetime = default;
            return false;
        }

        lifetime = extractedLifetime.Value;
        return true;
    }

    private static object? ExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel) =>
        SyntaxValueHelpers.TryExtractServiceKeyValue(expr, semanticModel, out var value, out _) ? value : null;

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, bool hasImplementationInstance, object? key, bool implementationInstanceTypeIsExact) ExtractTypes(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        bool hasImplementationInstance = false;
        object? key = null;
        bool implementationInstanceTypeIsExact = true;

        if (TryGetFactoryArgumentExpression(method, invocation, semanticModel, out var factoryArgumentExpression))
        {
            factoryExpression = factoryArgumentExpression;
        }

        bool isKeyed = IsKeyedMethod(method.Name);
        var arguments = invocation.ArgumentList.Arguments;

        // Pattern 1: Generic method AddXxx<TService>(...)
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            serviceType = method.TypeArguments[0] as INamedTypeSymbol;

            if (method.TypeArguments.Length > 1)
            {
                implementationType = method.TypeArguments[1] as INamedTypeSymbol;
            }
            else if (TryGetImplementationInstanceType(method, invocation, semanticModel, out var implementationInstanceType, out var instanceTypeIsExact))
            {
                implementationType = implementationInstanceType;
                hasImplementationInstance = true;
                implementationInstanceTypeIsExact = instanceTypeIsExact;
            }
            else if (factoryExpression is null)
            {
                // Only default to serviceType if NO factory is present.
                // If factory is present, implementation is unknown (null).
                implementationType = serviceType;
            }

            // Key extraction for generic methods
            // AddKeyedSingleton<T>(key, ...) -> key is 1st argument
            if (isKeyed && arguments.Count > 0)
            {
                key = ExtractConstantValue(arguments[0].Expression, semanticModel);
            }

            return (serviceType, implementationType, factoryExpression, hasImplementationInstance, key, implementationInstanceTypeIsExact);
        }

        if (TryExtractNonGenericOperationArguments(invocation, semanticModel, isKeyed, out var operationServiceType, out var operationImplementationType, out var operationHasImplementationInstance, out var operationKey, out var operationInstanceTypeIsExact))
        {
            // One-Type self-binding overloads such as AddSingleton(IServiceCollection, Type)
            // bind implementation := service. Pattern 2 (the typeof-syntax fallback) already
            // does this; mirror it here so the operation-arguments path stays consistent.
            // Only apply when the bound method's signature actually omits the implementation /
            // factory / instance parameters, otherwise a non-extractable implementation argument
            // (e.g. AddSingleton(typeof(IFoo), variableHoldingType)) would be wrongly recorded
            // as a service->service self-binding.
            if (operationImplementationType is null &&
                !operationHasImplementationInstance &&
                factoryExpression is null &&
                IsOneTypeSelfBindingOverload(method))
            {
                operationImplementationType = operationServiceType;
            }
            return (operationServiceType, operationImplementationType, factoryExpression, operationHasImplementationInstance, operationKey, operationInstanceTypeIsExact);
        }

        // Pattern 2: Non-generic with Type parameters AddXxx(typeof(TService)) or AddXxx(typeof(TService), typeof(TImpl))
        var typeofArgs = new List<INamedTypeSymbol>();
        INamedTypeSymbol? instanceImplementationType = null;
        int keyIndex = -1;

        for (int i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                }
            }
            else if (isKeyed && keyIndex == -1 && typeofArgs.Count > 0)
            {
                // If we found the service type (typeofArgs[0]), the next non-typeof argument might be the key
                // AddKeyedSingleton(typeof(T), key, ...)
                keyIndex = i;
                key = ExtractConstantValue(arg.Expression, semanticModel);
            }
            else if (ExtractImplementationInstanceType(arg.Expression, semanticModel, out var fallbackInstanceTypeIsExact) is { } fallbackInstanceType)
            {
                instanceImplementationType = fallbackInstanceType;
                implementationInstanceTypeIsExact = fallbackInstanceTypeIsExact;
            }
        }

        if (typeofArgs.Count >= 1)
        {
            serviceType = typeofArgs[0];
            if (typeofArgs.Count > 1)
            {
                implementationType = typeofArgs[1];
            }
            else if (instanceImplementationType is not null)
            {
                implementationType = instanceImplementationType;
                hasImplementationInstance = true;
            }
            else if (factoryExpression is null)
            {
                // Only default to serviceType if NO factory is present
                implementationType = serviceType;
            }

            return (serviceType, implementationType, factoryExpression, hasImplementationInstance, key, implementationInstanceTypeIsExact);
        }

        return (null, null, null, false, key, true);
    }

    private static bool IsOneTypeSelfBindingOverload(IMethodSymbol method)
    {
        var sourceMethod = method.ReducedFrom ?? method;
        bool sawServiceType = false;
        foreach (var parameter in sourceMethod.Parameters)
        {
            switch (parameter.Name)
            {
                case "serviceType":
                    sawServiceType = true;
                    break;
                case "implementationType":
                case "implementationInstance":
                case "implementationFactory":
                    return false;
            }
        }

        return sawServiceType;
    }

    private static bool TryExtractNonGenericOperationArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        bool isKeyed,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType,
        out bool hasImplementationInstance,
        out object? key,
        out bool implementationInstanceTypeIsExact)
    {
        serviceType = null;
        implementationType = null;
        hasImplementationInstance = false;
        key = null;
        implementationInstanceTypeIsExact = true;

        if (semanticModel.GetOperation(invocation) is not IInvocationOperation invocationOperation)
        {
            return false;
        }

        foreach (var argument in invocationOperation.Arguments)
        {
            var parameterName = argument.Parameter?.Name;
            if (string.IsNullOrEmpty(parameterName))
            {
                continue;
            }

            if (argument.Value.Syntax is not ExpressionSyntax expression)
            {
                continue;
            }

            switch (parameterName)
            {
                case "serviceType":
                    serviceType = ExtractTypeOfArgument(expression, semanticModel);
                    break;

                case "implementationType":
                    implementationType = ExtractTypeOfArgument(expression, semanticModel);
                    break;

                case "implementationInstance":
                    implementationType = ExtractImplementationInstanceType(expression, semanticModel, out var instanceTypeIsExact);
                    hasImplementationInstance = implementationType is not null;
                    implementationInstanceTypeIsExact = !hasImplementationInstance || instanceTypeIsExact;
                    break;

                case "serviceKey":
                case "key":
                    if (isKeyed)
                    {
                        key = ExtractConstantValue(expression, semanticModel);
                    }
                    break;
            }
        }

        return serviceType is not null;
    }

    private static INamedTypeSymbol? ExtractTypeOfArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            expression = parenthesizedExpression.Expression;
        }

        return expression is TypeOfExpressionSyntax typeOfExpression
            ? semanticModel.GetTypeInfo(typeOfExpression.Type).Type as INamedTypeSymbol
            : null;
    }

    private static bool TryGetImplementationInstanceType(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out INamedTypeSymbol? implementationInstanceType,
        out bool implementationInstanceTypeIsExact)
    {
        implementationInstanceType = null;
        implementationInstanceTypeIsExact = false;

        var sourceMethod = method.ReducedFrom ?? method;
        var isReducedExtension = method.ReducedFrom is not null;

        for (var parameterIndex = 0; parameterIndex < sourceMethod.Parameters.Length; parameterIndex++)
        {
            var parameter = sourceMethod.Parameters[parameterIndex];
            if (parameter.Type.TypeKind == TypeKind.Delegate ||
                IsServiceCollectionType(parameter.Type) ||
                parameter.Name is "serviceKey" or "key")
            {
                continue;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.NameColon?.Name.Identifier.Text != parameter.Name)
                {
                    continue;
                }

                implementationInstanceType = ExtractImplementationInstanceType(
                    argument.Expression,
                    semanticModel,
                    out implementationInstanceTypeIsExact);
                return implementationInstanceType is not null;
            }

            var argumentIndex = isReducedExtension ? parameterIndex - 1 : parameterIndex;
            if (argumentIndex < 0 || argumentIndex >= invocation.ArgumentList.Arguments.Count)
            {
                continue;
            }

            implementationInstanceType = ExtractImplementationInstanceType(
                invocation.ArgumentList.Arguments[argumentIndex].Expression,
                semanticModel,
                out implementationInstanceTypeIsExact);
            return implementationInstanceType is not null;
        }

        return false;
    }

    private static bool IsServiceCollectionType(ITypeSymbol type) =>
        type.Name == "IServiceCollection" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static INamedTypeSymbol? ExtractImplementationInstanceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out bool isExactRuntimeType)
    {
        // An object creation proves the runtime type even through upcasts/parentheses.
        // Only runtime-object-preserving casts (identity/reference/boxing) are unwrapped:
        // a user-defined conversion operator produces a different object, so the cast's
        // operand says nothing about the registered instance's runtime type.
        var unwrapped = expression;
        while (true)
        {
            if (unwrapped is ParenthesizedExpressionSyntax parenthesized)
            {
                unwrapped = parenthesized.Expression;
                continue;
            }

            if (unwrapped is CastExpressionSyntax cast &&
                semanticModel.GetTypeInfo(cast.Type).Type is { } castType &&
                semanticModel.ClassifyConversion(cast.Expression, castType) is
                    { IsIdentity: true } or { IsReference: true } or { IsBoxing: true })
            {
                unwrapped = cast.Expression;
                continue;
            }

            break;
        }

        if (unwrapped is BaseObjectCreationExpressionSyntax creation &&
            semanticModel.GetTypeInfo(creation).Type is INamedTypeSymbol createdType)
        {
            isExactRuntimeType = true;
            return createdType;
        }

        // Fallback: only the static type is known. Sealed and value types cannot have
        // subtypes, so the static type is the runtime type; anything else is inexact.
        var staticType = semanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol;
        isExactRuntimeType = staticType is not null && (staticType.IsSealed || staticType.IsValueType);
        return staticType;
    }

    private static bool TryGetFactoryArgumentExpression(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ExpressionSyntax factoryExpression)
    {
        factoryExpression = null!;

        if (semanticModel.GetOperation(invocation) is IInvocationOperation invocationOperation)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.Type.TypeKind != TypeKind.Delegate ||
                    argument.Value.Syntax is not ExpressionSyntax argumentExpression ||
                    !IsFactoryExpression(argumentExpression))
                {
                    continue;
                }

                factoryExpression = argumentExpression;
                return true;
            }
        }

        var sourceMethod = method.ReducedFrom ?? method;
        var isReducedExtension = method.ReducedFrom is not null;

        for (var parameterIndex = 0; parameterIndex < sourceMethod.Parameters.Length; parameterIndex++)
        {
            var parameter = sourceMethod.Parameters[parameterIndex];
            if (parameter.Type.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.NameColon?.Name.Identifier.Text != parameter.Name)
                {
                    continue;
                }

                factoryExpression = argument.Expression;
                return true;
            }

            var argumentIndex = isReducedExtension ? parameterIndex - 1 : parameterIndex;
            if (argumentIndex < 0 || argumentIndex >= invocation.ArgumentList.Arguments.Count)
            {
                continue;
            }

            var candidateExpression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
            if (!IsFactoryExpression(candidateExpression))
            {
                continue;
            }

            factoryExpression = candidateExpression;
            return true;
        }

        return false;
    }

    private static ExpressionSyntax? GetInvocationArgumentExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        string parameterName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
            {
                return argument.Expression;
            }
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var isReducedExtension = methodSymbol.ReducedFrom is not null;

        for (var i = 0; i < sourceMethod.Parameters.Length; i++)
        {
            if (sourceMethod.Parameters[i].Name != parameterName)
            {
                continue;
            }

            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private bool IsKnownServiceCollectionExtensionsType(INamedTypeSymbol type)
    {
        if (SymbolEqualityComparer.Default.Equals(type, _serviceCollectionServiceExtensionsType) ||
            SymbolEqualityComparer.Default.Equals(type, _serviceCollectionDescriptorExtensionsType) ||
            SymbolEqualityComparer.Default.Equals(type, _serviceCollectionHostedServiceExtensionsType) ||
            SymbolEqualityComparer.Default.Equals(type, _entityFrameworkServiceCollectionExtensionsType))
        {
            return true;
        }

        var fullName = type.ToDisplayString();
        return fullName == "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.ServiceCollectionHostedServiceExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions" ||
               IsKnownFrameworkServiceCollectionExtensionsTypeName(fullName);
    }

    private static bool IsKnownFrameworkServiceCollectionExtensionsType(INamedTypeSymbol? type)
    {
        return IsKnownFrameworkServiceCollectionExtensionsTypeName(type?.ToDisplayString());
    }

    private static bool IsKnownFrameworkServiceCollectionExtensionsTypeName(string? fullName)
    {
        return fullName == "Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.MemoryCacheServiceCollectionExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.HttpServiceCollectionExtensions";
    }

    private bool IsKnownEntityFrameworkServiceCollectionExtensionsType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, _entityFrameworkServiceCollectionExtensionsType))
        {
            return true;
        }

        return type.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions";
    }

    private bool TryExtractHostedServiceRegistration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType,
        out ExpressionSyntax? factoryExpression)
    {
        serviceType = null;
        implementationType = null;
        factoryExpression = null;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddHostedService" ||
            _hostedServiceType is null ||
            !IsKnownServiceCollectionExtensionsType(sourceMethod.ContainingType))
        {
            return false;
        }

        serviceType = _hostedServiceType;

        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            implementationType = method.TypeArguments[0] as INamedTypeSymbol;
        }

        if (TryGetFactoryArgumentExpression(method, invocation, semanticModel, out var hostedFactoryExpression))
        {
            factoryExpression = hostedFactoryExpression;
        }

        return implementationType is not null || factoryExpression is not null;
    }

    private bool TryExtractHttpClientRegistration(
        IMethodSymbol method,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType)
    {
        serviceType = null;
        implementationType = null;

        var sourceMethod = method.ReducedFrom ?? method;
        if (sourceMethod.Name != "AddHttpClient" ||
            !IsKnownFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType) ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length is not (1 or 2))
        {
            return false;
        }

        serviceType = method.TypeArguments[0] as INamedTypeSymbol;
        implementationType = method.TypeArguments.Length == 2
            ? method.TypeArguments[1] as INamedTypeSymbol
            : serviceType;

        return serviceType is not null && implementationType is not null;
    }

    private bool TryExtractFrameworkSingletonRegistration(
        IMethodSymbol method,
        out INamedTypeSymbol? serviceType)
    {
        serviceType = null;

        var sourceMethod = method.ReducedFrom ?? method;
        if (!IsKnownFrameworkServiceCollectionExtensionsType(sourceMethod.ContainingType))
        {
            return false;
        }

        serviceType = sourceMethod.Name switch
        {
            "AddHttpContextAccessor" => _httpContextAccessorType,
            "AddMemoryCache" => _memoryCacheType,
            "AddLogging" => _loggerFactoryType,
            "AddHttpClient" => _httpClientFactoryType,
            _ => null
        };

        return serviceType is not null;
    }

    private bool IsServiceDescriptorType(ITypeSymbol? type)
    {
        if (_serviceDescriptorType is not null)
        {
            return SymbolEqualityComparer.Default.Equals(type, _serviceDescriptorType);
        }

        return type?.Name == "ServiceDescriptor" &&
               (type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private static bool IsFactoryExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    expression = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                default:
                    return expression is
                        LambdaExpressionSyntax or
                        AnonymousMethodExpressionSyntax or
                        IdentifierNameSyntax or
                        MemberAccessExpressionSyntax;
            }
        }
    }
}
