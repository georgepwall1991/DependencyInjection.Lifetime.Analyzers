using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Collects service registrations from IServiceCollection extension method calls.
/// </summary>
public sealed class RegistrationCollector
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, ServiceRegistration> _registrations;
    private readonly ConcurrentBag<OrderedRegistration> _orderedRegistrations;
    private readonly INamedTypeSymbol? _serviceCollectionType;
    private int _registrationOrder;

    private RegistrationCollector(INamedTypeSymbol? serviceCollectionType)
    {
        _serviceCollectionType = serviceCollectionType;
        _registrations = new ConcurrentDictionary<INamedTypeSymbol, ServiceRegistration>(SymbolEqualityComparer.Default);
        _orderedRegistrations = new ConcurrentBag<OrderedRegistration>();
        _registrationOrder = 0;
    }

    /// <summary>
    /// Creates a registration collector for the given compilation.
    /// Returns null if IServiceCollection is not available.
    /// </summary>
    public static RegistrationCollector? Create(Compilation compilation)
    {
        var serviceCollectionType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");

        return new RegistrationCollector(serviceCollectionType);
    }

    /// <summary>
    /// Gets all collected registrations.
    /// </summary>
    public IEnumerable<ServiceRegistration> Registrations => _registrations.Values;

    /// <summary>
    /// Gets all ordered registrations for analyzing registration order.
    /// </summary>
    public IEnumerable<OrderedRegistration> OrderedRegistrations => _orderedRegistrations;

    /// <summary>
    /// Tries to get the registration for a specific service type.
    /// </summary>
    public bool TryGetRegistration(INamedTypeSymbol serviceType, out ServiceRegistration? registration)
    {
        return _registrations.TryGetValue(serviceType, out registration);
    }

    /// <summary>
    /// Gets the lifetime for a service type, if registered.
    /// </summary>
    public ServiceLifetime? GetLifetime(ITypeSymbol? serviceType)
    {
        if (serviceType is INamedTypeSymbol namedType &&
            _registrations.TryGetValue(namedType, out var registration))
        {
            return registration.Lifetime;
        }

        return null;
    }

    /// <summary>
    /// Analyzes an invocation expression to detect and record service registrations.
    /// </summary>
    public void AnalyzeInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Get the method symbol
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check if this is an extension method on IServiceCollection
        if (!IsServiceCollectionExtensionMethod(methodSymbol))
        {
            return;
        }

        var methodName = methodSymbol.Name;
        var isTryAdd = IsTryAddMethod(methodName);

        // Parse the lifetime from method name
        var lifetime = GetLifetimeFromMethodName(methodName);
        if (lifetime is null)
        {
            return;
        }

        // Extract service and implementation types
        var (serviceType, implementationType) = ExtractTypes(methodSymbol, invocation, semanticModel);
        if (serviceType is null)
        {
            return;
        }

        // Always track ordered registrations (for DI012 analysis)
        var order = Interlocked.Increment(ref _registrationOrder);
        var orderedRegistration = new OrderedRegistration(
            serviceType,
            lifetime.Value,
            invocation.GetLocation(),
            order,
            isTryAdd,
            methodName);
        _orderedRegistrations.Add(orderedRegistration);

        // Only store in main registrations dictionary if we have implementation type
        // and this is not a TryAdd (TryAdd doesn't override existing registrations)
        if (implementationType is not null && !isTryAdd)
        {
            var registration = new ServiceRegistration(
                serviceType,
                implementationType,
                lifetime.Value,
                invocation.GetLocation());

            // Store by service type (later registrations override earlier ones, like DI container behavior)
            _registrations[serviceType] = registration;
        }
    }

    private bool IsServiceCollectionExtensionMethod(IMethodSymbol method)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        // Check if the containing type is ServiceCollectionServiceExtensions or ServiceCollectionDescriptorExtensions
        var containingType = originalMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionServiceExtensions" &&
            containingType?.Name != "ServiceCollectionDescriptorExtensions")
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return firstParam.Type.Name == "IServiceCollection";
    }

    private static ServiceLifetime? GetLifetimeFromMethodName(string methodName)
    {
        // Handle common registration patterns (both Add* and TryAdd*)
        if (methodName.StartsWith("AddSingleton") || methodName.StartsWith("TryAddSingleton"))
        {
            return ServiceLifetime.Singleton;
        }

        if (methodName.StartsWith("AddScoped") || methodName.StartsWith("TryAddScoped"))
        {
            return ServiceLifetime.Scoped;
        }

        if (methodName.StartsWith("AddTransient") || methodName.StartsWith("TryAddTransient"))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private static bool IsTryAddMethod(string methodName)
    {
        return methodName.StartsWith("TryAdd");
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType) ExtractTypes(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Pattern 1: Generic method AddXxx<TService>() or AddXxx<TService, TImplementation>()
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            var serviceType = method.TypeArguments[0] as INamedTypeSymbol;
            var implementationType = method.TypeArguments.Length > 1
                ? method.TypeArguments[1] as INamedTypeSymbol
                : serviceType;

            return (serviceType, implementationType);
        }

        // Pattern 2: Non-generic with Type parameters AddXxx(typeof(TService)) or AddXxx(typeof(TService), typeof(TImpl))
        var arguments = invocation.ArgumentList.Arguments;

        // Skip the first argument if it's the IServiceCollection (extension method receiver)
        var typeofArgs = new List<INamedTypeSymbol>();
        foreach (var arg in arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                }
            }
        }

        if (typeofArgs.Count >= 1)
        {
            var serviceType = typeofArgs[0];
            var implementationType = typeofArgs.Count > 1 ? typeofArgs[1] : serviceType;
            return (serviceType, implementationType);
        }

        return (null, null);
    }
}
