using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects BuildServiceProvider usage while composing service registrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI016_BuildServiceProviderMisuseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BuildServiceProviderMisuse);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var iServiceCollectionType = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection");
            var iServiceProviderType = compilationContext.Compilation.GetTypeByMetadataName(
                "System.IServiceProvider");
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
            if (iServiceCollectionType is null)
            {
                return;
            }

            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(
                    operationContext,
                    iServiceCollectionType,
                    iServiceProviderType,
                    semanticModelsByTree),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol iServiceCollectionType,
        INamedTypeSymbol? iServiceProviderType,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsBuildServiceProviderInvocation(invocation, iServiceCollectionType))
        {
            return;
        }

        if (!IsRegistrationContext(
                invocation,
                iServiceCollectionType,
                iServiceProviderType,
                semanticModelsByTree))
        {
            return;
        }

        var location = GetBuildServiceProviderLocation(invocation.Syntax);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.BuildServiceProviderMisuse,
            location);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsBuildServiceProviderInvocation(
        IInvocationOperation invocation,
        INamedTypeSymbol iServiceCollectionType)
    {
        var sourceMethod = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (sourceMethod.Name != "BuildServiceProvider")
        {
            return false;
        }

        var containingType = sourceMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionContainerBuilderExtensions" ||
            containingType.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (!sourceMethod.IsExtensionMethod || sourceMethod.Parameters.Length == 0)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(sourceMethod.Parameters[0].Type, iServiceCollectionType);
    }

    private static bool IsRegistrationContext(
        IInvocationOperation invocation,
        INamedTypeSymbol iServiceCollectionType,
        INamedTypeSymbol? iServiceProviderType,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var syntax = invocation.Syntax;
        var semanticModel = invocation.SemanticModel;
        if (semanticModel is null)
        {
            return false;
        }

        if (!TryGetReceiverExpression(invocation, out var receiverExpression))
        {
            return false;
        }

        for (var node = syntax.Parent; node is not null; node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                if (semanticModel.GetSymbolInfo(anonymousFunction).Symbol is IMethodSymbol anonymousMethod &&
                    IsBoundaryRegistrationContext(
                        anonymousMethod,
                        anonymousFunction,
                        receiverExpression,
                        semanticModel,
                        iServiceCollectionType,
                        iServiceProviderType,
                        semanticModelsByTree))
                {
                    return true;
                }

                if (semanticModel.GetSymbolInfo(anonymousFunction).Symbol is IMethodSymbol lambdaMethod &&
                    ReturnsIServiceProvider(lambdaMethod, iServiceProviderType))
                {
                    return false;
                }

                continue;
            }

            if (node is LocalFunctionStatementSyntax localFunction)
            {
                if (semanticModel.GetDeclaredSymbol(localFunction) is not IMethodSymbol localMethod)
                {
                    continue;
                }

                if (ReturnsIServiceProvider(localMethod, iServiceProviderType))
                {
                    return false;
                }

                if (IsBoundaryRegistrationContext(
                        localMethod,
                        localFunction,
                        receiverExpression,
                        semanticModel,
                        iServiceCollectionType,
                        iServiceProviderType,
                        semanticModelsByTree))
                {
                    return true;
                }

                continue;
            }

            if (node is MethodDeclarationSyntax methodDeclaration)
            {
                if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
                {
                    return false;
                }

                return IsBoundaryRegistrationContext(
                    methodSymbol,
                    methodDeclaration,
                    receiverExpression,
                    semanticModel,
                    iServiceCollectionType,
                    iServiceProviderType,
                    semanticModelsByTree);
            }

            if (node is ConstructorDeclarationSyntax)
            {
                return false;
            }

            if (node is GlobalStatementSyntax)
            {
                return IsTopLevelRegistrationAccess(invocation, semanticModel, iServiceCollectionType);
            }
        }

        return false;
    }

    private static bool HasIServiceCollectionParameter(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol iServiceCollectionType)
    {
        foreach (var parameter in methodSymbol.Parameters)
        {
            if (IsAssignableToIServiceCollection(parameter.Type, iServiceCollectionType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReturnsIServiceProvider(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? iServiceProviderType)
    {
        if (iServiceProviderType is null)
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, iServiceProviderType);
    }

    private static bool IsTopLevelRegistrationAccess(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        INamedTypeSymbol iServiceCollectionType)
    {
        if (!TryGetReceiverExpression(invocation, out var receiverExpression))
        {
            return false;
        }

        return IsServicesPropertySource(
            receiverExpression,
            semanticModel,
            iServiceCollectionType,
            invocation.Syntax.SyntaxTree.GetRoot(),
            depth: 0,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            new ConcurrentDictionary<SyntaxTree, SemanticModel>());
    }

    private static bool IsServicesPropertySource(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        INamedTypeSymbol iServiceCollectionType,
        SyntaxNode boundary,
        int depth,
        HashSet<ISymbol> visitedSymbols,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (depth > 8)
        {
            return false;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return IsServicesPropertySource(
                parenthesized.Expression,
                semanticModel,
                iServiceCollectionType,
                boundary,
                depth + 1,
                visitedSymbols,
                semanticModelsByTree);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            semanticModel.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol propertySymbol &&
            propertySymbol.Name == "Services" &&
            IsAssignableToIServiceCollection(propertySymbol.Type, iServiceCollectionType))
        {
            return true;
        }

        if (expression is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol)
        {
            return TryResolveLocalServicesSource(
                localSymbol,
                identifierName,
                semanticModel,
                iServiceCollectionType,
                boundary,
                depth + 1,
                visitedSymbols,
                semanticModelsByTree);
        }

        if (expression is InvocationExpressionSyntax invocationExpression)
        {
            return TryResolveHelperMethodServicesSource(
                invocationExpression,
                semanticModel,
                iServiceCollectionType,
                boundary,
                depth + 1,
                visitedSymbols,
                semanticModelsByTree);
        }

        return false;
    }

    private static bool IsBoundaryRegistrationContext(
        IMethodSymbol methodSymbol,
        SyntaxNode boundarySyntax,
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        INamedTypeSymbol iServiceCollectionType,
        INamedTypeSymbol? iServiceProviderType,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (ReturnsIServiceProvider(methodSymbol, iServiceProviderType))
        {
            return false;
        }

        return HasIServiceCollectionParameter(methodSymbol, iServiceCollectionType) ||
               IsServicesPropertySource(
                   receiverExpression,
                   semanticModel,
                   iServiceCollectionType,
                   boundarySyntax,
                   depth: 0,
                   new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                   semanticModelsByTree);
    }

    private static bool TryGetReceiverExpression(
        IInvocationOperation invocation,
        out ExpressionSyntax receiverExpression)
    {
        if (invocation.Syntax is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: var receiver },
            })
        {
            receiverExpression = receiver;
            return true;
        }

        receiverExpression = null!;
        return false;
    }

    private static bool IsAssignableToIServiceCollection(
        ITypeSymbol? type,
        INamedTypeSymbol iServiceCollectionType)
    {
        if (type is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, iServiceCollectionType))
        {
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface, iServiceCollectionType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveLocalServicesSource(
        ILocalSymbol localSymbol,
        ExpressionSyntax usageExpression,
        SemanticModel semanticModel,
        INamedTypeSymbol iServiceCollectionType,
        SyntaxNode boundary,
        int depth,
        HashSet<ISymbol> visitedSymbols,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (!visitedSymbols.Add(localSymbol))
        {
            return false;
        }

        var writeExpressions = new List<ExpressionSyntax>();
        var usagePosition = usageExpression.SpanStart;

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax
                {
                    Initializer.Value: var initializer,
                } declarator &&
                boundary.FullSpan.Contains(declarator.Span) &&
                declarator.SpanStart < usagePosition)
            {
                writeExpressions.Add(initializer);
            }
        }

        foreach (var node in EnumerateBoundaryNodes(boundary))
        {
            if (node.SpanStart >= usagePosition)
            {
                continue;
            }

            if (node is AssignmentExpressionSyntax { Left: var left, Right: var right } assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbolInfo(left).Symbol is ILocalSymbol assignedLocal &&
                SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
            {
                writeExpressions.Add(right);
            }
        }

        if (writeExpressions.Count == 0)
        {
            return false;
        }

        foreach (var writeExpression in writeExpressions)
        {
            if (!IsServicesPropertySource(
                    writeExpression,
                    semanticModel,
                    iServiceCollectionType,
                    boundary,
                    depth,
                    visitedSymbols,
                    semanticModelsByTree))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveHelperMethodServicesSource(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        INamedTypeSymbol iServiceCollectionType,
        SyntaxNode callerBoundary,
        int depth,
        HashSet<ISymbol> visitedSymbols,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocationExpression);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.FirstOrDefault() as IMethodSymbol;
        if (methodSymbol is null || !IsAssignableToIServiceCollection(methodSymbol.ReturnType, iServiceCollectionType))
        {
            return false;
        }

        var visitedMethodSymbol = methodSymbol.OriginalDefinition;
        if (!visitedSymbols.Add(visitedMethodSymbol))
        {
            return false;
        }

        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            var declarationSyntax = syntaxReference.GetSyntax();
            var declarationSemanticModel = GetSemanticModel(
                declarationSyntax.SyntaxTree,
                semanticModel.Compilation,
                semanticModelsByTree);

            if (TryGetReturnedExpressions(declarationSyntax, out var returnExpressions) &&
                AreAllReturnExpressionsServicesSources(
                    returnExpressions,
                    methodSymbol,
                    invocationExpression,
                    semanticModel,
                    declarationSyntax,
                    declarationSemanticModel,
                    callerBoundary,
                    iServiceCollectionType,
                    depth,
                    visitedSymbols,
                    semanticModelsByTree))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetReturnedExpressions(
        SyntaxNode declarationSyntax,
        out IReadOnlyList<ExpressionSyntax> returnExpressions)
    {
        var expressions = new List<ExpressionSyntax>();

        switch (declarationSyntax)
        {
            case MethodDeclarationSyntax { ExpressionBody: { Expression: var expression } }:
                expressions.Add(expression);
                returnExpressions = expressions;
                return true;
            case LocalFunctionStatementSyntax { ExpressionBody: { Expression: var expression } }:
                expressions.Add(expression);
                returnExpressions = expressions;
                return true;
            case MethodDeclarationSyntax { Body: { } methodBody }:
                CollectReturnExpressions(methodBody, expressions);
                returnExpressions = expressions;
                return expressions.Count > 0;
            case LocalFunctionStatementSyntax { Body: { } localFunctionBody }:
                CollectReturnExpressions(localFunctionBody, expressions);
                returnExpressions = expressions;
                return expressions.Count > 0;
            default:
                returnExpressions = [];
                return false;
        }
    }

    private static void CollectReturnExpressions(
        SyntaxNode body,
        List<ExpressionSyntax> expressions)
    {
        foreach (var node in EnumerateBoundaryNodes(body))
        {
            if (node is ReturnStatementSyntax { Expression: { } expression })
            {
                expressions.Add(expression);
            }
        }
    }

    private static bool AreAllReturnExpressionsServicesSources(
        IReadOnlyList<ExpressionSyntax> returnExpressions,
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocationExpression,
        SemanticModel callerSemanticModel,
        SyntaxNode declarationSyntax,
        SemanticModel declarationSemanticModel,
        SyntaxNode callerBoundary,
        INamedTypeSymbol iServiceCollectionType,
        int depth,
        HashSet<ISymbol> visitedSymbols,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (returnExpressions.Count == 0)
        {
            return false;
        }

        foreach (var returnExpression in returnExpressions)
        {
            if (declarationSemanticModel.GetSymbolInfo(returnExpression).Symbol is IParameterSymbol parameterSymbol &&
                TryGetInvocationArgumentExpression(invocationExpression, methodSymbol, parameterSymbol, out var argumentExpression))
            {
                if (!IsServicesPropertySource(
                        argumentExpression,
                        callerSemanticModel,
                        iServiceCollectionType,
                        callerBoundary,
                        depth,
                        visitedSymbols,
                        semanticModelsByTree))
                {
                    return false;
                }

                continue;
            }

            if (!IsServicesPropertySource(
                    returnExpression,
                    declarationSemanticModel,
                    iServiceCollectionType,
                    declarationSyntax,
                    depth,
                    visitedSymbols,
                    semanticModelsByTree))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetInvocationArgumentExpression(
        InvocationExpressionSyntax invocationExpression,
        IMethodSymbol methodSymbol,
        IParameterSymbol parameterSymbol,
        out ExpressionSyntax argumentExpression)
    {
        foreach (var argument in invocationExpression.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterSymbol.Name)
            {
                argumentExpression = argument.Expression;
                return true;
            }
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var argumentIndex = sourceMethod.Parameters.IndexOf(parameterSymbol);
        if (methodSymbol.ReducedFrom is not null)
        {
            argumentIndex--;
        }

        if (argumentIndex >= 0 && argumentIndex < invocationExpression.ArgumentList.Arguments.Count)
        {
            argumentExpression = invocationExpression.ArgumentList.Arguments[argumentIndex].Expression;
            return true;
        }

        argumentExpression = null!;
        return false;
    }

    private static SemanticModel GetSemanticModel(
        SyntaxTree syntaxTree,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        #pragma warning disable RS1030
        return semanticModelsByTree.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
        #pragma warning restore RS1030
    }

    private static IEnumerable<SyntaxNode> EnumerateBoundaryNodes(SyntaxNode boundary)
    {
        bool DescendIntoChildren(SyntaxNode node)
        {
            if (node == boundary)
            {
                return true;
            }

            return node is not AnonymousFunctionExpressionSyntax &&
                   node is not LocalFunctionStatementSyntax &&
                   node is not BaseMethodDeclarationSyntax &&
                   node is not TypeDeclarationSyntax &&
                   node is not RecordDeclarationSyntax;
        }

        foreach (var node in boundary.DescendantNodes(DescendIntoChildren))
        {
            yield return node;
        }
    }

    private static Location GetBuildServiceProviderLocation(SyntaxNode invocationSyntax)
    {
        if (invocationSyntax is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }

        return invocationSyntax.GetLocation();
    }
}
