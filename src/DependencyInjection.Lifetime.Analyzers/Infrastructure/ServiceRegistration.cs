using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a discovered service registration from AddSingleton/AddScoped/AddTransient calls.
/// </summary>
public sealed class ServiceRegistration
{
    /// <summary>
    /// Gets the service type being registered (the interface or abstract type).
    /// </summary>
    public INamedTypeSymbol ServiceType { get; }

    /// <summary>
    /// Gets the implementation type. Null if this is a factory registration.
    /// </summary>
    public INamedTypeSymbol? ImplementationType { get; }

    /// <summary>
    /// Gets the factory expression if this is a factory registration.
    /// </summary>
    public ExpressionSyntax? FactoryExpression { get; }

    /// <summary>
    /// Gets the key of the registration if it is a keyed service.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Gets whether this registration is keyed.
    /// </summary>
    public bool IsKeyed { get; }

    /// <summary>
    /// Gets the lifetime of the registration.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Gets the location of the registration call in source code.
    /// </summary>
    public Location Location { get; }

    /// <summary>
    /// Creates a new service registration.
    /// </summary>
    public ServiceRegistration(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol? implementationType,
        ExpressionSyntax? factoryExpression,
        object? key,
        bool isKeyed,
        ServiceLifetime lifetime,
        Location location)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        FactoryExpression = factoryExpression;
        Key = key;
        IsKeyed = isKeyed;
        Lifetime = lifetime;
        Location = location;
    }
}
