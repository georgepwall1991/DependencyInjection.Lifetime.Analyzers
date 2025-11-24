using Microsoft.CodeAnalysis;

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
    /// Gets the implementation type (may be same as ServiceType for self-registrations).
    /// </summary>
    public INamedTypeSymbol ImplementationType { get; }

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
        INamedTypeSymbol implementationType,
        ServiceLifetime lifetime,
        Location location)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        Location = location;
    }
}
