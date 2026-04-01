using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Shared helpers for analyzing DI factory registrations.
/// </summary>
internal static class FactoryAnalysis
{
    public static IEnumerable<InvocationExpressionSyntax> GetFactoryInvocations(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel)
    {
        var unwrappedFactoryExpression = UnwrapFactoryExpression(factoryExpression);

        if (unwrappedFactoryExpression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            foreach (var invocation in unwrappedFactoryExpression.DescendantNodesAndSelf()
                         .OfType<InvocationExpressionSyntax>())
            {
                yield return invocation;
            }

            yield break;
        }

        if (!IsMethodGroupExpression(unwrappedFactoryExpression, semanticModel))
        {
            yield break;
        }

        if (!TryGetFactoryMethodBodyNode(unwrappedFactoryExpression, semanticModel, out var bodyNode))
        {
            yield break;
        }

        foreach (var invocation in bodyNode.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            yield return invocation;
        }
    }

    public static bool TryGetActivatorUtilitiesImplementationType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out INamedTypeSymbol implementationType,
        out bool hasExplicitConstructorArguments)
    {
        implementationType = null!;
        hasExplicitConstructorArguments = false;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (sourceMethod.Name != "CreateInstance" ||
            sourceMethod.ContainingType?.Name != "ActivatorUtilities" ||
            sourceMethod.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            if (methodSymbol.TypeArguments[0] is not INamedTypeSymbol namedType)
            {
                return false;
            }

            implementationType = namedType;
            hasExplicitConstructorArguments = invocation.ArgumentList.Arguments.Count > 1;
            return true;
        }

        var instanceTypeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "instanceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");
        if (instanceTypeExpression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeOfExpression.Type);
        if (typeInfo.Type is not INamedTypeSymbol nonGenericNamedType)
        {
            return false;
        }

        implementationType = nonGenericNamedType;
        hasExplicitConstructorArguments = invocation.ArgumentList.Arguments.Count > 2;
        return true;
    }

    public static bool TryGetFactoryReturnExpression(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out ExpressionSyntax returnExpression)
    {
        returnExpression = null!;

        var unwrappedFactoryExpression = UnwrapFactoryExpression(factoryExpression);
        switch (unwrappedFactoryExpression)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                return TryGetReturnedExpression(simpleLambda.Body, out returnExpression);
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                return TryGetReturnedExpression(parenthesizedLambda.Body, out returnExpression);
            case AnonymousMethodExpressionSyntax anonymousMethod:
                return anonymousMethod.Body is not null &&
                       TryGetReturnedExpression(anonymousMethod.Body, out returnExpression);
        }

        if (!IsMethodGroupExpression(unwrappedFactoryExpression, semanticModel) ||
            !TryGetFactoryMethodBodyNode(unwrappedFactoryExpression, semanticModel, out var bodyNode))
        {
            return false;
        }

        return TryGetReturnedExpression(bodyNode, out returnExpression);
    }

    private static ExpressionSyntax UnwrapFactoryExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    expression = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsMethodGroupExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        if (symbolInfo.Symbol is IMethodSymbol)
        {
            return true;
        }

        return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Any();
    }

    private static bool TryGetFactoryMethodBodyNode(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out SyntaxNode bodyNode)
    {
        bodyNode = null!;

        var symbolInfo = semanticModel.GetSymbolInfo(factoryExpression);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol is null)
        {
            return false;
        }

        var declaration = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        switch (declaration)
        {
            case MethodDeclarationSyntax methodDeclaration:
                var methodBody = (SyntaxNode?)methodDeclaration.Body ?? methodDeclaration.ExpressionBody?.Expression;
                if (methodBody is null)
                {
                    return false;
                }

                bodyNode = methodBody;
                return true;
            case LocalFunctionStatementSyntax localFunction:
                var localFunctionBody = (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression;
                if (localFunctionBody is null)
                {
                    return false;
                }

                bodyNode = localFunctionBody;
                return true;
            default:
                return false;
        }
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

        for (var i = 0; i < sourceMethod.Parameters.Length; i++)
        {
            if (sourceMethod.Parameters[i].Name != parameterName)
            {
                continue;
            }

            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static bool TryGetReturnedExpression(
        SyntaxNode bodyNode,
        out ExpressionSyntax returnExpression)
    {
        switch (bodyNode)
        {
            case ExpressionSyntax expression:
                returnExpression = expression;
                return true;
            case BlockSyntax { Statements.Count: 1 } blockSyntax
                when blockSyntax.Statements[0] is ReturnStatementSyntax { Expression: not null } returnStatement:
                returnExpression = returnStatement.Expression;
                return true;
            default:
                returnExpression = null!;
                return false;
        }
    }
}
