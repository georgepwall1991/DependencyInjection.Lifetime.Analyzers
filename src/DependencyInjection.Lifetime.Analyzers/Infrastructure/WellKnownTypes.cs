using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Caches well-known type symbols for DI-related types.
/// </summary>
public sealed class WellKnownTypes
{
    /// <summary>
    /// Gets the IServiceProvider type symbol.
    /// </summary>
    public INamedTypeSymbol? IServiceProvider { get; }

    /// <summary>
    /// Gets the IServiceScopeFactory type symbol.
    /// </summary>
    public INamedTypeSymbol? IServiceScopeFactory { get; }

    /// <summary>
    /// Gets the IServiceScope type symbol.
    /// </summary>
    public INamedTypeSymbol? IServiceScope { get; }

    /// <summary>
    /// Gets the AsyncServiceScope type symbol.
    /// </summary>
    public INamedTypeSymbol? AsyncServiceScope { get; }

    /// <summary>
    /// Gets the IDisposable type symbol.
    /// </summary>
    public INamedTypeSymbol? IDisposable { get; }

    /// <summary>
    /// Gets the IAsyncDisposable type symbol.
    /// </summary>
    public INamedTypeSymbol? IAsyncDisposable { get; }

    /// <summary>
    /// Gets the IKeyedServiceProvider type symbol (.NET 8+).
    /// </summary>
    public INamedTypeSymbol? IKeyedServiceProvider { get; }

    /// <summary>
    /// Gets the IServiceProviderIsService type symbol.
    /// </summary>
    public INamedTypeSymbol? IServiceProviderIsService { get; }

    /// <summary>
    /// Gets the IServiceProviderIsKeyedService type symbol (.NET 8+).
    /// </summary>
    public INamedTypeSymbol? IServiceProviderIsKeyedService { get; }

    /// <summary>
    /// Gets the IServiceCollection type symbol.
    /// </summary>
    public INamedTypeSymbol? IServiceCollection { get; }

    /// <summary>
    /// Gets the ServiceCollectionServiceExtensions type symbol.
    /// </summary>
    public INamedTypeSymbol? ServiceCollectionServiceExtensions { get; }

    /// <summary>
    /// Gets the IConfiguration type symbol.
    /// </summary>
    public INamedTypeSymbol? IConfiguration { get; }

    /// <summary>
    /// Gets the ILogger type symbol.
    /// </summary>
    public INamedTypeSymbol? ILogger { get; }

    /// <summary>
    /// Gets the ILogger{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? ILoggerOfT { get; }

    /// <summary>
    /// Gets the IOptions{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? IOptionsOfT { get; }

    /// <summary>
    /// Gets the IOptionsSnapshot{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? IOptionsSnapshotOfT { get; }

    /// <summary>
    /// Gets the IOptionsMonitor{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? IOptionsMonitorOfT { get; }

    /// <summary>
    /// Gets the ILoggerFactory type symbol.
    /// </summary>
    public INamedTypeSymbol? ILoggerFactory { get; }

    /// <summary>
    /// Gets the IHttpClientFactory type symbol.
    /// </summary>
    public INamedTypeSymbol? IHttpClientFactory { get; }

    /// <summary>
    /// Gets the IMemoryCache type symbol.
    /// </summary>
    public INamedTypeSymbol? IMemoryCache { get; }

    /// <summary>
    /// Gets the IHttpContextAccessor type symbol.
    /// </summary>
    public INamedTypeSymbol? IHttpContextAccessor { get; }

    /// <summary>
    /// Gets the IHostApplicationLifetime type symbol.
    /// </summary>
    public INamedTypeSymbol? IHostApplicationLifetime { get; }

    // The symbols below are set through an object initializer in Create rather than through the
    // positional constructor above. The constructor already carries twenty-two parameters; threading
    // sixteen more through it would make every call site unreadable and every future addition a
    // wide, merge-hostile edit. They stay optional: each consumer gates on its own symbols.

    /// <summary>
    /// Gets the Microsoft.Extensions.Primitives.IChangeToken type symbol.
    /// </summary>
    public INamedTypeSymbol? IChangeToken { get; private set; }

    /// <summary>
    /// Gets the Microsoft.Extensions.Primitives.ChangeToken static helper type symbol.
    /// </summary>
    public INamedTypeSymbol? ChangeToken { get; private set; }

    /// <summary>
    /// Gets the System.Threading.CancellationToken type symbol.
    /// </summary>
    public INamedTypeSymbol? CancellationToken { get; private set; }

