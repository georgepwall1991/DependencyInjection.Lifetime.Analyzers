using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

internal static class MiddlewareHelpers
{
    public static bool IsMiddlewareClass(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(IsMiddlewareInvokeMethod))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsMiddlewareInvokeMethod(IMethodSymbol method)
    {
        return method.Name is "Invoke" or "InvokeAsync" &&
               method.DeclaredAccessibility == Accessibility.Public &&
               IsTaskType(method.ReturnType) &&
               method.Parameters.Length > 0 &&
               IsAspNetCoreHttpContext(method.Parameters[0].Type);
    }

    public static bool IsTaskType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               !namedType.IsGenericType &&
               namedType.Name == "Task" &&
               namedType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    public static bool IsNonGenericTaskLikeType(INamedTypeSymbol type)
    {
        return !type.IsGenericType &&
               type.Name is "Task" or "ValueTask" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static bool IsAspNetCoreHttpContext(ITypeSymbol type)
    {
        return type.Name == "HttpContext" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http";
    }
}
