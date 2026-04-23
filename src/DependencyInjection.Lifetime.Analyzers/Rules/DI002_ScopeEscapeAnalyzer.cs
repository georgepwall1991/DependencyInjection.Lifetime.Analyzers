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

            var executableRoots = new ConcurrentBag<SyntaxNode>();
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
                    executableRoots.Add(syntaxContext.Node);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                ExecutableSyntaxHelper.ExecutableRootKinds);

            compilationContext.RegisterCompilationEndAction(
                endContext =>
                {
                    foreach (var executableRoot in executableRoots)
                    {
                        if (!semanticModelsByTree.TryGetValue(executableRoot.SyntaxTree, out var semanticModel))
                        {
                            continue;
                        }

                        AnalyzeExecutableRoot(
                            endContext,
                            executableRoot,
                            semanticModel,
                            wellKnownTypes,
                            registrationCollector);
                    }
                });
        });
    }

    private static void AnalyzeExecutableRoot(
        CompilationAnalysisContext context,
        SyntaxNode executableRoot,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        RegistrationCollector registrationCollector)
    {
        if (!ExecutableSyntaxHelper.TryGetExecutableBody(executableRoot, out var executableBody))
        {
            return;
        }

        var scopeVariables = CollectScopeVariables(executableBody, semanticModel, wellKnownTypes);
        if (scopeVariables.Count == 0)
        {
            return;
        }

        var providerAliases = CollectScopeProviderAliases(executableBody, semanticModel, scopeVariables);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var reportedSpans = new HashSet<TextSpan>();

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    out var lifetime) ||
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

            if (invocation.Parent is AssignmentExpressionSyntax propertyAssignment &&
                semanticModel.GetSymbolInfo(propertyAssignment.Left).Symbol is IPropertySymbol propertySymbol)
            {
                ReportDiagnostic(context, invocation, propertySymbol.Name, reportedSpans);
            }

            if (invocation.Parent is AssignmentExpressionSyntax parameterAssignment &&
                semanticModel.GetSymbolInfo(parameterAssignment.Left).Symbol is IParameterSymbol parameterSymbol &&
                IsEscapingParameter(parameterSymbol))
            {
                ReportDiagnostic(context, invocation, parameterSymbol.Name, reportedSpans);
            }
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            TrackServiceAlias(node, semanticModel, serviceVariables);

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

            if (node is AssignmentExpressionSyntax propertyAssignment &&
                propertyAssignment.Right is IdentifierNameSyntax propertyValueId &&
                semanticModel.GetSymbolInfo(propertyValueId).Symbol is ILocalSymbol propertyValueSymbol &&
                serviceVariables.TryGetValue(propertyValueSymbol, out var propertySource) &&
                semanticModel.GetSymbolInfo(propertyAssignment.Left).Symbol is IPropertySymbol property)
            {
                ReportDiagnostic(context, propertySource, property.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax parameterAssignment &&
                parameterAssignment.Right is IdentifierNameSyntax parameterValueId &&
                semanticModel.GetSymbolInfo(parameterValueId).Symbol is ILocalSymbol parameterValueSymbol &&
                serviceVariables.TryGetValue(parameterValueSymbol, out var parameterSource) &&
                semanticModel.GetSymbolInfo(parameterAssignment.Left).Symbol is IParameterSymbol parameter &&
                IsEscapingParameter(parameter))
            {
                ReportDiagnostic(context, parameterSource, parameter.Name, reportedSpans);
            }
        }
    }

    private static bool IsEscapingParameter(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out;

    private static HashSet<ILocalSymbol> CollectScopeVariables(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        var scopeVariables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
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

            if (node is UsingStatementSyntax usingStmt &&
                TryGetScopeSymbolFromUsingStatement(usingStmt, semanticModel, wellKnownTypes, out var scopeSymbol))
            {
                scopeVariables.Add(scopeSymbol);
            }
        }

        return scopeVariables;
    }

    private static Dictionary<ILocalSymbol, ILocalSymbol> CollectScopeProviderAliases(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables)
    {
        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                    {
                        continue;
                    }

                    if (variable.Initializer?.Value is null ||
                        !TryResolveProviderScope(
                            variable.Initializer.Value,
                            semanticModel,
                            scopeVariables,
                            providerAliases,
                            out var scopeSymbol))
                    {
                        providerAliases.Remove(localSymbol);
                        continue;
                    }

                    providerAliases[localSymbol] = scopeSymbol;
                }
            }

            if (node is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignmentLocal)
            {
                if (!TryResolveProviderScope(
                        assignment.Right,
                        semanticModel,
                        scopeVariables,
                        providerAliases,
                        out var resolvedScope))
                {
                    providerAliases.Remove(assignmentLocal);
                    continue;
                }

                providerAliases[assignmentLocal] = resolvedScope;
            }
        }

        return providerAliases;
    }

    private static bool TryGetScopeSymbolFromUsingStatement(
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (usingStmt.Declaration is not null)
        {
            foreach (var variable in usingStmt.Declaration.Variables)
            {
                if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                    IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                    semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                {
                    scopeSymbol = localSymbol;
                    return true;
                }
            }
        }

        return TryGetExistingScopeSymbol(usingStmt.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
    }

    private static bool TryGetExistingScopeSymbol(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetExistingScopeSymbol(parenthesized.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
        }

        if (expression is not IdentifierNameSyntax identifierName ||
            semanticModel.GetSymbolInfo(identifierName).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: InvocationExpressionSyntax invocation } &&
                IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes))
            {
                scopeSymbol = localSymbol;
                return true;
            }
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

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(
                invocation,
                semanticModel,
                scopeVariables,
                providerAliases,
                wellKnownTypes,
                out var serviceType,
                out var key,
                out var isKeyed))
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
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol? serviceType,
        out object? key,
        out bool isKeyed)
    {
        serviceType = null;
        key = null;
        isKeyed = false;

        if (invocation.Expression is not MemberAccessExpressionSyntax outerMember ||
            !TryResolveProviderScope(
                outerMember.Expression,
                semanticModel,
                scopeVariables,
                providerAliases,
                out _))
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

    private static bool TryResolveProviderScope(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryResolveProviderScope(
                parenthesized.Expression,
                semanticModel,
                scopeVariables,
                providerAliases,
                out scopeSymbol);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ServiceProvider" &&
            memberAccess.Expression is IdentifierNameSyntax scopeIdentifier &&
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is ILocalSymbol directScope &&
            scopeVariables.Contains(directScope))
        {
            scopeSymbol = directScope;
            return true;
        }

        if (expression is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol providerLocal &&
            providerAliases.TryGetValue(providerLocal, out var resolvedScope))
        {
            scopeSymbol = resolvedScope;
            return true;
        }

        return false;
    }

    private static void TrackServiceAlias(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not IdentifierNameSyntax identifier ||
                    semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol sourceLocal ||
                    !serviceVariables.TryGetValue(sourceLocal, out var sourceInvocation) ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol aliasLocal)
                {
                    continue;
                }

                serviceVariables[aliasLocal] = sourceInvocation;
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (assignment.Right is InvocationExpressionSyntax directInvocation &&
                serviceVariables.TryGetValue(leftLocal, out var existingInvocation) &&
                existingInvocation == directInvocation)
            {
                return;
            }

            if (assignment.Right is not IdentifierNameSyntax rightIdentifier ||
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is not ILocalSymbol rightLocal ||
                !serviceVariables.TryGetValue(rightLocal, out var invocation))
            {
                serviceVariables.Remove(leftLocal);
                return;
            }

            serviceVariables[leftLocal] = invocation;
        }
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
