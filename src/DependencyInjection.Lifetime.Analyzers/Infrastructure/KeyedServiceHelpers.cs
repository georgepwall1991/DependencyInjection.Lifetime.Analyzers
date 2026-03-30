using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Helpers for extracting keyed service information from parameter symbols.
/// </summary>
internal static class KeyedServiceHelpers
{
    /// <summary>
    /// Extracts the service key and keyed status from a parameter's [FromKeyedServices] attribute.
    /// </summary>
    /// <returns>A tuple of (key, isKeyed). If [FromKeyedServices] is present but the key value
    /// cannot be extracted, returns (null, true).</returns>
    public static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return (attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value
                    : null, true);
            }
        }

        return (null, false);
    }
}
