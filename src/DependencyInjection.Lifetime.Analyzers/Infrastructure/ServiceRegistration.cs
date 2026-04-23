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
    /// Gets whether this registration is backed by a pre-built implementation instance.
    /// </summary>
    public bool HasImplementationInstance { get; }

    /// <summary>
    /// Gets the key of the registration if it is a keyed service.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Gets a C# literal for the key when it can be safely round-tripped into code.
    /// </summary>
    public string? KeyLiteral { get; }

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
    /// Gets the analyzed service-collection flow key for this registration.
    /// </summary>
    public string? FlowKey { get; }

    /// <summary>
    /// Gets the order in which this registration was encountered.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Creates a new service registration.
    /// </summary>
    public ServiceRegistration(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol? implementationType,
        ExpressionSyntax? factoryExpression,
        bool hasImplementationInstance,
        object? key,
        bool isKeyed,
        ServiceLifetime lifetime,
        Location location,
        string? keyLiteral = null,
        string? flowKey = null,
        int order = 0)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        FactoryExpression = factoryExpression;
        HasImplementationInstance = hasImplementationInstance;
        Key = key;
        KeyLiteral = keyLiteral;
        IsKeyed = isKeyed;
        Lifetime = lifetime;
        Location = location;
        FlowKey = flowKey;
        Order = order;
    }
}