    /// <summary>
    /// Gets the System.Threading.CancellationTokenSource type symbol.
    /// </summary>
    public INamedTypeSymbol? CancellationTokenSource { get; private set; }

    /// <summary>
    /// Gets the System.Threading.CancellationTokenRegistration type symbol.
    /// </summary>
    public INamedTypeSymbol? CancellationTokenRegistration { get; private set; }

    /// <summary>
    /// Gets the System.Net.Http.HttpClient type symbol.
    /// </summary>
    public INamedTypeSymbol? HttpClient { get; private set; }

    /// <summary>
    /// Gets the System.Net.Http.HttpMessageHandler type symbol.
    /// </summary>
    public INamedTypeSymbol? HttpMessageHandler { get; private set; }

    /// <summary>
    /// Gets the System.Net.Http.HttpClientHandler type symbol.
    /// </summary>
    public INamedTypeSymbol? HttpClientHandler { get; private set; }

    /// <summary>
    /// Gets the System.Net.Http.SocketsHttpHandler type symbol.
    /// </summary>
    public INamedTypeSymbol? SocketsHttpHandler { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue} type symbol.
    /// </summary>
    public INamedTypeSymbol? ConcurrentDictionaryOfTKeyTValue { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Generic.Dictionary{TKey, TValue} type symbol.
    /// </summary>
    public INamedTypeSymbol? DictionaryOfTKeyTValue { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Generic.List{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? ListOfT { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Generic.HashSet{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? HashSetOfT { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Generic.Queue{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? QueueOfT { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Concurrent.ConcurrentBag{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? ConcurrentBagOfT { get; private set; }

    /// <summary>
    /// Gets the System.Collections.Concurrent.ConcurrentQueue{T} type symbol.
    /// </summary>
    public INamedTypeSymbol? ConcurrentQueueOfT { get; private set; }

    /// <summary>
    /// Gets the Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions type symbol.
    /// </summary>
    public INamedTypeSymbol? MemoryCacheEntryOptions { get; private set; }

    /// <summary>
    /// Gets the Microsoft.Extensions.Caching.Memory.ICacheEntry type symbol.
    /// </summary>
    public INamedTypeSymbol? ICacheEntry { get; private set; }

    /// <summary>
    /// Gets the Microsoft.Extensions.Caching.Memory.MemoryCacheOptions type symbol.
    /// </summary>
    public INamedTypeSymbol? MemoryCacheOptions { get; private set; }

    private WellKnownTypes(
        INamedTypeSymbol? serviceProvider,
        INamedTypeSymbol? serviceScopeFactory,
        INamedTypeSymbol? serviceScope,
        INamedTypeSymbol? asyncServiceScope,
        INamedTypeSymbol? disposable,
        INamedTypeSymbol? asyncDisposable,
        INamedTypeSymbol? keyedServiceProvider,
        INamedTypeSymbol? serviceProviderIsService,
        INamedTypeSymbol? serviceProviderIsKeyedService,
        INamedTypeSymbol? serviceCollection,
        INamedTypeSymbol? serviceCollectionServiceExtensions,
        INamedTypeSymbol? configuration,
        INamedTypeSymbol? logger,
        INamedTypeSymbol? loggerOfT,
        INamedTypeSymbol? optionsOfT,
        INamedTypeSymbol? optionsSnapshotOfT,
        INamedTypeSymbol? optionsMonitorOfT,
        INamedTypeSymbol? loggerFactory,
        INamedTypeSymbol? httpClientFactory,
        INamedTypeSymbol? memoryCache,
        INamedTypeSymbol? httpContextAccessor,
        INamedTypeSymbol? hostApplicationLifetime
    )
    {
        IServiceProvider = serviceProvider;
        IServiceScopeFactory = serviceScopeFactory;
        IServiceScope = serviceScope;
        AsyncServiceScope = asyncServiceScope;
        IDisposable = disposable;
        IAsyncDisposable = asyncDisposable;
        IKeyedServiceProvider = keyedServiceProvider;
        IServiceProviderIsService = serviceProviderIsService;
        IServiceProviderIsKeyedService = serviceProviderIsKeyedService;
        IServiceCollection = serviceCollection;
        ServiceCollectionServiceExtensions = serviceCollectionServiceExtensions;
        IConfiguration = configuration;
        ILogger = logger;
        ILoggerOfT = loggerOfT;
        IOptionsOfT = optionsOfT;
        IOptionsSnapshotOfT = optionsSnapshotOfT;
        IOptionsMonitorOfT = optionsMonitorOfT;
        ILoggerFactory = loggerFactory;
        IHttpClientFactory = httpClientFactory;
        IMemoryCache = memoryCache;
        IHttpContextAccessor = httpContextAccessor;
        IHostApplicationLifetime = hostApplicationLifetime;
    }

    /// <summary>
    /// Creates a WellKnownTypes instance from the given compilation.
    /// Returns null if the essential DI types are not available.
    /// </summary>
    public static WellKnownTypes? Create(Compilation compilation)
    {
        var serviceProvider = compilation.GetTypeByMetadataName("System.IServiceProvider");
        var serviceScopeFactory = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
        );
        var serviceScope = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScope"
        );
        var asyncServiceScope = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.AsyncServiceScope"
        );
        var disposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var asyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        var keyedServiceProvider = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider"
        );
        var serviceProviderIsService = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceProviderIsService"
        );
        var serviceProviderIsKeyedService = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService"
        );
        var serviceCollection = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection"
        );
        var serviceCollectionServiceExtensions = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
        );
        var configuration = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Configuration.IConfiguration"
        );
        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        var loggerOfT = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger`1");
        var optionsOfT = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Options.IOptions`1"
        );
        var optionsSnapshotOfT = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Options.IOptionsSnapshot`1"
        );
        var optionsMonitorOfT = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Options.IOptionsMonitor`1"
        );
        var loggerFactory = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Logging.ILoggerFactory"
        );
        var httpClientFactory = compilation.GetTypeByMetadataName(
            "System.Net.Http.IHttpClientFactory"
        );
        var memoryCache = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Caching.Memory.IMemoryCache"
        );
        var httpContextAccessor = compilation.GetTypeByMetadataName(
            "Microsoft.AspNetCore.Http.IHttpContextAccessor"
        );
        var hostApplicationLifetime = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Hosting.IHostApplicationLifetime"
        );

        // Return null if we don't have the basic types needed for analysis
        if (serviceProvider is null && serviceScopeFactory is null)
        {
            return null;
        }

        return WithOptionalSymbols(
            compilation,
            new WellKnownTypes(
                serviceProvider,
                serviceScopeFactory,
                serviceScope,
                asyncServiceScope,
                disposable,
                asyncDisposable,
                keyedServiceProvider,
                serviceProviderIsService,
                serviceProviderIsKeyedService,
                serviceCollection,
                serviceCollectionServiceExtensions,
                configuration,
                logger,
                loggerOfT,
                optionsOfT,
                optionsSnapshotOfT,
                optionsMonitorOfT,
                loggerFactory,
                httpClientFactory,
                memoryCache,
                httpContextAccessor,
                hostApplicationLifetime
            )
        );
    }

    /// <summary>
    /// Populates the symbols that are resolved outside the positional constructor. Every symbol here
    /// is optional: a compilation that does not reference Primitives, Hosting, Http, or Caching still
    /// produces a usable instance, and each consumer gates on the symbols it actually needs.
    /// </summary>
    private static WellKnownTypes WithOptionalSymbols(Compilation compilation, WellKnownTypes types)
    {
        types.IChangeToken = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Primitives.IChangeToken"
        );
        types.ChangeToken = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Primitives.ChangeToken"
        );
        types.CancellationToken = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationToken"
        );
        types.CancellationTokenSource = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationTokenSource"
        );
        types.CancellationTokenRegistration = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationTokenRegistration"
        );
        types.HttpClient = compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");
        types.HttpMessageHandler = compilation.GetTypeByMetadataName(
            "System.Net.Http.HttpMessageHandler"
        );
        types.HttpClientHandler = compilation.GetTypeByMetadataName(
            "System.Net.Http.HttpClientHandler"
        );
        types.SocketsHttpHandler = compilation.GetTypeByMetadataName(
            "System.Net.Http.SocketsHttpHandler"
        );
        types.ConcurrentDictionaryOfTKeyTValue = compilation.GetTypeByMetadataName(
            "System.Collections.Concurrent.ConcurrentDictionary`2"
        );
        types.DictionaryOfTKeyTValue = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.Dictionary`2"
        );
        types.ListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        types.HashSetOfT = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.HashSet`1"
        );
        types.QueueOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.Queue`1");
        types.ConcurrentBagOfT = compilation.GetTypeByMetadataName(
            "System.Collections.Concurrent.ConcurrentBag`1"
        );
        types.ConcurrentQueueOfT = compilation.GetTypeByMetadataName(
            "System.Collections.Concurrent.ConcurrentQueue`1"
        );
        types.MemoryCacheEntryOptions = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions"
        );
        types.ICacheEntry = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Caching.Memory.ICacheEntry"
        );
        types.MemoryCacheOptions = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Caching.Memory.MemoryCacheOptions"
        );

        return types;
    }

    /// <summary>
    /// Checks if the given type is IServiceProvider.
    /// </summary>
    public bool IsServiceProvider(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IServiceProvider);
    }

    /// <summary>
    /// Checks if the given type is IServiceScopeFactory.
    /// </summary>
    public bool IsServiceScopeFactory(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, IServiceScopeFactory);
    }

    /// <summary>
    /// Checks if the given type is IServiceScope.
    /// </summary>
    public bool IsServiceScope(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IServiceScope);
    }

    /// <summary>
    /// Checks if the given type is AsyncServiceScope.
    /// </summary>
    public bool IsAsyncServiceScope(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, AsyncServiceScope);
    }

    /// <summary>
    /// Checks if the given type is any service provider or scope factory type.
    /// </summary>
    public bool IsServiceProviderOrFactory(ITypeSymbol? type)
    {
        return IsServiceProvider(type) || IsServiceScopeFactory(type);
    }

    /// <summary>
    /// Checks if the given type is IKeyedServiceProvider.
    /// </summary>
    public bool IsKeyedServiceProvider(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, IKeyedServiceProvider);
    }

    /// <summary>
    /// Checks if the given type is provided by the container for service availability inspection.
    /// </summary>
    public bool IsServiceProviderInspectionService(ITypeSymbol? type)
    {
        return type is not null
            && (
                SymbolEqualityComparer.Default.Equals(type, IServiceProviderIsService)
                || SymbolEqualityComparer.Default.Equals(type, IServiceProviderIsKeyedService)
            );
    }

    /// <summary>
    /// Checks if the given type is IServiceCollection.
    /// </summary>
    public bool IsServiceCollection(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IServiceCollection);
    }

    /// <summary>
    /// Checks if the given type is ServiceCollectionServiceExtensions.
    /// </summary>
    public bool IsServiceCollectionServiceExtensions(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, ServiceCollectionServiceExtensions);
    }

    /// <summary>
    /// Checks if the given type is IConfiguration.
    /// </summary>
    public bool IsConfiguration(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IConfiguration);
    }

    /// <summary>
    /// Checks if the given type is ILogger or ILogger{T}.
    /// </summary>
    public bool IsLogger(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, ILogger))
        {
            return true;
        }

        return type is INamedTypeSymbol { IsGenericType: true } namedType
            && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, ILoggerOfT);
    }

    /// <summary>
    /// Checks if the given type is ILoggerFactory.
    /// </summary>
    public bool IsLoggerFactory(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, ILoggerFactory);
    }

    /// <summary>
    /// Checks if the given type is IHttpClientFactory.
    /// </summary>
    public bool IsHttpClientFactory(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IHttpClientFactory);
    }

    /// <summary>
    /// Checks if the given type is IMemoryCache.
    /// </summary>
    public bool IsMemoryCache(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IMemoryCache);
    }

    /// <summary>
    /// Checks if the given type is IHttpContextAccessor.
    /// </summary>
    public bool IsHttpContextAccessor(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, IHttpContextAccessor);
    }

    /// <summary>
    /// Checks if the given type is IHostApplicationLifetime.
    /// </summary>
    public bool IsHostApplicationLifetime(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, IHostApplicationLifetime);
    }

    /// <summary>
    /// Checks if the given type is IOptions{T}, IOptionsSnapshot{T}, or IOptionsMonitor{T}.
    /// </summary>
    public bool IsOptionsAbstraction(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return false;
        }

        var constructedFrom = namedType.ConstructedFrom;
        return SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsOfT)
            || SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsSnapshotOfT)
            || SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsMonitorOfT);
    }

    /// <summary>
    /// Checks if the given type is any service provider type (including keyed).
    /// </summary>
    public bool IsAnyServiceProvider(ITypeSymbol? type)
    {
        return IsServiceProvider(type) || IsKeyedServiceProvider(type);
    }

    /// <summary>
    /// Checks if the given type is any service provider, keyed provider, or scope factory type.
    /// Also returns true for types that inherit from or implement these interfaces.
    /// </summary>
    public bool IsServiceProviderOrFactoryOrKeyed(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (IsServiceProvider(type) || IsServiceScopeFactory(type) || IsKeyedServiceProvider(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (
                SymbolEqualityComparer.Default.Equals(iface, IServiceProvider)
                || SymbolEqualityComparer.Default.Equals(iface, IServiceScopeFactory)
                || SymbolEqualityComparer.Default.Equals(iface, IKeyedServiceProvider)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the given type implements IDisposable.
    /// </summary>
    public bool ImplementsIDisposable(ITypeSymbol? type)
    {
        if (type is null || IDisposable is null)
            return false;

        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, IDisposable));
    }

    /// <summary>
    /// Checks if the given type implements IAsyncDisposable.
    /// </summary>
    public bool ImplementsIAsyncDisposable(ITypeSymbol? type)
    {
        if (type is null || IAsyncDisposable is null)
            return false;

        return type.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i, IAsyncDisposable)
        );
    }

    /// <summary>
    /// Gets the disposable interface name if the type implements either IDisposable or IAsyncDisposable.
    /// Returns null if the type implements neither.
    /// </summary>
    public string? GetDisposableInterfaceName(ITypeSymbol? type)
    {
        if (type is null)
            return null;

        if (ImplementsIAsyncDisposable(type))
            return "IAsyncDisposable";

        if (ImplementsIDisposable(type))
            return "IDisposable";

        return null;
    }

    /// <summary>
    /// Matches a symbol by name and containing namespace. Used as a fallback for the framework
    /// abstractions a project may model locally instead of taking the package reference: symbol
    /// equality fails there because the stub is source-declared, but the contract is the same one.
    /// Only ever used after symbol equality has already been tried.
    /// </summary>
    private static bool MatchesByNameAndNamespace(
        ISymbol? symbol,
        string name,
        string containingNamespace
    )
    {
        return symbol is not null
            && symbol.Name == name
            && symbol.ContainingNamespace?.ToDisplayString() == containingNamespace;
    }

    /// <summary>
    /// Checks if the given type is IOptionsMonitor{T}. Narrower than
    /// <see cref="IsOptionsAbstraction"/> on purpose: IOptions{T} has no change notification and
    /// IOptionsSnapshot{T} is scoped, so only the monitor can pin a subscriber for process lifetime.
    /// </summary>
    public bool IsOptionsMonitor(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return false;
        }

        var constructedFrom = namedType.ConstructedFrom;
        return SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsMonitorOfT)
            || MatchesByNameAndNamespace(
                constructedFrom,
                "IOptionsMonitor",
                "Microsoft.Extensions.Options"
            );
    }

    /// <summary>
    /// Checks if the given type is, or implements, IChangeToken.
    /// </summary>
    public bool ImplementsChangeToken(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (
            SymbolEqualityComparer.Default.Equals(type, IChangeToken)
            || MatchesByNameAndNamespace(type, "IChangeToken", "Microsoft.Extensions.Primitives")
        )
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (
                SymbolEqualityComparer.Default.Equals(iface, IChangeToken)
                || MatchesByNameAndNamespace(
                    iface,
                    "IChangeToken",
                    "Microsoft.Extensions.Primitives"
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the given type is the static ChangeToken helper class that owns ChangeToken.OnChange.
    /// </summary>
    public bool IsChangeTokenStaticHelper(ITypeSymbol? type)
    {
        return type is not null
            && (
                SymbolEqualityComparer.Default.Equals(type, ChangeToken)
                || MatchesByNameAndNamespace(type, "ChangeToken", "Microsoft.Extensions.Primitives")
            );
    }

    /// <summary>
    /// Checks if the given type is System.Threading.CancellationToken.
    /// </summary>
    public bool IsCancellationToken(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, CancellationToken);
    }

    /// <summary>
    /// Checks if the given type is System.Threading.CancellationTokenSource.
    /// </summary>
    public bool IsCancellationTokenSource(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, CancellationTokenSource);
    }

    /// <summary>
    /// Checks if the given type is System.Threading.CancellationTokenRegistration.
    /// </summary>
    public bool IsCancellationTokenRegistration(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, CancellationTokenRegistration);
    }

    /// <summary>
    /// Checks if the given type is IHostApplicationLifetime, including a source-declared stub of it.
    /// Kept separate from <see cref="IsHostApplicationLifetime"/> so the exact-symbol behavior that
    /// existing rules depend on stays unchanged.
    /// </summary>
    public bool IsHostApplicationLifetimeIncludingSourceDeclared(ITypeSymbol? type)
    {
        return IsHostApplicationLifetime(type)
            || MatchesByNameAndNamespace(
                type,
                "IHostApplicationLifetime",
                "Microsoft.Extensions.Hosting"
            );
    }

    /// <summary>
    /// Checks if the given type is exactly System.Net.Http.HttpClient. Derived types are excluded:
    /// a subclass may own a handler whose rotation the analyzer cannot reason about.
    /// </summary>
    public bool IsHttpClient(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, HttpClient);
    }

    /// <summary>
    /// Checks if the given type is exactly System.Net.Http.HttpMessageHandler.
    /// </summary>
    public bool IsHttpMessageHandler(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, HttpMessageHandler);
    }

    /// <summary>
    /// Checks if the given type derives from HttpMessageHandler. Used to recognize a handler passed
    /// in by the caller, which transfers socket-pool ownership away from the construction site.
    /// </summary>
    public bool DerivesFromHttpMessageHandler(ITypeSymbol? type)
    {
        if (type is null || HttpMessageHandler is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, HttpMessageHandler))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the given type is one of the concrete handlers that owns a connection pool and is
    /// rotated by IHttpClientFactory: HttpClientHandler or SocketsHttpHandler.
    /// </summary>
    public bool IsRotatableHandler(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(type, HttpClientHandler)
            || SymbolEqualityComparer.Default.Equals(type, SocketsHttpHandler);
    }

    /// <summary>
    /// Checks if constructing the given type consumes a socket or connection pool that the container
    /// would otherwise own and rotate.
    /// </summary>
    public bool IsSocketOwningCreationType(ITypeSymbol? type)
    {
        return IsHttpClient(type) || IsRotatableHandler(type);
    }

    /// <summary>
    /// Checks if the given type is, or implements, IMemoryCache. Broader than
    /// <see cref="IsMemoryCache"/> so a concrete MemoryCache receiver matches too.
    /// </summary>
    public bool ImplementsMemoryCache(ITypeSymbol? type)
    {
        if (type is null || IMemoryCache is null)
        {
            return false;
        }

        return IsMemoryCache(type)
            || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, IMemoryCache));
    }

    /// <summary>
    /// Checks if the given type is MemoryCacheEntryOptions.
    /// </summary>
    public bool IsMemoryCacheEntryOptions(ITypeSymbol? type)
    {
        return type is not null
            && SymbolEqualityComparer.Default.Equals(type, MemoryCacheEntryOptions);
    }

    /// <summary>
    /// Checks if the given type is ICacheEntry.
    /// </summary>
    public bool IsCacheEntry(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, ICacheEntry);
    }

    /// <summary>
    /// Checks if the given type is MemoryCacheOptions.
    /// </summary>
    public bool IsMemoryCacheOptions(ITypeSymbol? type)
    {
        return type is not null && SymbolEqualityComparer.Default.Equals(type, MemoryCacheOptions);
    }

    /// <summary>
    /// Checks if the given type is one of the mutable collections whose growth is unbounded, and
    /// reports the element type a caller would key or store into it. Interfaces are excluded on
    /// purpose: an IDictionary-typed member may be backed by a frozen or size-capped implementation,
    /// so only the concrete constructions are recognized.
    /// </summary>
    public bool IsUnboundedGrowthCollection(ITypeSymbol? type, out ITypeSymbol? elementType)
    {
        elementType = null;

        if (type is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return false;
        }

        var definition = namedType.OriginalDefinition;
        var matches =
            SymbolEqualityComparer.Default.Equals(definition, ConcurrentDictionaryOfTKeyTValue)
            || SymbolEqualityComparer.Default.Equals(definition, DictionaryOfTKeyTValue)
            || SymbolEqualityComparer.Default.Equals(definition, ListOfT)
            || SymbolEqualityComparer.Default.Equals(definition, HashSetOfT)
            || SymbolEqualityComparer.Default.Equals(definition, QueueOfT)
            || SymbolEqualityComparer.Default.Equals(definition, ConcurrentBagOfT)
            || SymbolEqualityComparer.Default.Equals(definition, ConcurrentQueueOfT);

        if (!matches || namedType.TypeArguments.Length == 0)
        {
            return false;
        }

        // For dictionaries the stored value is the last type argument; for the single-argument
        // collections it is the only one.
        elementType = namedType.TypeArguments[namedType.TypeArguments.Length - 1];
        return true;
    }
}
