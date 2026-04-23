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

        var reportedSpans = new HashSet<TextSpan>();

        foreach (var usingStmt in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody).OfType<UsingStatementSyntax>())
        {
            AnalyzeUsingStatement(
                context,
                executableBody,
                usingStmt,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }

        foreach (var localDecl in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody).OfType<LocalDeclarationStatementSyntax>())
        {
            if (localDecl.UsingKeyword == default)
            {
                continue;
            }

            AnalyzeUsingDeclaration(
                context,
                executableBody,
                localDecl,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }

        AnalyzeExplicitScopeDisposals(
            context,
            executableBody,
            semanticModel,
            registrationCollector,
            wellKnownTypes,
            reportedSpans);
    }

    private static void AnalyzeUsingStatement(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
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

        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
        if (usingStmt.Statement is not null)
        {
            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(usingStmt.Statement))
            {
                TrackProviderAlias(node, semanticModel, scopeSymbol, providerAliases);
                TrackServiceAlias(node, semanticModel, serviceVariables);
                TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

                if (node is not InvocationExpressionSyntax invocation ||
                    !TryGetResolutionLifetime(
                        invocation,
                        semanticModel,
                        scopeSymbol,
                        providerAliases,
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
                    serviceVariables[assignedVariable] = invocation;
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
            executableBody,
            semanticModel,
            serviceVariables,
            capturedDelegateVariables,
            usingEndPosition,
            reportedSpans);
    }

    private static void AnalyzeUsingDeclaration(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
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

        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(containingBlock))
        {
            if (node.SpanStart < localDecl.SpanStart)
            {
                continue;
            }

            TrackProviderAlias(node, semanticModel, scopeSymbol, providerAliases);
            TrackServiceAlias(node, semanticModel, serviceVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeSymbol,
                    providerAliases,
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
                serviceVariables[assignedVariable] = invocation;
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        var blockEndPosition = containingBlock.Span.End;
        ReportUsageAfterPosition(
            context,
            executableBody,
            semanticModel,
            serviceVariables,
            capturedDelegateVariables,
            blockEndPosition,
            reportedSpans);
    }

    private static void AnalyzeExplicitScopeDisposals(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        foreach (var scope in CollectExplicitScopeVariables(executableBody, semanticModel, wellKnownTypes))
        {
            var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
            var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
            var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
            int? disposePosition = null;

            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
            {
                if (node.SpanStart < scope.CreationPosition)
                {
                    continue;
                }

                if (node is InvocationExpressionSyntax disposeInvocation &&
                    IsDisposeInvocationForScope(disposeInvocation, semanticModel, scope.Symbol))
                {
                    disposePosition = disposeInvocation.Span.End;
                    break;
                }

                TrackProviderAlias(node, semanticModel, scope.Symbol, providerAliases);
                TrackServiceAlias(node, semanticModel, serviceVariables);
                TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

                if (node is not InvocationExpressionSyntax invocation ||
                    !TryGetResolutionLifetime(
                        invocation,
                        semanticModel,
                        scope.Symbol,
                        providerAliases,
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
                    serviceVariables[assignedVariable] = invocation;
                }
            }

            if (disposePosition is null || serviceVariables.Count == 0 && capturedDelegateVariables.Count == 0)
            {
                continue;
            }

            ReportUsageAfterPosition(
                context,
                executableBody,
                semanticModel,
                serviceVariables,
                capturedDelegateVariables,
                disposePosition.Value,
                reportedSpans);
        }
    }

    private static void ReportUsageAfterPosition(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, string> capturedDelegateVariables,
        int position,
        HashSet<TextSpan> reportedSpans)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node.SpanStart < position)
            {
                continue;
            }

            TrackServiceAlias(node, semanticModel, serviceVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            if (node is InvocationExpressionSyntax delegateInvocation &&
                delegateInvocation.Expression is IdentifierNameSyntax delegateIdentifier &&
                semanticModel.GetSymbolInfo(delegateIdentifier).Symbol is ILocalSymbol delegateSymbol &&
                capturedDelegateVariables.TryGetValue(delegateSymbol, out var capturedServiceName))
            {
                ReportDiagnostic(context, delegateInvocation, capturedServiceName, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax assignment &&
                TryGetTrackedLocalReference(assignment.Right, semanticModel, serviceVariables, out var assignedServiceName) &&
                (assignment.Left is TupleExpressionSyntax ||
                 semanticModel.GetSymbolInfo(assignment.Left).Symbol is IFieldSymbol or IPropertySymbol or IParameterSymbol))
            {
                ReportDiagnostic(context, assignment.Right, assignedServiceName, reportedSpans);
            }

            if (node is InvocationExpressionSyntax invocationAfter &&
                invocationAfter.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol symbol &&
                serviceVariables.ContainsKey(symbol))
            {
                ReportDiagnostic(context, invocationAfter, identifier.Identifier.Text, reportedSpans);
            }

            if (node is InvocationExpressionSyntax invocationWithTrackedArgument)
            {
                foreach (var argument in invocationWithTrackedArgument.ArgumentList.Arguments)
                {
                    if (argument.Expression is not IdentifierNameSyntax argumentIdentifier ||
                        semanticModel.GetSymbolInfo(argumentIdentifier).Symbol is not ILocalSymbol argumentSymbol ||
                        !serviceVariables.ContainsKey(argumentSymbol))
                    {
                        continue;
                    }

                    ReportDiagnostic(context, argumentIdentifier, argumentIdentifier.Identifier.Text, reportedSpans);
                }
            }

            if (node is ForEachStatementSyntax foreachStatement &&
                foreachStatement.Expression is IdentifierNameSyntax foreachIdentifier &&
                semanticModel.GetSymbolInfo(foreachIdentifier).Symbol is ILocalSymbol foreachSymbol &&
                serviceVariables.ContainsKey(foreachSymbol))
            {
                ReportDiagnostic(context, foreachIdentifier, foreachIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.Expression is IdentifierNameSyntax conditionalIdentifier &&
                semanticModel.GetSymbolInfo(conditionalIdentifier).Symbol is ILocalSymbol conditionalSymbol &&
                serviceVariables.ContainsKey(conditionalSymbol))
            {
                ReportDiagnostic(context, conditionalAccess, conditionalIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is ElementAccessExpressionSyntax elementAccess &&
                elementAccess.Expression is IdentifierNameSyntax elementIdentifier &&
                semanticModel.GetSymbolInfo(elementIdentifier).Symbol is ILocalSymbol elementSymbol &&
                serviceVariables.ContainsKey(elementSymbol))
            {
                ReportDiagnostic(context, elementAccess, elementIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is MemberAccessExpressionSyntax memberAccessAfter &&
                memberAccessAfter.Parent is not InvocationExpressionSyntax &&
                memberAccessAfter.Expression is IdentifierNameSyntax identifierAccess &&
                semanticModel.GetSymbolInfo(identifierAccess).Symbol is ILocalSymbol symbolAccess &&
                serviceVariables.ContainsKey(symbolAccess))
            {
                ReportDiagnostic(context, memberAccessAfter, identifierAccess.Identifier.Text, reportedSpans);
            }

            if (node is ReturnStatementSyntax { Expression: IdentifierNameSyntax returnIdentifier } &&
                semanticModel.GetSymbolInfo(returnIdentifier).Symbol is ILocalSymbol returnSymbol &&
                serviceVariables.ContainsKey(returnSymbol))
            {
                ReportDiagnostic(context, returnIdentifier, returnIdentifier.Identifier.Text, reportedSpans);
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

        if (usingStmt.Declaration is not null)
        {
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
        }

        return TryGetExistingScopeSymbol(usingStmt.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
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

    private readonly struct ScopeVariable
    {
        public ScopeVariable(ILocalSymbol symbol, int creationPosition)
        {
            Symbol = symbol;
            CreationPosition = creationPosition;
        }

        public ILocalSymbol Symbol { get; }

        public int CreationPosition { get; }
    }

    private static IEnumerable<ScopeVariable> CollectExplicitScopeVariables(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0 } localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                    {
                        yield return new ScopeVariable(localSymbol, localDeclaration.SpanStart);
                    }
                }
            }

            if (node is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax leftIdentifier &&
                assignment.Right is InvocationExpressionSyntax assignedInvocation &&
                IsCreateScopeInvocation(assignedInvocation, semanticModel, wellKnownTypes) &&
                semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol assignedScope)
            {
                yield return new ScopeVariable(assignedScope, assignment.SpanStart);
            }
        }
    }

    private static bool IsDisposeInvocationForScope(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text is not ("Dispose" or "DisposeAsync") ||
            memberAccess.Expression is not IdentifierNameSyntax scopeIdentifier ||
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(localSymbol, scopeSymbol);
    }

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(
                invocation,
                semanticModel,
                scopeSymbol,
                providerAliases,
                wellKnownTypes,
                out var serviceType,
                out var key,
                out var isKeyed,
                out var isCollectionResolution))
        {
            return false;
        }

        if (serviceType is null)
        {
            return true;
        }

        lifetime = isCollectionResolution
            ? GetCollectionResolutionLifetime(registrationCollector, serviceType, key, isKeyed)
            : registrationCollector.GetLifetime(serviceType, key, isKeyed);

        return true;
    }

    private static bool TryGetResolvedServiceInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol? serviceType,
        out object? key,
        out bool isKeyed,
        out bool isCollectionResolution)
    {
        serviceType = null;
        key = null;
        isKeyed = false;
        isCollectionResolution = false;

        if (invocation.Expression is not MemberAccessExpressionSyntax outerMember ||
            !TryResolveProviderScope(
                outerMember.Expression,
                semanticModel,
                scopeSymbol,
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
        isCollectionResolution = sourceMethod.Name == "GetServices";

        serviceType = GetResolvedServiceType(invocation, methodSymbol, semanticModel);
        if (isKeyed)
        {
            if (!TryExtractKey(invocation, methodSymbol, semanticModel, out key))
            {
                return false;
            }
        }

        return true;
    }

    private static ServiceLifetime? GetCollectionResolutionLifetime(
        RegistrationCollector registrationCollector,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed)
    {
        ServiceLifetime? singletonLifetime = null;

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key) ||
                !IsMatchingRegistration(registration.ServiceType, serviceType))
            {
                continue;
            }

            if (registration.Lifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient)
            {
                return registration.Lifetime;
            }

            singletonLifetime = registration.Lifetime;
        }

        return singletonLifetime;
    }

    private static bool IsMatchingRegistration(INamedTypeSymbol registeredType, ITypeSymbol requestedType)
    {
        if (SymbolEqualityComparer.Default.Equals(registeredType, requestedType))
        {
            return true;
        }

        if (requestedType is INamedTypeSymbol requestedNamedType &&
            requestedNamedType.IsGenericType &&
            !requestedNamedType.IsUnboundGenericType)
        {
            var openRequestedType = requestedNamedType.ConstructUnboundGenericType();
            return SymbolEqualityComparer.Default.Equals(registeredType, openRequestedType);
        }

        return false;
    }

    private static void TrackProviderAlias(
        SyntaxNode node,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases)
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
                        scopeSymbol,
                        providerAliases,
                        out var resolvedScope))
                {
                    providerAliases.Remove(localSymbol);
                    continue;
                }

                providerAliases[localSymbol] = resolvedScope;
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (!TryResolveProviderScope(
                    assignment.Right,
                    semanticModel,
                    scopeSymbol,
                    providerAliases,
                    out var assignmentScope))
            {
                providerAliases.Remove(leftLocal);
                return;
            }

            providerAliases[leftLocal] = assignmentScope;
        }
    }

    private static bool TryResolveProviderScope(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        out ILocalSymbol resolvedScope)
    {
        resolvedScope = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryResolveProviderScope(
                parenthesized.Expression,
                semanticModel,
                scopeSymbol,
                providerAliases,
                out resolvedScope);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ServiceProvider" &&
            memberAccess.Expression is IdentifierNameSyntax scopeIdentifier &&
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is ILocalSymbol directScope &&
            SymbolEqualityComparer.Default.Equals(directScope, scopeSymbol))
        {
            resolvedScope = directScope;
            return true;
        }

        if (expression is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol providerLocal &&
            providerAliases.TryGetValue(providerLocal, out var aliasScope) &&
            SymbolEqualityComparer.Default.Equals(aliasScope, scopeSymbol))
        {
            resolvedScope = aliasScope;
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
            if (assignment.Right is InvocationExpressionSyntax &&
                serviceVariables.TryGetValue(leftLocal, out var existingInvocation) &&
                existingInvocation == assignment.Right)
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

    private static void TrackDelegateCapture(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, string> capturedDelegateVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol delegateLocal)
                {
                    continue;
                }

                if (variable.Initializer?.Value is AnonymousFunctionExpressionSyntax anonymousFunction)
                {
                    if (TryFindCapturedServiceName(anonymousFunction, semanticModel, serviceVariables, out var capturedServiceName))
                    {
                        capturedDelegateVariables[delegateLocal] = capturedServiceName;
                    }

                    continue;
                }

                if (variable.Initializer?.Value is IdentifierNameSyntax identifier &&
                    semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol sourceDelegate &&
                    capturedDelegateVariables.TryGetValue(sourceDelegate, out var aliasedCapturedServiceName))
                {
                    capturedDelegateVariables[delegateLocal] = aliasedCapturedServiceName;
                }
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol assignedDelegate)
        {
            if (assignment.Right is AnonymousFunctionExpressionSyntax anonymousFunction &&
                TryFindCapturedServiceName(anonymousFunction, semanticModel, serviceVariables, out var capturedServiceName))
            {
                capturedDelegateVariables[assignedDelegate] = capturedServiceName;
                return;
            }

            if (assignment.Right is IdentifierNameSyntax rightIdentifier &&
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is ILocalSymbol sourceDelegate &&
                capturedDelegateVariables.TryGetValue(sourceDelegate, out var aliasedCapturedServiceName))
            {
                capturedDelegateVariables[assignedDelegate] = aliasedCapturedServiceName;
                return;
            }

            capturedDelegateVariables.Remove(assignedDelegate);
        }
    }

    private static bool TryFindCapturedServiceName(
        AnonymousFunctionExpressionSyntax anonymousFunction,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out string serviceName)
    {
        foreach (var identifier in anonymousFunction.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
                serviceVariables.ContainsKey(localSymbol))
            {
                serviceName = identifier.Identifier.Text;
                return true;
            }
        }

        serviceName = string.Empty;
        return false;
    }

    private static bool TryGetTrackedLocalReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out string serviceName)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetTrackedLocalReference(parenthesized.Expression, semanticModel, serviceVariables, out serviceName);
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
            serviceVariables.ContainsKey(localSymbol))
        {
            serviceName = identifier.Identifier.Text;
            return true;
        }

        serviceName = string.Empty;
        return false;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol, WellKnownTypes wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetServices" or "GetKeyedService" or "GetRequiredKeyedService"))
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

    private static bool TryExtractKey(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out object? key)
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
            key = null;
            return false;
        }

        var constant = semanticModel.GetConstantValue(keyExpression);
        key = constant.HasValue ? constant.Value : null;
        return constant.HasValue;
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
