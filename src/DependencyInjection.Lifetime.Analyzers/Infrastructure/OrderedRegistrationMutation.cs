using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a source-ordered IServiceCollection mutation that can remove an earlier registration.
/// </summary>
public sealed class OrderedRegistrationMutation
{
    public OrderedRegistrationMutation(
        INamedTypeSymbol serviceType,
        object? key,
        bool isKeyed,
        Location location,
        string? flowKey,
        int order,
        RegistrationMutationKind kind)
    {
        ServiceType = serviceType;
        Key = key;
        IsKeyed = isKeyed;
        Location = location;
        FlowKey = flowKey;
        Order = order;
        Kind = kind;
    }

    public INamedTypeSymbol ServiceType { get; }

    public object? Key { get; }

    public bool IsKeyed { get; }

    public Location Location { get; }

    public string? FlowKey { get; }

    public int Order { get; }

    public RegistrationMutationKind Kind { get; }
}
