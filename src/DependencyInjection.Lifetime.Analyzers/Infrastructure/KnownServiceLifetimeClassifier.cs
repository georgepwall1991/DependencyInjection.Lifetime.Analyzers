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

        if (serviceType is not INamedTypeSymbol namedType ||
            isKeyed)
        {
            return false;
        }

        if (namedType.IsGenericType)
        {
            var genericDefinition = namedType.ConstructedFrom;
            if (IsOptionsSnapshot(genericDefinition))
            {
                lifetime = ServiceLifetime.Scoped;
                return true;
            }

            if (IsOptionsOrOptionsMonitor(genericDefinition) ||
                IsLoggerOfT(genericDefinition))
            {
                lifetime = ServiceLifetime.Singleton;
                return true;
            }
        }

        if (IsKnownSingleton(namedType))
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

    private bool IsLoggerOfT(INamedTypeSymbol genericDefinition) =>
        IsKnownGenericType(
            genericDefinition,
            _wellKnownTypes?.ILoggerOfT,
            "Microsoft.Extensions.Logging",
            "ILogger");

    private bool IsKnownSingleton(INamedTypeSymbol type)
    {
        if (_wellKnownTypes is not null &&
            (_wellKnownTypes.IsConfiguration(type) ||
             _wellKnownTypes.IsLogger(type) ||
             _wellKnownTypes.IsLoggerFactory(type) ||
             _wellKnownTypes.IsHttpClientFactory(type) ||
             _wellKnownTypes.IsMemoryCache(type) ||
             _wellKnownTypes.IsHttpContextAccessor(type) ||
             _wellKnownTypes.IsHostApplicationLifetime(type)))
        {
            return true;
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return (type.Name == "IConfiguration" &&
                namespaceName == "Microsoft.Extensions.Configuration") ||
               (type.Name is "ILogger" or "ILoggerFactory" &&
                namespaceName == "Microsoft.Extensions.Logging") ||
               (type.Name == "IHttpClientFactory" &&
                namespaceName == "System.Net.Http") ||
               (type.Name == "IMemoryCache" &&
                namespaceName == "Microsoft.Extensions.Caching.Memory") ||
               (type.Name == "IHttpContextAccessor" &&
                namespaceName == "Microsoft.AspNetCore.Http") ||
               (type.Name is "IHostEnvironment" or "IWebHostEnvironment" &&
                (namespaceName == "Microsoft.Extensions.Hosting" ||
                 namespaceName == "Microsoft.AspNetCore.Hosting")) ||
               (type.Name == "IHostApplicationLifetime" &&
                namespaceName == "Microsoft.Extensions.Hosting");
    }

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
