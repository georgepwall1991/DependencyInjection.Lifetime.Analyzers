using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a service registration with ordering information for tracking registration order.
/// Used by DI012 to detect TryAdd after Add and duplicate registrations.
/// </summary>
public sealed class OrderedRegistration
{
    /// <summary>
    /// Gets the service type being registered.
    /// </summary>
    public INamedTypeSymbol ServiceType { get; }

    /// <summary>
    /// Gets the lifetime of the registration.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Gets the location of the registration call in source code.
    /// </summary>
    public Location Location { get; }

    /// <summary>
    /// Gets the analyzed service-collection flow key for this registration.
    /// Registrations on different flows should not cross-trigger DI012.
    /// </summary>
    public string? FlowKey { get; }

    /// <summary>
    /// Gets the order in which this registration was encountered (0-based).
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets whether this is a TryAdd* registration (true) or Add* registration (false).
    /// </summary>
    public bool IsTryAdd { get; }

    /// <summary>
    /// Gets the method name used for registration (e.g., "AddSingleton", "TryAddScoped").
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the key of the registration if it is a keyed service.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Gets whether this registration is for a keyed service.
    /// </summary>
    public bool IsKeyed { get; }

    /// <summary>
    /// Creates a new ordered registration.
    /// </summary>
    public OrderedRegistration(
        INamedTypeSymbol serviceType,
        object? key,
        bool isKeyed,
        ServiceLifetime lifetime,
        Location location,
        string? flowKey,
        int order,
        bool isTryAdd,
        string methodName)
    {
        ServiceType = serviceType;
        Key = key;
        IsKeyed = isKeyed;
        Lifetime = lifetime;
        Location = location;
        FlowKey = flowKey;
        Order = order;
        IsTryAdd = isTryAdd;
        MethodName = methodName;
    }
}
