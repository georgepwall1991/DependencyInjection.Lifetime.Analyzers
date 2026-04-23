using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Helpers for extracting keyed service information from parameter symbols.
/// </summary>
internal static class KeyedServiceHelpers
{
    public readonly struct ServiceKeyRequest
    {
        public ServiceKeyRequest(
            object? key,
            bool isKeyed,
            bool isUnknown,
            string? keyLiteral)
        {
            Key = key;
            IsKeyed = isKeyed;
            IsUnknown = isUnknown;
            KeyLiteral = keyLiteral;
        }

        public object? Key { get; }

        public bool IsKeyed { get; }

        public bool IsUnknown { get; }

        public string? KeyLiteral { get; }
    }

    /// <summary>
    /// Extracts the service key and keyed status from a parameter's [FromKeyedServices] attribute.
    /// </summary>
    public static ServiceKeyRequest GetServiceKey(
        IParameterSymbol parameter,
        object? inheritedKey,
        bool hasInheritedKey,
        string? inheritedKeyLiteral)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var key = attribute.ConstructorArguments[0].Value;
                    var literal = SyntaxValueHelpers.TryFormatCSharpLiteral(key, out var formatted)
                        ? formatted
                        : null;
                    return new ServiceKeyRequest(key, isKeyed: true, isUnknown: false, literal);
                }

                return hasInheritedKey
                    ? new ServiceKeyRequest(inheritedKey, isKeyed: true, isUnknown: false, inheritedKeyLiteral)
                    : new ServiceKeyRequest(null, isKeyed: true, isUnknown: true, keyLiteral: null);
            }
        }

        return new ServiceKeyRequest(null, isKeyed: false, isUnknown: false, keyLiteral: null);
    }

    public static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        var request = GetServiceKey(
            parameter,
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null);

        return (request.Key, request.IsKeyed);
    }

    public static bool IsServiceKeyParameter(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "ServiceKeyAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return true;
            }
        }

        return false;
    }
}
