using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Extracts high-confidence dependency requests from DI factory registrations.
/// </summary>
internal static class FactoryDependencyAnalysis
{
    public static ImmutableArray<DependencyRequest> GetDependencyRequests(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        WellKnownTypes? wellKnownTypes)
    {
        var requests = ImmutableArray.CreateBuilder<DependencyRequest>();

        foreach (var invocation in FactoryAnalysis.GetFactoryInvocations(factoryExpression, semanticModel))
        {
            if (TryCreateRequiredServiceRequest(invocation, semanticModel, wellKnownTypes, out var request) ||
                TryCreateActivatorUtilitiesRequest(invocation, semanticModel, out request))
            {
                requests.Add(request);
            }
        }

        return requests.ToImmutable();
    }

    private static bool TryCreateRequiredServiceRequest(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes? wellKnownTypes,
        out DependencyRequest request)
    {
        request = null!;

        if (semanticModel.GetOperation(invocation) is IInvocationOperation invocationOperation &&
            TryCreateRequiredServiceRequestFromOperation(invocationOperation, wellKnownTypes, out request))
        {
            return true;
        }

        return TryCreateRequiredServiceRequestFromSyntax(invocation, semanticModel, wellKnownTypes, out request);
    }

    private static bool TryCreateRequiredServiceRequestFromOperation(
        IInvocationOperation invocationOperation,
        WellKnownTypes? wellKnownTypes,
        out DependencyRequest request)
    {
        request = null!;

        var targetMethod = invocationOperation.TargetMethod;
        if (!IsRequiredResolutionMethod(targetMethod, wellKnownTypes) ||
            !TryGetResolvedDependencyType(invocationOperation, out var dependencyType))
        {
            return false;
        }

        var isKeyed = IsKeyedRequiredResolutionMethod(targetMethod);
        object? key = null;
        if (isKeyed && !TryGetConstantArgumentValue(invocationOperation, "serviceKey", "key", out key))
        {
            return false;
        }

        request = new DependencyRequest(
            dependencyType,
            key,
            isKeyed,
            DependencySourceKind.RequiredServiceCall,
            invocationOperation.Syntax.GetLocation(),
            DependencyResolutionEngine.FormatDependencyName(dependencyType, key, isKeyed));
        return true;
    }

    private static bool TryCreateRequiredServiceRequestFromSyntax(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes? wellKnownTypes,
        out DependencyRequest request)
    {
        request = null!;

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            !IsRequiredResolutionMethod(methodSymbol, wellKnownTypes) ||
            !TryGetResolvedDependencyType(invocation, methodSymbol, semanticModel, out var dependencyType))
        {
            return false;
        }

        var isKeyed = IsKeyedRequiredResolutionMethod(methodSymbol);
        object? key = null;
        if (isKeyed && !TryExtractKeyFromResolution(invocation, methodSymbol, semanticModel, out key))
        {
            return false;
        }

