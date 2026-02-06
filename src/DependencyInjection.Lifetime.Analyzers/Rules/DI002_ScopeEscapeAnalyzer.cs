using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects when scoped services escape their scope lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI002_ScopeEscapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ScopedServiceEscapes);

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

            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var methods = new ConcurrentBag<MethodDeclarationSyntax>();
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel);

                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    methods.Add((MethodDeclarationSyntax)syntaxContext.Node);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                SyntaxKind.MethodDeclaration);

            compilationContext.RegisterCompilationEndAction(
                endContext =>
                {
                    foreach (var method in methods)
                    {
                        if (!semanticModelsByTree.TryGetValue(method.SyntaxTree, out var semanticModel))
                        {
                            continue;
                        }

                        AnalyzeMethod(endContext, method, semanticModel, wellKnownTypes, registrationCollector);
                    }
                });
        });
    }

    private static void AnalyzeMethod(
        CompilationAnalysisContext context,
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        RegistrationCollector registrationCollector)
    {
        var scopeVariables = CollectScopeVariables(method, semanticModel, wellKnownTypes);
        if (scopeVariables.Count == 0)
        {
            return;
        }

        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var reportedSpans = new HashSet<TextSpan>();

        foreach (var node in method.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(invocation, semanticModel, scopeVariables, registrationCollector, wellKnownTypes, out var lifetime) ||
                !ShouldReportScopedEscape(lifetime))
            {
                continue;
            }

            if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
                equalsValue.Parent is VariableDeclaratorSyntax declarator &&
                semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol localSymbol)
            {
                serviceVariables[localSymbol] = invocation;
            }

            if (invocation.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignedLocalSymbol)
            {
                serviceVariables[assignedLocalSymbol] = invocation;
            }

            if (invocation.Parent is ReturnStatementSyntax)
            {
                ReportDiagnostic(context, invocation, "return", reportedSpans);
            }

            if (invocation.Parent is AssignmentExpressionSyntax fieldAssignment &&
                semanticModel.GetSymbolInfo(fieldAssignment.Left).Symbol is IFieldSymbol fieldSymbol)
            {
                ReportDiagnostic(context, invocation, fieldSymbol.Name, reportedSpans);
            }
        }

        foreach (var node in method.DescendantNodes())
        {
            if (node is ReturnStatementSyntax returnStmt &&
                returnStmt.Expression is IdentifierNameSyntax returnId &&
                semanticModel.GetSymbolInfo(returnId).Symbol is ILocalSymbol returnSymbol &&
                serviceVariables.TryGetValue(returnSymbol, out var sourceInvocation))
            {
                ReportDiagnostic(context, sourceInvocation, "return", reportedSpans);
            }

            if (node is AssignmentExpressionSyntax fieldAssignment &&
                fieldAssignment.Right is IdentifierNameSyntax valueId &&
                semanticModel.GetSymbolInfo(valueId).Symbol is ILocalSymbol valueSymbol &&
                serviceVariables.TryGetValue(valueSymbol, out var source) &&
                semanticModel.GetSymbolInfo(fieldAssignment.Left).Symbol is IFieldSymbol field)
            {
                ReportDiagnostic(context, source, field.Name, reportedSpans);
            }
        }
    }

    private static HashSet<ILocalSymbol> CollectScopeVariables(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        var scopeVariables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in method.DescendantNodes())
        {
            if (node is LocalDeclarationStatementSyntax localDecl &&
                (localDecl.UsingKeyword != default || localDecl.AwaitKeyword != default))
            {
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                    {
                        scopeVariables.Add(localSymbol);
                    }
                }
            }

            if (node is UsingStatementSyntax usingStmt && usingStmt.Declaration is not null)
            {
                foreach (var variable in usingStmt.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                    {
                        scopeVariables.Add(localSymbol);
                    }
                }
            }
        }

        return scopeVariables;
    }

    private static bool IsCreateScopeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (methodSymbol.Name is not ("CreateScope" or "CreateAsyncScope"))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var containingType = sourceMethod.ContainingType;

        if (containingType is null)
        {
            return false;
        }

        if (wellKnownTypes.IsServiceScopeFactory(containingType))
        {
            return true;
        }

        return containingType.Name == "ServiceProviderServiceExtensions" &&
               containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(invocation, semanticModel, scopeVariables, wellKnownTypes, out var serviceType, out var key, out var isKeyed))
        {
            return false;
        }

        if (serviceType is null)
        {
            return true;
        }

        lifetime = registrationCollector.GetLifetime(serviceType, key, isKeyed);
        return true;
    }

    private static bool TryGetResolvedServiceInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol? serviceType,
        out object? key,
        out bool isKeyed)
    {
        serviceType = null;
        key = null;
        isKeyed = false;

        if (invocation.Expression is not MemberAccessExpressionSyntax outerMember ||
            outerMember.Expression is not MemberAccessExpressionSyntax innerMember ||
            innerMember.Name.Identifier.Text != "ServiceProvider" ||
            innerMember.Expression is not IdentifierNameSyntax scopeIdentifier ||
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is not ILocalSymbol scopeSymbol ||
            !scopeVariables.Contains(scopeSymbol))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!IsServiceResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService";

        serviceType = GetResolvedServiceType(invocation, methodSymbol, semanticModel);
        if (isKeyed)
        {
            key = ExtractKey(invocation, methodSymbol, semanticModel);
        }

        return true;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol, WellKnownTypes wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService"))
        {
            return false;
        }

        var containingType = sourceMethod.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        if (containingType.Name == "ServiceProviderServiceExtensions" &&
            containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            return true;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            if (IsSystemIServiceProvider(receiverType) ||
                wellKnownTypes.IsKeyedServiceProvider(receiverType))
            {
                return true;
            }
        }

        return IsSystemIServiceProvider(containingType) ||
               wellKnownTypes.IsKeyedServiceProvider(containingType);
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
               type.ContainingNamespace.ToDisplayString() == "System";
    }

    private static ITypeSymbol? GetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            return methodSymbol.TypeArguments[0];
        }

        var serviceTypeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");
        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        return null;
    }

    private static object? ExtractKey(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            return null;
        }

        var constant = semanticModel.GetConstantValue(keyExpression);
        return constant.HasValue ? constant.Value : null;
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

    private static bool ShouldReportScopedEscape(ServiceLifetime? lifetime)
    {
        // DI002 targets scoped services escaping their scope. If lifetime is unknown,
        // skip reporting to avoid false positives when registration metadata is incomplete.
        return lifetime is ServiceLifetime.Scoped;
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string escapeTarget,
        HashSet<TextSpan> reportedSpans)
    {
        if (!reportedSpans.Add(invocation.Span))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ScopedServiceEscapes,
            invocation.GetLocation(),
            escapeTarget);

        context.ReportDiagnostic(diagnostic);
    }
}
