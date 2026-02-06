using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects when services are used after their scope has been disposed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI004_UseAfterDisposeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UseAfterScopeDisposed);

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
        var reportedSpans = new HashSet<TextSpan>();

        foreach (var usingStmt in method.DescendantNodes().OfType<UsingStatementSyntax>())
        {
            AnalyzeUsingStatement(
                context,
                method,
                usingStmt,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }

        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (localDecl.UsingKeyword == default)
            {
                continue;
            }

            AnalyzeUsingDeclaration(
                context,
                method,
                localDecl,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }
    }

    private static void AnalyzeUsingStatement(
        CompilationAnalysisContext context,
        MethodDeclarationSyntax containingMethod,
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        if (!TryGetScopeSymbolFromUsingStatement(usingStmt, semanticModel, wellKnownTypes, out var scopeSymbol))
        {
            return;
        }

        var serviceVariables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        if (usingStmt.Statement is not null)
        {
            foreach (var invocation in usingStmt.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryGetResolutionLifetime(
                        invocation,
                        semanticModel,
                        scopeSymbol,
                        registrationCollector,
                        wellKnownTypes,
                        out var lifetime) ||
                    !ShouldReportUseAfterDispose(lifetime))
                {
                    continue;
                }

                var assignedVariable = GetAssignedVariableSymbol(invocation, semanticModel);
                if (assignedVariable is not null)
                {
                    serviceVariables.Add(assignedVariable);
                }
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        var usingEndPosition = usingStmt.Span.End;
        ReportUsageAfterPosition(
            context,
            containingMethod,
            semanticModel,
            serviceVariables,
            usingEndPosition,
            reportedSpans);
    }

    private static void AnalyzeUsingDeclaration(
        CompilationAnalysisContext context,
        MethodDeclarationSyntax containingMethod,
        LocalDeclarationStatementSyntax localDecl,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        if (!TryGetScopeSymbolFromUsingDeclaration(localDecl, semanticModel, wellKnownTypes, out var scopeSymbol))
        {
            return;
        }

        if (localDecl.Parent is not BlockSyntax containingBlock)
        {
            return;
        }

        var serviceVariables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in containingBlock.DescendantNodes())
        {
            if (node.SpanStart < localDecl.SpanStart)
            {
                continue;
            }

            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeSymbol,
                    registrationCollector,
                    wellKnownTypes,
                    out var lifetime) ||
                !ShouldReportUseAfterDispose(lifetime))
            {
                continue;
            }

            var assignedVariable = GetAssignedVariableSymbol(invocation, semanticModel);
            if (assignedVariable is not null)
            {
                serviceVariables.Add(assignedVariable);
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        var blockEndPosition = containingBlock.Span.End;
        ReportUsageAfterPosition(
            context,
            containingMethod,
            semanticModel,
            serviceVariables,
            blockEndPosition,
            reportedSpans);
    }

    private static void ReportUsageAfterPosition(
        CompilationAnalysisContext context,
        MethodDeclarationSyntax containingMethod,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> serviceVariables,
        int position,
        HashSet<TextSpan> reportedSpans)
    {
        foreach (var node in containingMethod.DescendantNodes())
        {
            if (node.SpanStart < position)
            {
                continue;
            }

            if (node is InvocationExpressionSyntax invocationAfter &&
                invocationAfter.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol symbol &&
                serviceVariables.Contains(symbol))
            {
                ReportDiagnostic(context, invocationAfter, identifier.Identifier.Text, reportedSpans);
            }

            if (node is MemberAccessExpressionSyntax memberAccessAfter &&
                memberAccessAfter.Parent is not InvocationExpressionSyntax &&
                memberAccessAfter.Expression is IdentifierNameSyntax identifierAccess &&
                semanticModel.GetSymbolInfo(identifierAccess).Symbol is ILocalSymbol symbolAccess &&
                serviceVariables.Contains(symbolAccess))
            {
                ReportDiagnostic(context, memberAccessAfter, identifierAccess.Identifier.Text, reportedSpans);
            }
        }
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        SyntaxNode locationNode,
        string serviceVariableName,
        HashSet<TextSpan> reportedSpans)
    {
        if (!reportedSpans.Add(locationNode.Span))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UseAfterScopeDisposed,
            locationNode.GetLocation(),
            serviceVariableName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryGetScopeSymbolFromUsingStatement(
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (usingStmt.Declaration is null)
        {
            return false;
        }

        foreach (var variable in usingStmt.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation ||
                !IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) ||
                semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
            {
                continue;
            }

            scopeSymbol = localSymbol;
            return true;
        }

        return false;
    }

    private static bool TryGetScopeSymbolFromUsingDeclaration(
        LocalDeclarationStatementSyntax localDecl,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        foreach (var variable in localDecl.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation ||
                !IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) ||
                semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
            {
                continue;
            }

            scopeSymbol = localSymbol;
            return true;
        }

        return false;
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

    private static ILocalSymbol? GetAssignedVariableSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
            semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignmentSymbol)
        {
            return assignmentSymbol;
        }

        if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator &&
            semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaratorSymbol)
        {
            return declaratorSymbol;
        }

        return null;
    }

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(invocation, semanticModel, scopeSymbol, wellKnownTypes, out var serviceType, out var key, out var isKeyed))
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
        ILocalSymbol scopeSymbol,
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
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is not ILocalSymbol resolvedScopeSymbol ||
            !SymbolEqualityComparer.Default.Equals(resolvedScopeSymbol, scopeSymbol))
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

    private static bool ShouldReportUseAfterDispose(ServiceLifetime? lifetime)
    {
        // Unknown lifetime should not produce a warning to avoid false positives
        // when registration metadata cannot be resolved.
        return lifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;
    }
}