        request = new DependencyRequest(
            dependencyType,
            key,
            isKeyed,
            DependencySourceKind.RequiredServiceCall,
            invocation.GetLocation(),
            DependencyResolutionEngine.FormatDependencyName(dependencyType, key, isKeyed));
        return true;
    }

    private static bool TryCreateActivatorUtilitiesRequest(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out DependencyRequest request)
    {
        request = null!;

        if (!FactoryAnalysis.TryGetActivatorUtilitiesImplementationType(
                invocation,
                semanticModel,
                out var implementationType,
                out var hasExplicitConstructorArguments) ||
            hasExplicitConstructorArguments)
        {
            return false;
        }

        request = new DependencyRequest(
            implementationType,
            key: null,
            isKeyed: false,
            DependencySourceKind.ActivatorUtilitiesConstruction,
            invocation.GetLocation(),
            implementationType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        return true;
    }

    private static bool IsRequiredResolutionMethod(
        IMethodSymbol methodSymbol,
        WellKnownTypes? wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (sourceMethod.Name is not ("GetRequiredService" or "GetRequiredKeyedService"))
        {
            return false;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            if (wellKnownTypes?.IServiceProvider is not null &&
                SymbolEqualityComparer.Default.Equals(receiverType, wellKnownTypes.IServiceProvider))
            {
                return true;
            }

            if (wellKnownTypes?.IKeyedServiceProvider is not null &&
                SymbolEqualityComparer.Default.Equals(receiverType, wellKnownTypes.IKeyedServiceProvider))
            {
                return true;
            }

            if (wellKnownTypes is null &&
                receiverType.Name is "IServiceProvider" or "IKeyedServiceProvider")
            {
                return true;
            }
        }

        if (wellKnownTypes?.IKeyedServiceProvider is not null &&
            SymbolEqualityComparer.Default.Equals(sourceMethod.ContainingType, wellKnownTypes.IKeyedServiceProvider) &&
            sourceMethod.Name == "GetRequiredKeyedService")
        {
            return true;
        }

        if (wellKnownTypes is null &&
            sourceMethod.ContainingType?.Name == "IKeyedServiceProvider" &&
            sourceMethod.Name == "GetRequiredKeyedService")
        {
            return true;
        }

        return false;
    }

    private static bool IsKeyedRequiredResolutionMethod(IMethodSymbol methodSymbol)
    {
        return (methodSymbol.ReducedFrom ?? methodSymbol).Name == "GetRequiredKeyedService";
    }

    private static bool TryGetResolvedDependencyType(
        IInvocationOperation invocationOperation,
        out ITypeSymbol dependencyType)
    {
        dependencyType = null!;

        if (invocationOperation.TargetMethod.IsGenericMethod &&
            invocationOperation.TargetMethod.TypeArguments.Length > 0)
        {
            dependencyType = invocationOperation.TargetMethod.TypeArguments[0];
            return true;
        }

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.Parameter?.Name is not ("serviceType" or "type"))
            {
                continue;
            }

            if (argument.Value is ITypeOfOperation typeOfOperation &&
                typeOfOperation.TypeOperand is ITypeSymbol typeSymbol)
            {
                dependencyType = typeSymbol;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetResolvedDependencyType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out ITypeSymbol dependencyType)
    {
        dependencyType = null!;

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            dependencyType = methodSymbol.TypeArguments[0];
            return true;
        }

        var typeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");

        if (typeExpression is null && invocation.ArgumentList.Arguments.Count > 0)
        {
            typeExpression = invocation.ArgumentList.Arguments[0].Expression;
        }

        if (typeExpression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeOfExpression.Type);
        if (typeInfo.Type is null)
        {
            return false;
        }

        dependencyType = typeInfo.Type;
        return true;
    }

    private static bool TryExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out object? key)
    {
        key = null;

        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            return false;
        }

        if (!SyntaxValueHelpers.TryExtractConstantValue(keyExpression, semanticModel, out key))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetConstantArgumentValue(
        IInvocationOperation invocationOperation,
        string firstParameterName,
        string secondParameterName,
        out object? value)
    {
        value = null;

        foreach (var argument in invocationOperation.Arguments)
        {
            var parameterName = argument.Parameter?.Name;
            if (parameterName != firstParameterName &&
                parameterName != secondParameterName)
            {
                continue;
            }

            if (!argument.Value.ConstantValue.HasValue)
            {
                return false;
            }

            value = argument.Value.ConstantValue.Value;
            return true;
        }

        return false;
    }

    private static ExpressionSyntax? GetInvocationArgumentExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        string parameterName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
            {
                return argument.Expression;
            }
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var isReducedExtension = methodSymbol.ReducedFrom is not null;

        for (var index = 0; index < sourceMethod.Parameters.Length; index++)
        {
            if (sourceMethod.Parameters[index].Name != parameterName)
            {
                continue;
            }

            var argumentIndex = isReducedExtension ? index - 1 : index;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }
}
