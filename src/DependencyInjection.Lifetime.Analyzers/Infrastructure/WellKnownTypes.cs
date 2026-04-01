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

    private WellKnownTypes(
        INamedTypeSymbol? serviceProvider,
        INamedTypeSymbol? serviceScopeFactory,
        INamedTypeSymbol? serviceScope,
        INamedTypeSymbol? asyncServiceScope,
        INamedTypeSymbol? disposable,
        INamedTypeSymbol? asyncDisposable,
        INamedTypeSymbol? keyedServiceProvider,
        INamedTypeSymbol? serviceCollection,
        INamedTypeSymbol? serviceCollectionServiceExtensions,
        INamedTypeSymbol? configuration,
        INamedTypeSymbol? logger,
        INamedTypeSymbol? loggerOfT,
        INamedTypeSymbol? optionsOfT,
        INamedTypeSymbol? optionsSnapshotOfT,
        INamedTypeSymbol? optionsMonitorOfT)
    {
        IServiceProvider = serviceProvider;
        IServiceScopeFactory = serviceScopeFactory;
        IServiceScope = serviceScope;
        AsyncServiceScope = asyncServiceScope;
        IDisposable = disposable;
        IAsyncDisposable = asyncDisposable;
        IKeyedServiceProvider = keyedServiceProvider;
        IServiceCollection = serviceCollection;
        ServiceCollectionServiceExtensions = serviceCollectionServiceExtensions;
        IConfiguration = configuration;
        ILogger = logger;
        ILoggerOfT = loggerOfT;
        IOptionsOfT = optionsOfT;
        IOptionsSnapshotOfT = optionsSnapshotOfT;
        IOptionsMonitorOfT = optionsMonitorOfT;
    }

    /// <summary>
    /// Creates a WellKnownTypes instance from the given compilation.
    /// Returns null if the essential DI types are not available.
    /// </summary>
    public static WellKnownTypes? Create(Compilation compilation)
    {
        var serviceProvider = compilation.GetTypeByMetadataName("System.IServiceProvider");
        var serviceScopeFactory = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");
        var serviceScope = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScope");
        var asyncServiceScope = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.AsyncServiceScope");
        var disposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var asyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        var keyedServiceProvider = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider");
        var serviceCollection = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
        var serviceCollectionServiceExtensions = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions");
        var configuration = compilation.GetTypeByMetadataName("Microsoft.Extensions.Configuration.IConfiguration");
        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        var loggerOfT = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger`1");
        var optionsOfT = compilation.GetTypeByMetadataName("Microsoft.Extensions.Options.IOptions`1");
        var optionsSnapshotOfT = compilation.GetTypeByMetadataName("Microsoft.Extensions.Options.IOptionsSnapshot`1");
        var optionsMonitorOfT = compilation.GetTypeByMetadataName("Microsoft.Extensions.Options.IOptionsMonitor`1");

        // Return null if we don't have the basic types needed for analysis
        if (serviceProvider is null && serviceScopeFactory is null)
        {
            return null;
        }

        return new WellKnownTypes(
            serviceProvider,
            serviceScopeFactory,
            serviceScope,
            asyncServiceScope,
            disposable,
            asyncDisposable,
            keyedServiceProvider,
            serviceCollection,
            serviceCollectionServiceExtensions,
            configuration,
            logger,
            loggerOfT,
            optionsOfT,
            optionsSnapshotOfT,
            optionsMonitorOfT);
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
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IServiceScopeFactory);
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
        return type is not null && SymbolEqualityComparer.Default.Equals(type, IKeyedServiceProvider);
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
        return type is not null && SymbolEqualityComparer.Default.Equals(type, ServiceCollectionServiceExtensions);
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

        return type is INamedTypeSymbol { IsGenericType: true } namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, ILoggerOfT);
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
        return SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsOfT) ||
               SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsSnapshotOfT) ||
               SymbolEqualityComparer.Default.Equals(constructedFrom, IOptionsMonitorOfT);
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
            if (SymbolEqualityComparer.Default.Equals(iface, IServiceProvider) ||
                SymbolEqualityComparer.Default.Equals(iface, IServiceScopeFactory) ||
                SymbolEqualityComparer.Default.Equals(iface, IKeyedServiceProvider))
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

        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, IAsyncDisposable));
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
}
