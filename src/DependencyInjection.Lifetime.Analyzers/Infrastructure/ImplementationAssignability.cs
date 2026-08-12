using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// MEDI implementation binding follows CLR assignability, not C# implicit conversions.
/// </summary>
internal static class ImplementationAssignability
{
    public static bool IsClosedTypeCompatible(
        Compilation compilation,
        INamedTypeSymbol service,
        INamedTypeSymbol implementation)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return false;
        }

        var conversion = csharpCompilation.ClassifyConversion(implementation, service);
        if (conversion.IsImplicit &&
            (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing))
        {
            return true;
        }

        // Runtime Type.IsAssignableFrom treats T as assignable to Nullable<T>.
        return service.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            service.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(service.TypeArguments[0], implementation);
    }
}
