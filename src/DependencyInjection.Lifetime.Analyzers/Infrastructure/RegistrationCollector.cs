using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ConcurrentDictionary<ServiceIdentifier, ServiceRegistration> _registrations;
    private readonly ConcurrentBag<OrderedRegistration> _orderedRegistrations;
    private readonly INamedTypeSymbol? _serviceCollectionType;
    private int _registrationOrder;

    private RegistrationCollector(INamedTypeSymbol? serviceCollectionType)
    {
        _serviceCollectionType = serviceCollectionType;
        _registrations = new ConcurrentDictionary<ServiceIdentifier, ServiceRegistration>();
        _orderedRegistrations = new ConcurrentBag<OrderedRegistration>();
        _registrationOrder = 0;
    }

    private readonly struct ServiceIdentifier : System.IEquatable<ServiceIdentifier>
    {
        public INamedTypeSymbol Type { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }

        public ServiceIdentifier(INamedTypeSymbol type, object? key, bool isKeyed)
        {
            Type = type;
            Key = key;
            IsKeyed = isKeyed;
        }

        public bool Equals(ServiceIdentifier other)
        {
            return SymbolEqualityComparer.Default.Equals(Type, other.Type) && Equals(Key, other.Key) && IsKeyed == other.IsKeyed;
        }

        public override bool Equals(object? obj)
        {
            return obj is ServiceIdentifier other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Type);
                hashCode = (hashCode * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Creates a registration collector for the given compilation.
    /// Returns null if IServiceCollection is not available.
    /// </summary>
    public static RegistrationCollector? Create(Compilation compilation)
    {
        var serviceCollectionType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        if (serviceCollectionType is null)
        {
            return null;
        }

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
    /// Tries to get the registration for a specific service type and key.
    /// </summary>
    public bool TryGetRegistration(INamedTypeSymbol serviceType, object? key, bool isKeyed, out ServiceRegistration? registration)
    {
        return _registrations.TryGetValue(new ServiceIdentifier(serviceType, key, isKeyed), out registration);
    }

    /// <summary>
    /// Gets the lifetime for a service type, if registered.
    /// </summary>
    public ServiceLifetime? GetLifetime(ITypeSymbol? serviceType, object? key = null, bool isKeyed = false)
    {
        if (serviceType is INamedTypeSymbol namedType &&
            _registrations.TryGetValue(new ServiceIdentifier(namedType, key, isKeyed), out var registration))
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

        var isExtension = IsServiceCollectionExtensionMethod(methodSymbol);
        var isAddMethod = IsServiceCollectionAddMethod(methodSymbol);

        // Check if this is an extension method on IServiceCollection OR ICollection.Add
        if (!isExtension && !isAddMethod)
        {
            return;
        }

        // If it's the instance Add method, verify the receiver is IServiceCollection
        if (isAddMethod && !IsReceiverServiceCollection(invocation, semanticModel))
        {
            return;
        }

        var methodName = methodSymbol.Name;
        var isTryAdd = IsTryAddMethod(methodName);

        // Parse the lifetime from method name
        var lifetime = GetLifetimeFromMethodName(methodName);

        INamedTypeSymbol? serviceType;
        INamedTypeSymbol? implementationType;
        ExpressionSyntax? factoryExpression;
        object? key = null;
        bool isKeyed = IsKeyedMethod(methodName);

        if (lifetime.HasValue)
        {
            // Extract service, implementation types, factory expression, and key from standard methods
            (serviceType, implementationType, factoryExpression, key) = ExtractTypes(methodSymbol, invocation, semanticModel);
        }
        else if ((methodName == "Add" || methodName == "TryAdd") && 
                 (isExtension || isAddMethod))
        {
            // Handle Add(ServiceDescriptor)
            (serviceType, implementationType, factoryExpression, lifetime, key, isKeyed) = ExtractFromServiceDescriptor(invocation, semanticModel);
        }
        else
        {
            return;
        }

        if (serviceType is null || lifetime is null)
        {
            return;
        }

        // Always track ordered registrations (for DI012 analysis)
        var order = Interlocked.Increment(ref _registrationOrder);
        var orderedRegistration = new OrderedRegistration(
            serviceType,
            key,
            isKeyed,
            lifetime.Value,
            invocation.GetLocation(),
            order,
            isTryAdd,
            methodName);
        _orderedRegistrations.Add(orderedRegistration);

        // Store in main registrations dictionary if we have implementation type OR a factory
        // and this is not a TryAdd (TryAdd doesn't override existing registrations)
        if ((implementationType is not null || factoryExpression is not null) && !isTryAdd)
        {
            var registration = new ServiceRegistration(
                serviceType,
                implementationType,
                factoryExpression,
                key,
                lifetime.Value,
                invocation.GetLocation());

            // Store by service type and key (later registrations override earlier ones, like DI container behavior)
            _registrations[new ServiceIdentifier(serviceType, key, isKeyed)] = registration;
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

    private bool IsServiceCollectionAddMethod(IMethodSymbol method)
    {
        if (method.Name != "Add") return false;
        if (method.Parameters.Length != 1) return false;

        var paramType = method.Parameters[0].Type;
        return paramType.Name == "ServiceDescriptor" &&
               (paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private bool IsReceiverServiceCollection(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (_serviceCollectionType is null) return false;

        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }

        if (receiver is null) return false;

        var typeInfo = semanticModel.GetTypeInfo(receiver);
        var type = typeInfo.Type;

        if (type is null) return false;

        // Check if type equals or implements IServiceCollection
        return InheritsFromOrEquals(type, _serviceCollectionType);
    }

    private bool InheritsFromOrEquals(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, baseType))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ServiceLifetime? GetLifetimeFromMethodName(string methodName)
    {
        // Handle common registration patterns (both Add*, TryAdd*, and keyed variants)
        if (methodName.StartsWith("AddSingleton") || methodName.StartsWith("TryAddSingleton") ||
            methodName.StartsWith("AddKeyedSingleton") || methodName.StartsWith("TryAddKeyedSingleton"))
        {
            return ServiceLifetime.Singleton;
        }

        if (methodName.StartsWith("AddScoped") || methodName.StartsWith("TryAddScoped") ||
            methodName.StartsWith("AddKeyedScoped") || methodName.StartsWith("TryAddKeyedScoped"))
        {
            return ServiceLifetime.Scoped;
        }

        if (methodName.StartsWith("AddTransient") || methodName.StartsWith("TryAddTransient") ||
            methodName.StartsWith("AddKeyedTransient") || methodName.StartsWith("TryAddKeyedTransient"))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private static bool IsKeyedMethod(string methodName)
    {
        return methodName.Contains("Keyed");
    }

    private static bool IsTryAddMethod(string methodName)
    {
        return methodName.StartsWith("TryAdd");
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, ServiceLifetime? lifetime, object? key, bool isKeyed) ExtractFromServiceDescriptor(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Look for ServiceDescriptor argument
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is ObjectCreationExpressionSyntax creation)
            {
                var typeSymbol = semanticModel.GetTypeInfo(creation).Type;
                if (typeSymbol?.Name == "ServiceDescriptor" && 
                    (typeSymbol.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                     typeSymbol.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions"))
                {
                    return ExtractFromServiceDescriptorArguments(creation.ArgumentList, semanticModel);
                }
            }
            else if (arg.Expression is InvocationExpressionSyntax describeInvocation)
            {
                var methodSymbol = semanticModel.GetSymbolInfo(describeInvocation).Symbol as IMethodSymbol;
                if (methodSymbol?.Name == "Describe" &&
                    methodSymbol.ContainingType.Name == "ServiceDescriptor" &&
                    (methodSymbol.ContainingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                     methodSymbol.ContainingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions"))
                {
                    return ExtractFromServiceDescriptorArguments(describeInvocation.ArgumentList, semanticModel);
                }
            }
        }

        return (null, null, null, null, null, false);
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, ServiceLifetime? lifetime, object? key, bool isKeyed) ExtractFromServiceDescriptorArguments(
        ArgumentListSyntax? argumentList,
        SemanticModel semanticModel)
    {
        var args = argumentList?.Arguments;
        if (args is null || args.Value.Count < 2)
        {
            return (null, null, null, null, null, false);
        }

        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        ServiceLifetime? lifetime = null;
        object? key = null;
        bool isKeyed = false;

        for (int i = 0; i < args.Value.Count; i++)
        {
            var arg = args.Value[i];
            var argName = arg.NameColon?.Name.Identifier.Text;
            var expr = arg.Expression;

            // 1. Service Type (Argument "serviceType" or index 0)
            if (argName == "serviceType" || (argName == null && i == 0))
            {
                if (expr is TypeOfExpressionSyntax serviceTypeOf)
                {
                    var typeInfo = semanticModel.GetTypeInfo(serviceTypeOf.Type);
                    serviceType = typeInfo.Type as INamedTypeSymbol;
                }
                continue;
            }

            // 2. Key (Argument "serviceKey")
            if (argName == "serviceKey")
            {
                key = ExtractConstantValue(expr, semanticModel);
                isKeyed = true;
                continue;
            }

            // 3. Lifetime (Argument "lifetime" or explicit ServiceLifetime enum/cast)
            if (argName == "lifetime" || IsServiceLifetimeExpression(expr, semanticModel))
            {
                lifetime = ExtractLifetime(expr, semanticModel);
                continue;
            }

            // 4. Implementation Type (Argument "implementationType")
            if (argName == "implementationType")
            {
                if (expr is TypeOfExpressionSyntax implTypeOf)
                {
                    var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                    implementationType = typeInfo.Type as INamedTypeSymbol;
                }
                continue;
            }

            // 5. Factory (Argument "factory")
            if (argName == "factory")
            {
                if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                {
                    factoryExpression = expr;
                }
                continue;
            }

            // 6. Instance (Argument "instance")
            if (argName == "instance")
            {
                 if (semanticModel.GetTypeInfo(expr).Type is INamedTypeSymbol instanceType &&
                     instanceType.SpecialType == SpecialType.None)
                {
                     implementationType = instanceType;
                }
                continue;
            }

            // Fallback for positional arguments
            if (argName == null)
            {
                if (i == 1)
                {
                    if (expr is TypeOfExpressionSyntax implTypeOf)
                    {
                        var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                        implementationType = typeInfo.Type as INamedTypeSymbol;
                    }
                    else if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                    {
                        factoryExpression = expr;
                    }
                    else
                    {
                         // Check if it's a key (constant) or instance
                        var val = ExtractConstantValue(expr, semanticModel);
                        if (val != null)
                        {
                            key = val;
                            isKeyed = true;
                        }
                        else if (semanticModel.GetTypeInfo(expr).Type is INamedTypeSymbol instanceType &&
                                 instanceType.SpecialType == SpecialType.None)
                        {
                             implementationType = instanceType;
                        }
                    }
                }
                else if (i == 2)
                {
                    // If we have a key (from i=1), this might be impl/factory
                    if (key != null || isKeyed)
                    {
                        if (expr is TypeOfExpressionSyntax implTypeOf)
                        {
                            var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
                            implementationType = typeInfo.Type as INamedTypeSymbol;
                        }
                        else if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                        {
                            factoryExpression = expr;
                        }
                    }
                    else if (lifetime == null)
                    {
                        lifetime = ExtractLifetime(expr, semanticModel);
                    }
                }
            }
        }

        return (serviceType, implementationType, factoryExpression, lifetime, key, isKeyed);
    }

    private static bool IsServiceLifetimeExpression(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expr);
        return typeInfo.Type?.Name == "ServiceLifetime" || 
               (typeInfo.ConvertedType?.Name == "ServiceLifetime");
    }

    private static ServiceLifetime? ExtractLifetime(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        // Handle Enum member access: ServiceLifetime.Scoped
        if (expr is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax enumType &&
            enumType.Identifier.Text == "ServiceLifetime")
        {
            var lifetimeName = memberAccess.Name.Identifier.Text;
            if (System.Enum.TryParse<ServiceLifetime>(lifetimeName, out var parsedLifetime))
            {
                return parsedLifetime;
            }
        }
        // Handle Cast: (ServiceLifetime)0
        else if (expr is CastExpressionSyntax castExpr)
        {
            // We only handle constant values in casts for now
            var constantValue = semanticModel.GetConstantValue(castExpr);
            if (constantValue.HasValue && constantValue.Value is int intValue)
            {
                 if (System.Enum.IsDefined(typeof(ServiceLifetime), intValue))
                 {
                     return (ServiceLifetime)intValue;
                 }
            }
        }

        return null;
    }

    private static object? ExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var constantValue = semanticModel.GetConstantValue(expr);
        if (constantValue.HasValue)
        {
            return constantValue.Value;
        }
        return null;
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, object? key) ExtractTypes(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        object? key = null;

        if (TryGetFactoryArgumentExpression(method, invocation, out var factoryArgumentExpression))
        {
            factoryExpression = factoryArgumentExpression;
        }

        bool isKeyed = IsKeyedMethod(method.Name);
        var arguments = invocation.ArgumentList.Arguments;

        // Pattern 1: Generic method AddXxx<TService>(...)
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            serviceType = method.TypeArguments[0] as INamedTypeSymbol;
            
            if (method.TypeArguments.Length > 1)
            {
                implementationType = method.TypeArguments[1] as INamedTypeSymbol;
            }
            else if (factoryExpression is null)
            {
                // Only default to serviceType if NO factory is present.
                // If factory is present, implementation is unknown (null).
                implementationType = serviceType;
            }
            
            // Key extraction for generic methods
            // AddKeyedSingleton<T>(key, ...) -> key is 1st argument
            if (isKeyed && arguments.Count > 0)
            {
                 key = ExtractConstantValue(arguments[0].Expression, semanticModel);
            }

            return (serviceType, implementationType, factoryExpression, key);
        }

        // Pattern 2: Non-generic with Type parameters AddXxx(typeof(TService)) or AddXxx(typeof(TService), typeof(TImpl))
        var typeofArgs = new List<INamedTypeSymbol>();
        int keyIndex = -1;

        for (int i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                }
            }
            else if (isKeyed && keyIndex == -1 && typeofArgs.Count > 0)
            {
                 // If we found the service type (typeofArgs[0]), the next non-typeof argument might be the key
                 // AddKeyedSingleton(typeof(T), key, ...)
                 keyIndex = i;
                 key = ExtractConstantValue(arg.Expression, semanticModel);
            }
        }

        if (typeofArgs.Count >= 1)
        {
            serviceType = typeofArgs[0];
            if (typeofArgs.Count > 1)
            {
                implementationType = typeofArgs[1];
            }
            else if (factoryExpression is null)
            {
                 // Only default to serviceType if NO factory is present
                implementationType = serviceType;
            }
            
            return (serviceType, implementationType, factoryExpression, key);
        }

        return (null, null, null, key);
    }

    private static bool TryGetFactoryArgumentExpression(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax factoryExpression)
    {
        factoryExpression = null!;

        var sourceMethod = method.ReducedFrom ?? method;
        var isReducedExtension = method.ReducedFrom is not null;

        for (var parameterIndex = 0; parameterIndex < sourceMethod.Parameters.Length; parameterIndex++)
        {
            var parameter = sourceMethod.Parameters[parameterIndex];
            if (parameter.Type.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var argumentIndex = isReducedExtension ? parameterIndex - 1 : parameterIndex;
            if (argumentIndex < 0 || argumentIndex >= invocation.ArgumentList.Arguments.Count)
            {
                continue;
            }

            factoryExpression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
            return true;
        }

        return false;
    }
}
