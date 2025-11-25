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

    private WellKnownTypes(
        INamedTypeSymbol? serviceProvider,
        INamedTypeSymbol? serviceScopeFactory,
        INamedTypeSymbol? serviceScope,
        INamedTypeSymbol? asyncServiceScope,
        INamedTypeSymbol? disposable,
        INamedTypeSymbol? asyncDisposable,
        INamedTypeSymbol? keyedServiceProvider)
    {
        IServiceProvider = serviceProvider;
        IServiceScopeFactory = serviceScopeFactory;
        IServiceScope = serviceScope;
        AsyncServiceScope = asyncServiceScope;
        IDisposable = disposable;
        IAsyncDisposable = asyncDisposable;
        IKeyedServiceProvider = keyedServiceProvider;
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

        // Return null if we don't have the basic types needed for analysis
        if (serviceProvider is null && serviceScopeFactory is null)
        {
            return null;
        }

        return new WellKnownTypes(serviceProvider, serviceScopeFactory, serviceScope, asyncServiceScope, disposable, asyncDisposable, keyedServiceProvider);
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
    /// Checks if the given type is any service provider type (including keyed).
    /// </summary>
    public bool IsAnyServiceProvider(ITypeSymbol? type)
    {
        return IsServiceProvider(type) || IsKeyedServiceProvider(type);
    }

    /// <summary>
    /// Checks if the given type is any service provider, keyed provider, or scope factory type.
    /// </summary>
    public bool IsServiceProviderOrFactoryOrKeyed(ITypeSymbol? type)
    {
        return IsServiceProvider(type) || IsServiceScopeFactory(type) || IsKeyedServiceProvider(type);
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
