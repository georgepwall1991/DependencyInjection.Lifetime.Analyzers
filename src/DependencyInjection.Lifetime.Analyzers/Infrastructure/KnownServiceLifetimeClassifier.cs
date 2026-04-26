using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Classifies framework-provided services whose lifetimes are known even when
/// the source registration is hidden inside framework extension methods.
/// </summary>
internal sealed class KnownServiceLifetimeClassifier
{
    private readonly WellKnownTypes? _wellKnownTypes;

    public KnownServiceLifetimeClassifier(WellKnownTypes? wellKnownTypes)
    {
        _wellKnownTypes = wellKnownTypes;
    }

    public bool TryGetLifetime(ITypeSymbol? serviceType, bool isKeyed, out ServiceLifetime lifetime)
    {
        lifetime = default;

        if (serviceType is not INamedTypeSymbol { IsGenericType: true } namedType ||
            isKeyed)
        {
            return false;
        }

        var genericDefinition = namedType.ConstructedFrom;
        if (IsOptionsSnapshot(genericDefinition))
        {
            lifetime = ServiceLifetime.Scoped;
            return true;
        }

        if (IsOptionsOrOptionsMonitor(genericDefinition))
        {
            lifetime = ServiceLifetime.Singleton;
            return true;
        }

        return false;
    }

    private bool IsOptionsSnapshot(INamedTypeSymbol genericDefinition) =>
        IsKnownGenericType(
            genericDefinition,
            _wellKnownTypes?.IOptionsSnapshotOfT,
            "Microsoft.Extensions.Options",
            "IOptionsSnapshot");

    private bool IsOptionsOrOptionsMonitor(INamedTypeSymbol genericDefinition) =>
        IsKnownGenericType(
            genericDefinition,
            _wellKnownTypes?.IOptionsOfT,
            "Microsoft.Extensions.Options",
            "IOptions") ||
        IsKnownGenericType(
            genericDefinition,
            _wellKnownTypes?.IOptionsMonitorOfT,
            "Microsoft.Extensions.Options",
            "IOptionsMonitor");

    private static bool IsKnownGenericType(
        INamedTypeSymbol genericDefinition,
        INamedTypeSymbol? knownType,
        string namespaceName,
        string metadataName)
    {
        if (knownType is not null &&
            SymbolEqualityComparer.Default.Equals(genericDefinition, knownType))
        {
            return true;
        }

        return genericDefinition.Name == metadataName &&
               genericDefinition.ContainingNamespace.ToDisplayString() == namespaceName;
    }
}
