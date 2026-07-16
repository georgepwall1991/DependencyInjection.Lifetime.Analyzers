using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a discovered dependency-injection service registration.
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
    /// Gets whether this registration should be ignored when an earlier registration
    /// for the same service/key already exists in source order.
    /// </summary>
    public bool SkipIfAlreadyRegistered { get; }

    /// <summary>
    /// Gets whether this registration came from a TryAdd* call.
    /// </summary>
    public bool IsTryAdd { get; }

    /// <summary>
    /// Gets whether this registration should be ignored only when an earlier registration
    /// for the same service/key and implementation type already exists in source order.
    /// </summary>
    public bool SkipIfSameImplementationAlreadyRegistered { get; }

    /// <summary>
    /// Gets whether this descriptor was inserted at collection index zero instead of
    /// appended. Multiple prepend operations appear in reverse execution order in the
    /// final descriptor list.
    /// </summary>
    public bool PrependToCollection { get; }

    /// <summary>
    /// Gets whether <see cref="ImplementationType"/> is provably the runtime type for an
    /// instance-backed registration (the instance expression is an object creation, or its
    /// static type is sealed or a value type). When false, the recorded type is only the
    /// instance expression's static type — the runtime type may be any subtype, so
    /// incompatibility with the service type cannot be proven. Always true for
    /// type-based registrations.
    /// </summary>
    public bool ImplementationInstanceTypeIsExact { get; }

    /// <summary>
    /// Gets constructor parameter types supplied by a framework factory for this registration.
    /// </summary>
    public ImmutableArray<ITypeSymbol> FactoryProvidedParameterTypes { get; }

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
        int order = 0,
        bool skipIfAlreadyRegistered = false,
        bool isTryAdd = false,
        bool skipIfSameImplementationAlreadyRegistered = false,
        bool implementationInstanceTypeIsExact = true,
        ImmutableArray<ITypeSymbol> factoryProvidedParameterTypes = default,
        bool prependToCollection = false
    )
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
        SkipIfAlreadyRegistered = skipIfAlreadyRegistered;
        IsTryAdd = isTryAdd;
        SkipIfSameImplementationAlreadyRegistered = skipIfSameImplementationAlreadyRegistered;
        PrependToCollection = prependToCollection;
        ImplementationInstanceTypeIsExact = implementationInstanceTypeIsExact;
        FactoryProvidedParameterTypes = factoryProvidedParameterTypes.IsDefault
            ? ImmutableArray<ITypeSymbol>.Empty
            : factoryProvidedParameterTypes;
    }
}
