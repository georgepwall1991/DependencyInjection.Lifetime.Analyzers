using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects transient services implementing IDisposable or IAsyncDisposable.
/// The DI container does not track transient services, so Dispose will never be called.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI008_DisposableTransientAnalyzer : DiagnosticAnalyzer
{
    private const string AllowedDisposableTypesOption = "dotnet_code_quality.DI008.allowed_disposable_types";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DisposableTransient);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            var serviceCollectionDescriptorExtensionsType =
                compilationContext.Compilation.GetTypeByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions") ??
                compilationContext.Compilation.GetTypeByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions");

            var serviceDescriptorType = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.ServiceDescriptor");

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeInvocation(
                    syntaxContext,
                    wellKnownTypes,
                    serviceCollectionDescriptorExtensionsType,
                    serviceDescriptorType,
                    compilationContext.Options.AnalyzerConfigOptionsProvider),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceCollectionDescriptorExtensionsType,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Get the method symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Path 1: AddTransient / AddKeyedTransient on ServiceCollectionServiceExtensions
        if (IsAddTransientMethod(methodSymbol, wellKnownTypes))
        {
            HandleAddTransientShape(context, invocation, methodSymbol, wellKnownTypes, optionsProvider);
            return;
        }

        // Path 2: TryAddTransient / TryAddKeyedTransient / TryAddEnumerable on ServiceCollectionDescriptorExtensions
        if (serviceCollectionDescriptorExtensionsType is not null &&
            IsTryAddTransientMethod(methodSymbol, wellKnownTypes, serviceCollectionDescriptorExtensionsType))
        {
            HandleTryAddTransientShape(context, invocation, methodSymbol, wellKnownTypes, serviceDescriptorType, optionsProvider);
            return;
        }

        // Path 3: services.Add(ServiceDescriptor.Transient<...>()) / .Describe(..., Transient) / new ServiceDescriptor(..., Transient)
        if (IsServiceCollectionAddOfDescriptor(invocation, methodSymbol, context.SemanticModel, wellKnownTypes, serviceDescriptorType))
        {
            HandleServiceCollectionAddDescriptor(context, invocation, wellKnownTypes, serviceDescriptorType, optionsProvider);
            return;
        }
    }

    private static void HandleAddTransientShape(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (IsFactoryRegistration(methodSymbol, invocation, context.SemanticModel))
        {
            return;
        }

        var implementationType = ExtractImplementationType(methodSymbol, invocation, context.SemanticModel);
        if (implementationType is null)
        {
            return;
        }

        ReportIfDisposable(context, invocation, implementationType, wellKnownTypes, optionsProvider);
    }

    private static void HandleTryAddTransientShape(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var methodName = (methodSymbol.ReducedFrom ?? methodSymbol).Name;

        // TryAddEnumerable takes a ServiceDescriptor (or IEnumerable<ServiceDescriptor>) argument
        if (methodName == "TryAddEnumerable")
        {
            HandleEnumerableDescriptorArguments(context, invocation, wellKnownTypes, serviceDescriptorType, optionsProvider);
            return;
        }

        // TryAddTransient / TryAddKeyedTransient mirror AddTransient shape
        if (IsFactoryRegistration(methodSymbol, invocation, context.SemanticModel))
        {
            return;
        }

        var implementationType = ExtractImplementationType(methodSymbol, invocation, context.SemanticModel);
        if (implementationType is null)
        {
            return;
        }

        ReportIfDisposable(context, invocation, implementationType, wellKnownTypes, optionsProvider);
    }

    private static void HandleServiceCollectionAddDescriptor(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        // services.Add(...) takes a single ServiceDescriptor argument
        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        var arg = invocation.ArgumentList.Arguments[0].Expression;
        TryHandleServiceDescriptorExpression(context, invocation, arg, wellKnownTypes, serviceDescriptorType, optionsProvider);
    }

    private static void HandleEnumerableDescriptorArguments(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            TryHandleServiceDescriptorExpressionRecursive(context, invocation, arg.Expression, wellKnownTypes, serviceDescriptorType, optionsProvider);
        }
    }

    private static void TryHandleServiceDescriptorExpressionRecursive(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax reportLocation,
        ExpressionSyntax expression,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        TryHandleServiceDescriptorExpression(context, reportLocation, expression, wellKnownTypes, serviceDescriptorType, optionsProvider);

        switch (expression)
        {
            case ImplicitArrayCreationExpressionSyntax implicitArray when implicitArray.Initializer is not null:
                foreach (var item in implicitArray.Initializer.Expressions)
                {
                    TryHandleServiceDescriptorExpressionRecursive(context, reportLocation, item, wellKnownTypes, serviceDescriptorType, optionsProvider);
                }

                break;

            case ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer is not null:
                foreach (var item in arrayCreation.Initializer.Expressions)
                {
                    TryHandleServiceDescriptorExpressionRecursive(context, reportLocation, item, wellKnownTypes, serviceDescriptorType, optionsProvider);
                }

                break;

            case ObjectCreationExpressionSyntax { Initializer: not null } objectCreation:
                foreach (var item in objectCreation.Initializer.Expressions)
                {
                    TryHandleServiceDescriptorExpressionRecursive(context, reportLocation, item, wellKnownTypes, serviceDescriptorType, optionsProvider);
                }

                break;
        }
    }

    private static void TryHandleServiceDescriptorExpression(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax reportLocation,
        ExpressionSyntax expression,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var (lifetime, implementationType, hasFactory) = ExtractFromDescriptorExpression(
            expression,
            context.SemanticModel,
            serviceDescriptorType);

        if (lifetime != ServiceLifetime.Transient || implementationType is null || hasFactory)
        {
            return;
        }

        ReportIfDisposable(context, reportLocation, implementationType, wellKnownTypes, optionsProvider);
    }

    private static void ReportIfDisposable(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol implementationType,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (IsAllowedDisposableType(implementationType, optionsProvider, context.SemanticModel.SyntaxTree))
        {
            return;
        }

        var disposableInterface = GetDisposableInterfaceName(implementationType, wellKnownTypes);
        if (disposableInterface is null)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DisposableTransient,
            invocation.GetLocation(),
            implementationType.Name,
            disposableInterface);

        context.ReportDiagnostic(diagnostic);
    }

    private static string? GetDisposableInterfaceName(
        INamedTypeSymbol implementationType,
        WellKnownTypes wellKnownTypes)
    {
        var disposableInterface = wellKnownTypes.GetDisposableInterfaceName(implementationType);
        if (disposableInterface is not null)
        {
            return disposableInterface;
        }

        if (implementationType.IsUnboundGenericType)
        {
            return wellKnownTypes.GetDisposableInterfaceName(implementationType.OriginalDefinition);
        }

        return null;
    }

    private static bool IsAllowedDisposableType(
        INamedTypeSymbol implementationType,
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree syntaxTree)
    {
        if (TryGetAllowedDisposableTypes(optionsProvider.GetOptions(syntaxTree), out var treeValue) ||
            TryGetAllowedDisposableTypes(optionsProvider.GlobalOptions, out treeValue))
        {
            var simpleName = implementationType.Name;
            var displayName = implementationType.ToDisplayString();
            var fullName = implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fullName.StartsWith("global::", StringComparison.Ordinal))
            {
                fullName = fullName.Substring("global::".Length);
            }

            foreach (var candidate in treeValue.Split(','))
            {
                var allowed = candidate.Trim();
                if (allowed.Length == 0)
                {
                    continue;
                }

                if (string.Equals(allowed, simpleName, StringComparison.Ordinal) ||
                    string.Equals(allowed, displayName, StringComparison.Ordinal) ||
                    string.Equals(allowed, fullName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetAllowedDisposableTypes(
        AnalyzerConfigOptions options,
        out string value)
    {
        if (options.TryGetValue(AllowedDisposableTypesOption, out var optionValue))
        {
            value = optionValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private enum ServiceLifetime
    {
        Singleton,
        Scoped,
        Transient,
    }

    private static (ServiceLifetime? lifetime, INamedTypeSymbol? implementationType, bool hasFactory)
        ExtractFromDescriptorExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            INamedTypeSymbol? serviceDescriptorType)
    {
        // ServiceDescriptor.Transient<TService, TImpl>() / ServiceDescriptor.Transient(serviceType, implementationType)
        // ServiceDescriptor.Describe(..., ServiceLifetime.Transient)
        if (expression is InvocationExpressionSyntax descriptorInvocation)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(descriptorInvocation);
            if (symbolInfo.Symbol is not IMethodSymbol descriptorMethod)
            {
                return (null, null, false);
            }

            if (!IsServiceDescriptorContainingType(descriptorMethod.ContainingType, serviceDescriptorType))
            {
                return (null, null, false);
            }

            var name = descriptorMethod.Name;
            ServiceLifetime? lifetime = name switch
            {
                "Transient" => ServiceLifetime.Transient,
                "Scoped" => ServiceLifetime.Scoped,
                "Singleton" => ServiceLifetime.Singleton,
                "Describe" => ExtractLifetimeFromDescribeArgs(descriptorInvocation, semanticModel),
                _ => null,
            };

            if (lifetime is null)
            {
                return (null, null, false);
            }

            var (implType, hasFactory) = ExtractImplFromDescriptorFactoryCall(
                descriptorMethod,
                descriptorInvocation,
                semanticModel);

            return (lifetime, implType, hasFactory);
        }

        // new ServiceDescriptor(serviceType, implementationType, ServiceLifetime.Transient)
        if (expression is ObjectCreationExpressionSyntax creation)
        {
            var typeInfo = semanticModel.GetTypeInfo(creation).Type;
            if (!IsServiceDescriptorContainingType(typeInfo, serviceDescriptorType))
            {
                return (null, null, false);
            }

            var lifetime = ExtractLifetimeFromArgs(creation.ArgumentList, semanticModel);
            if (lifetime is null)
            {
                return (null, null, false);
            }

            var (implType, hasFactory) = ExtractImplFromDescriptorCtorArgs(creation.ArgumentList, semanticModel);
            return (lifetime, implType, hasFactory);
        }

        return (null, null, false);
    }

    private static bool IsServiceDescriptorContainingType(ITypeSymbol? type, INamedTypeSymbol? serviceDescriptorType)
    {
        if (type is null)
        {
            return false;
        }

        if (serviceDescriptorType is not null)
        {
            return SymbolEqualityComparer.Default.Equals(type, serviceDescriptorType);
        }

        return type.Name == "ServiceDescriptor" &&
               (type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private static ServiceLifetime? ExtractLifetimeFromDescribeArgs(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return ExtractLifetimeFromArgs(invocation.ArgumentList, semanticModel);
    }

    private static ServiceLifetime? ExtractLifetimeFromArgs(
        ArgumentListSyntax? argumentList,
        SemanticModel semanticModel)
    {
        if (argumentList is null)
        {
            return null;
        }

        foreach (var arg in argumentList.Arguments)
        {
            var expr = arg.Expression;
            var typeInfo = semanticModel.GetTypeInfo(expr);
            var typeName = typeInfo.Type?.Name ?? typeInfo.ConvertedType?.Name;
            if (typeName != "ServiceLifetime")
            {
                continue;
            }

            string? memberName = null;
            if (expr is MemberAccessExpressionSyntax memberAccess)
            {
                memberName = memberAccess.Name.Identifier.Text;
            }
            else
            {
                var symbol = semanticModel.GetSymbolInfo(expr).Symbol;
                if (symbol is not null && symbol.ContainingType?.Name == "ServiceLifetime")
                {
                    memberName = symbol.Name;
                }
            }

            return memberName switch
            {
                "Transient" => ServiceLifetime.Transient,
                "Scoped" => ServiceLifetime.Scoped,
                "Singleton" => ServiceLifetime.Singleton,
                _ => null,
            };
        }

        return null;
    }

    private static (INamedTypeSymbol? implementationType, bool hasFactory) ExtractImplFromDescriptorFactoryCall(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Generic: ServiceDescriptor.Transient<TService, TImpl>() or ServiceDescriptor.Transient<TImpl>()
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            var implIndex = method.TypeArguments.Length > 1 ? 1 : 0;
            var (_, hasFactory) = ExtractImplFromDescriptorCtorArgs(invocation.ArgumentList, semanticModel);
            return (method.TypeArguments[implIndex] as INamedTypeSymbol, hasFactory);
        }

        // Non-generic: walk argument list for typeof(...) and factory lambdas
        return ExtractImplFromDescriptorCtorArgs(invocation.ArgumentList, semanticModel);
    }

    private static (INamedTypeSymbol? implementationType, bool hasFactory) ExtractImplFromDescriptorCtorArgs(
        ArgumentListSyntax? argumentList,
        SemanticModel semanticModel)
    {
        if (argumentList is null)
        {
            return (null, false);
        }

        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        var positionalTypeArgs = new List<INamedTypeSymbol>();
        var hasFactory = false;

        for (int i = 0; i < argumentList.Arguments.Count; i++)
        {
            var arg = argumentList.Arguments[i];
            var name = arg.NameColon?.Name.Identifier.Text;
            var expr = arg.Expression;

            if (expr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                hasFactory = true;
                continue;
            }

            if (expr is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol named)
                {
                    if (name == "implementationType")
                    {
                        implementationType = named;
                    }
                    else if (name == "serviceType")
                    {
                        serviceType = named;
                    }
                    else
                    {
                        positionalTypeArgs.Add(named);
                    }
                }
                continue;
            }

            // Method group / delegate-typed argument => factory
            var argType = semanticModel.GetTypeInfo(expr).ConvertedType;
            if (argType is INamedTypeSymbol delegateCandidate &&
                delegateCandidate.DelegateInvokeMethod is not null)
            {
                hasFactory = true;
            }
        }

        if (implementationType is not null)
        {
            return (implementationType, hasFactory);
        }

        if (positionalTypeArgs.Count > 1)
        {
            return (positionalTypeArgs[1], hasFactory);
        }

        if (positionalTypeArgs.Count == 1)
        {
            return (positionalTypeArgs[0], hasFactory);
        }

        return (serviceType, hasFactory);
    }

    private static bool IsAddTransientMethod(IMethodSymbol method, WellKnownTypes wellKnownTypes)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        // Check if the method name is AddTransient or AddKeyedTransient (for .NET 8+ keyed services)
        var methodName = originalMethod.Name;
        if (methodName is not ("AddTransient" or "AddKeyedTransient"))
        {
            return false;
        }

        // Check if the containing type is the framework ServiceCollectionServiceExtensions symbol
        var containingType = originalMethod.ContainingType;
        if (!wellKnownTypes.IsServiceCollectionServiceExtensions(containingType))
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return wellKnownTypes.IsServiceCollection(firstParam.Type);
    }

    private static bool IsTryAddTransientMethod(
        IMethodSymbol method,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol descriptorExtensionsType)
    {
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        var methodName = originalMethod.Name;
        if (methodName is not ("TryAddTransient" or "TryAddKeyedTransient" or "TryAddEnumerable"))
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(originalMethod.ContainingType, descriptorExtensionsType))
        {
            return false;
        }

        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        return wellKnownTypes.IsServiceCollection(originalMethod.Parameters[0].Type);
    }

    private static bool IsServiceCollectionAddOfDescriptor(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        INamedTypeSymbol? serviceDescriptorType)
    {
        // services.Add(ServiceDescriptor) typically binds to ICollection<ServiceDescriptor>.Add
        // (IServiceCollection extends IList<ServiceDescriptor>). We match by name + receiver type + single ServiceDescriptor parameter.
        if (method.Name != "Add")
        {
            return false;
        }

        if (method.Parameters.Length != 1)
        {
            return false;
        }

        var paramType = method.Parameters[0].Type;
        if (!IsServiceDescriptorContainingType(paramType, serviceDescriptorType))
        {
            return false;
        }

        // Resolve the receiver expression type and check it is (or implements) IServiceCollection.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null)
        {
            return false;
        }

        if (wellKnownTypes.IsServiceCollection(receiverType))
        {
            return true;
        }

        foreach (var iface in receiverType.AllInterfaces)
        {
            if (wellKnownTypes.IsServiceCollection(iface))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFactoryRegistration(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var args = invocation.ArgumentList.Arguments;

        for (int i = 0; i < args.Count; i++)
        {
            if (!TryGetArgumentParameter(method, args[i], i, out var param))
            {
                continue;
            }

            // Check if the parameter type is a delegate (Func<...>)
            if (param.Type is not INamedTypeSymbol paramNamed ||
                paramNamed.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var argExpr = args[i].Expression;

            // Lambda or anonymous method — always a factory
            if (argExpr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                return true;
            }

            // Check if the argument binds to a delegate type (lambda, method group, etc.)
            var argType = semanticModel.GetTypeInfo(argExpr).ConvertedType;
            if (argType is INamedTypeSymbol namedType &&
                namedType.DelegateInvokeMethod is not null)
            {
                return true;
            }

            // Fallback: if the argument is an identifier or member access and the
            // parameter is delegate-typed, treat it as a factory (method group)
            if (argExpr is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? ExtractImplementationType(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Pattern 1: Generic method AddTransient<TService>() or AddTransient<TService, TImplementation>()
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            // For AddTransient<TService, TImpl>, use TImpl
            // For AddTransient<TService>(), use TService as implementation
            return method.TypeArguments.Length > 1
                ? method.TypeArguments[1] as INamedTypeSymbol
                : method.TypeArguments[0] as INamedTypeSymbol;
        }

        // Pattern 2: Non-generic with Type parameters AddTransient(typeof(TService), typeof(TImpl)).
        // Prefer Roslyn's argument-to-parameter binding so named arguments can appear
        // out of source order without changing which type is the implementation.
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        var typeofArgs = new List<INamedTypeSymbol>();
        var args = invocation.ArgumentList.Arguments;
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                    if (TryGetArgumentParameter(method, arg, i, out var parameter))
                    {
                        switch (parameter.Name)
                        {
                            case "implementationType":
                                implementationType = namedType;
                                break;
                            case "serviceType":
                                serviceType = namedType;
                                break;
                        }
                    }
                }
            }
        }

        if (implementationType is not null)
        {
            return implementationType;
        }

        if (serviceType is not null)
        {
            return serviceType;
        }

        if (typeofArgs.Count >= 1)
        {
            // If two typeof arguments, second is implementation; otherwise first is both service and impl
            return typeofArgs.Count > 1 ? typeofArgs[1] : typeofArgs[0];
        }

        return null;
    }

    private static bool TryGetArgumentParameter(
        IMethodSymbol method,
        ArgumentSyntax argument,
        int argumentIndex,
        out IParameterSymbol parameter)
    {
        var originalMethod = method.ReducedFrom ?? method;

        if (argument.NameColon is { } nameColon)
        {
            var argumentName = nameColon.Name.Identifier.ValueText;
            foreach (var candidate in originalMethod.Parameters)
            {
                if (candidate.Name == argumentName)
                {
                    parameter = candidate;
                    return true;
                }
            }

            parameter = null!;
            return false;
        }

        // Reduced extension-method invocations omit the receiver from the argument list,
        // while static extension calls include it. Adjust only for the reduced form.
        var parameterIndex = argumentIndex + (method.ReducedFrom is not null ? 1 : 0);
        if (parameterIndex >= 0 && parameterIndex < originalMethod.Parameters.Length)
        {
            parameter = originalMethod.Parameters[parameterIndex];
            return true;
        }

        parameter = null!;
        return false;
    }
}
