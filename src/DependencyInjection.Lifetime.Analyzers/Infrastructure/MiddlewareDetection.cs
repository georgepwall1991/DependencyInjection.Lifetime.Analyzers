using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Helpers for recognizing ASP.NET Core middleware shapes. Used by DI007 and DI011
/// to exempt middleware from other diagnostics (loose shape check) and by DI020 to
/// detect convention-based middleware candidates for captive-dependency analysis
/// (strict check including a <see cref="RequestDelegate"/> constructor parameter and
/// excluding factory-based <c>IMiddleware</c> implementations).
/// </summary>
internal static class MiddlewareDetection
{
    private const string AspNetCoreHttpNamespace = "Microsoft.AspNetCore.Http";
    private const string HttpContextTypeName = "HttpContext";
    private const string RequestDelegateTypeName = "RequestDelegate";
    private const string IMiddlewareTypeName = "IMiddleware";

    /// <summary>
    /// Loose check: returns true if the type has any public <c>Invoke</c> or
    /// <c>InvokeAsync</c> method whose first parameter is <c>HttpContext</c> and
    /// which returns <see cref="System.Threading.Tasks.Task"/>. Matches the historical
    /// behavior of DI007 and DI011's middleware exemption.
    /// </summary>
    public static bool HasMiddlewareInvokeShape(INamedTypeSymbol type)
    {
        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(IsMiddlewareInvokeMethod);
    }

    /// <summary>
    /// Strict check: returns true if the type is a convention-based ASP.NET Core
    /// middleware class. Requires:
    /// (1) class kind, not abstract / static,
    /// (2) at least one public <c>Invoke</c> or <c>InvokeAsync</c> method that takes
    ///     <c>HttpContext</c> as the first parameter and returns <see cref="System.Threading.Tasks.Task"/>,
    /// (3) at least one public instance constructor with a <c>RequestDelegate</c>
    ///     parameter, and
    /// (4) does NOT implement <c>IMiddleware</c> (factory middleware, which has
    ///     correct lifecycle by design).
    /// </summary>
    public static bool IsConventionMiddlewareCandidate(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
        {
            return false;
        }

        if (ImplementsIMiddleware(type))
        {
            return false;
        }

        if (!HasMiddlewareInvokeShape(type))
        {
            return false;
        }

        return type.Constructors
            .Any(constructor =>
                !constructor.IsStatic &&
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Any(p => IsRequestDelegateParameter(p)));
    }

    /// <summary>
    /// Returns the <c>Invoke</c> or <c>InvokeAsync</c> method on the middleware type.
    /// Prefers <c>InvokeAsync</c> when both are present (it's the canonical async form
    /// the pipeline calls when both exist).
    /// </summary>
    public static IMethodSymbol? GetInvokeMethod(INamedTypeSymbol type)
    {
        IMethodSymbol? invoke = null;
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!IsMiddlewareInvokeMethod(member))
            {
                continue;
            }

            if (member.Name == "InvokeAsync")
            {
                return member;
            }

            invoke ??= member;
        }

        return invoke;
    }

    /// <summary>
    /// Returns true if the method is a middleware <c>Invoke</c>/<c>InvokeAsync</c>
    /// method: public, returns non-generic <see cref="System.Threading.Tasks.Task"/>,
    /// and the first parameter is <c>Microsoft.AspNetCore.Http.HttpContext</c>.
    /// </summary>
    public static bool IsMiddlewareInvokeMethod(IMethodSymbol method)
    {
        return method.Name is "Invoke" or "InvokeAsync" &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.IsStatic &&
               IsTaskType(method.ReturnType) &&
               method.Parameters.Length > 0 &&
               IsAspNetCoreHttpContext(method.Parameters[0].Type);
    }

    /// <summary>
    /// Returns true for a syntax-side check: the method declaration has the
    /// middleware <c>Invoke</c>/<c>InvokeAsync</c> shape. Used by DI007 which
    /// performs syntax-walk analysis.
    /// </summary>
    public static bool IsMiddlewareInvokeMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        return semanticModel.GetDeclaredSymbol(methodDecl) is IMethodSymbol method &&
               IsMiddlewareInvokeMethod(method);
    }

    /// <summary>
    /// Returns true if the parameter's type is the <c>Microsoft.AspNetCore.Http.RequestDelegate</c>
    /// delegate.
    /// </summary>
    public static bool IsRequestDelegateParameter(IParameterSymbol parameter)
    {
        return IsRequestDelegate(parameter.Type);
    }

    /// <summary>
    /// Returns true if the type is <c>Microsoft.AspNetCore.Http.IMiddleware</c> or
    /// the class implements that interface.
    /// </summary>
    public static bool ImplementsIMiddleware(INamedTypeSymbol type)
    {
        if (IsAspNetCoreHttpNamedType(type, IMiddlewareTypeName))
        {
            return true;
        }

        return type.AllInterfaces.Any(i => IsAspNetCoreHttpNamedType(i, IMiddlewareTypeName));
    }

    private static bool IsTaskType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: false } &&
               type.Name == "Task" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static bool IsAspNetCoreHttpContext(ITypeSymbol type)
    {
        return IsAspNetCoreHttpNamedType(type, HttpContextTypeName);
    }

    private static bool IsRequestDelegate(ITypeSymbol type)
    {
        return IsAspNetCoreHttpNamedType(type, RequestDelegateTypeName);
    }

    private static bool IsAspNetCoreHttpNamedType(ITypeSymbol type, string name)
    {
        return type.Name == name &&
               type.ContainingNamespace?.ToDisplayString() == AspNetCoreHttpNamespace;
    }
}
