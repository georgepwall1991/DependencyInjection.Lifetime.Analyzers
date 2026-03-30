using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Selects constructors that are likely to be used by the DI container.
/// </summary>
public static class ConstructorSelection
{
    /// <summary>
    /// Gets constructors to analyze for DI diagnostics.
    /// Prefers constructors marked with <c>[ActivatorUtilitiesConstructor]</c>;
    /// otherwise analyzes all accessible constructors.
    /// </summary>
    public static IEnumerable<IMethodSymbol> GetConstructorsToAnalyze(INamedTypeSymbol implementationType)
    {
        var candidates = implementationType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility != Accessibility.Private)
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var attributed = candidates
            .Where(HasActivatorUtilitiesConstructorAttribute)
            .ToList();

        if (attributed.Count > 0)
        {
            return attributed;
        }

        return candidates;
    }

    /// <summary>
    /// Gets the constructor(s) most likely to be selected for activation.
    /// Prefers attributed constructors, otherwise chooses the resolvable constructor set
    /// with the highest parameter count.
    /// </summary>
    public static IEnumerable<IMethodSymbol> GetLikelyActivationConstructors(
        INamedTypeSymbol implementationType,
        System.Func<IParameterSymbol, bool> isParameterResolvable)
    {
        var candidates = GetConstructorsToAnalyze(implementationType).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var attributed = candidates
            .Where(HasActivatorUtilitiesConstructorAttribute)
            .ToList();

        if (attributed.Count > 0)
        {
            return attributed;
        }

        var resolvableConstructors = candidates
            .Where(constructor => constructor.Parameters.All(isParameterResolvable))
            .ToList();

        if (resolvableConstructors.Count == 0)
        {
            return [];
        }

        var selectedParameterCount = resolvableConstructors.Max(constructor => constructor.Parameters.Length);
        return resolvableConstructors.Where(constructor => constructor.Parameters.Length == selectedParameterCount);
    }

    private static bool HasActivatorUtilitiesConstructorAttribute(IMethodSymbol constructor)
    {
        foreach (var attribute in constructor.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return true;
            }
        }

        return false;
    }
}
