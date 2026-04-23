using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects scoped services resolved from a root service provider.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI019_RootScopedResolutionAnalyzer : DiagnosticAnalyzer
{
    internal const string ScopedDependencyTypeNamePropertyName = "ScopedDependencyTypeName";

    private readonly struct InvocationObservation
    {
        public InvocationObservation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            Invocation = invocation;
            SemanticModel = semanticModel;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public SemanticModel SemanticModel { get; }
    }

    private sealed class ProviderFacts
    {
        public ProviderFacts()
        {
            Facts = [];
        }

        public List<ProviderFact> Facts { get; }

        public bool TryGetLatestFactBefore(ISymbol symbol, int position, out bool isRootProvider)
        {
            ProviderFact? latest = null;
            foreach (var fact in Facts)
            {
                if (fact.Position >= position ||
                    !SymbolEqualityComparer.Default.Equals(fact.Symbol, symbol))
                {
                    continue;
                }

                if (latest is null || fact.Position > latest.Value.Position)
                {
                    latest = fact;
                }
            }

            if (latest is null)
            {
                isRootProvider = false;
                return false;
            }

            isRootProvider = latest.Value.IsRootProvider;
            return true;
        }
    }

    private readonly struct ProviderFact
    {
        public ProviderFact(ISymbol symbol, int position, bool isRootProvider)
        {
            Symbol = symbol;
            Position = position;
            IsRootProvider = isRootProvider;
        }

        public ISymbol Symbol { get; }

        public int Position { get; }

        public bool IsRootProvider { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RootScopedResolution);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            var invocations = new ConcurrentQueue<InvocationObservation>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(invocation, syntaxContext.SemanticModel);
                    invocations.Enqueue(new InvocationObservation(invocation, syntaxContext.SemanticModel));
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
                AnalyzeRootScopedResolutions(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    invocations.ToImmutableArray()));
        });
    }

    private static void AnalyzeRootScopedResolutions(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        ImmutableArray<InvocationObservation> invocations)
    {
        var providerFactsByTree = BuildProviderFactsByTree(context.Compilation, wellKnownTypes);
        var singletonImplementationTypes = GetSingletonImplementationTypes(registrationCollector);
        var scopedGraph = new ScopedDependencyGraph(
            registrationCollector,
            wellKnownTypes,
            context.Compilation);

        foreach (var observation in invocations)
        {
            var invocation = observation.Invocation;
            var semanticModel = observation.SemanticModel;
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol ||
                !TryGetServiceResolutionRequest(
                    invocation,
                    methodSymbol,
                    semanticModel,
                    wellKnownTypes,
                    out var serviceType,
                    out var key,
                    out var isKeyed,
                    out var isEnumerableRequest) ||
                !TryGetResolutionReceiver(invocation, methodSymbol, out var receiver))
            {
                continue;
            }

            if (IsInServiceRegistrationFactoryContext(invocation, semanticModel))
            {
                continue;
            }

            var providerFacts = providerFactsByTree.TryGetValue(invocation.SyntaxTree, out var facts)
                ? facts
                : new ProviderFacts();

            if (!IsRootProviderReceiver(
                    receiver,
                    invocation,
                    semanticModel,
                    wellKnownTypes,
                    providerFacts,
                    singletonImplementationTypes))
            {
                continue;
            }

            if (!scopedGraph.TryFindScopedDependency(
                    serviceType,
                    key,
                    isKeyed,
                    isEnumerableRequest,
                    out var scopedMatch))
            {
                continue;
            }

            ReportDiagnostic(
                context,
                invocation.GetLocation(),
                serviceType,
                scopedMatch);
        }
    }

    private static Dictionary<SyntaxTree, ProviderFacts> BuildProviderFactsByTree(
        Compilation compilation,
        WellKnownTypes wellKnownTypes)
    {
        var factsByTree = new Dictionary<SyntaxTree, ProviderFacts>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            #pragma warning disable RS1030
            var semanticModel = compilation.GetSemanticModel(tree);
            #pragma warning restore RS1030
            var facts = new ProviderFacts();
            var root = tree.GetRoot();

            foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer?.Value is not { } initializer)
                {
                    continue;
                }

                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol is null)
                {
                    continue;
                }

                AddProviderFact(
                    symbol,
                    initializer,
                    initializer.SpanStart,
                    semanticModel,
                    wellKnownTypes,
                    facts);
            }

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                var symbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (symbol is null)
                {
                    continue;
                }

                AddProviderFact(
                    symbol,
                    assignment.Right,
                    assignment.Right.SpanStart,
                    semanticModel,
                    wellKnownTypes,
                    facts);
            }

            factsByTree[tree] = facts;
        }

        return factsByTree;
    }

    private static void AddProviderFact(
        ISymbol symbol,
        ExpressionSyntax expression,
        int position,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts facts)
    {
        if (IsScopedProviderExpression(expression, semanticModel, wellKnownTypes, facts, position))
        {
            facts.Facts.Add(new ProviderFact(symbol, position, isRootProvider: false));
            return;
        }

        if (IsRootProviderExpression(expression, semanticModel, wellKnownTypes, facts, position))
        {
            facts.Facts.Add(new ProviderFact(symbol, position, isRootProvider: true));
        }
    }

    private static ImmutableHashSet<INamedTypeSymbol> GetSingletonImplementationTypes(
        RegistrationCollector registrationCollector)
    {
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.Lifetime == ServiceLifetime.Singleton &&
                !registration.HasImplementationInstance &&
                registration.ImplementationType is not null)
            {
                builder.Add(registration.ImplementationType);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryGetServiceResolutionRequest(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol serviceType,
        out object? key,
        out bool isKeyed,
        out bool isEnumerableRequest)
    {
        serviceType = null!;
        key = null;
        isKeyed = false;
        isEnumerableRequest = false;

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (!IsServiceResolutionMethod(sourceMethod, wellKnownTypes))
        {
            return false;
        }

        if (!TryGetResolvedServiceType(invocation, methodSymbol, semanticModel, out serviceType))
        {
            return false;
        }

        isEnumerableRequest = sourceMethod.Name is "GetServices" or "GetKeyedServices";
        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices";
        if (isKeyed &&
            !TryExtractKeyFromResolution(invocation, methodSymbol, semanticModel, out key))
        {
            return false;
        }

        return true;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol sourceMethod, WellKnownTypes wellKnownTypes)
    {
        if (sourceMethod.Name is not ("GetService" or "GetRequiredService" or "GetServices" or
            "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"))
        {
            return false;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            return wellKnownTypes.IsServiceProvider(receiverType) ||
                   wellKnownTypes.IsKeyedServiceProvider(receiverType);
        }

        return wellKnownTypes.IsServiceProvider(sourceMethod.ContainingType) ||
               wellKnownTypes.IsKeyedServiceProvider(sourceMethod.ContainingType);
    }

    private static bool TryGetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out ITypeSymbol serviceType)
    {
        serviceType = null!;

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            serviceType = methodSymbol.TypeArguments[0];
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

        serviceType = semanticModel.GetTypeInfo(typeOfExpression.Type).Type!;
        return serviceType is not null;
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

        return SyntaxValueHelpers.TryExtractServiceKeyValue(keyExpression, semanticModel, out key, out _) &&
               !SyntaxValueHelpers.IsKeyedServiceAnyKey(key);
    }

    private static bool TryGetResolutionReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        out ExpressionSyntax receiver)
    {
        receiver = null!;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
            return true;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (!sourceMethod.IsExtensionMethod || invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        receiver = invocation.ArgumentList.Arguments[0].Expression;
        return true;
    }

    private static bool IsRootProviderReceiver(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        ImmutableHashSet<INamedTypeSymbol> singletonImplementationTypes)
    {
        var position = invocation.SpanStart;
        if (IsScopedProviderExpression(receiver, semanticModel, wellKnownTypes, providerFacts, position))
        {
            return false;
        }

        if (IsRootProviderExpression(receiver, semanticModel, wellKnownTypes, providerFacts, position))
        {
            return true;
        }

        if (!wellKnownTypes.IsAnyServiceProvider(semanticModel.GetTypeInfo(receiver).Type))
        {
            return false;
        }

        if (!IsInsideSingletonImplementation(invocation, semanticModel, singletonImplementationTypes))
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        return symbol is IFieldSymbol or IPropertySymbol or IParameterSymbol or ILocalSymbol;
    }

    private static bool IsRootProviderExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        int position)
    {
        expression = Unwrap(expression);

        if (expression is InvocationExpressionSyntax invocation &&
            IsBuildServiceProviderInvocation(invocation, semanticModel))
        {
            return true;
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not null &&
            providerFacts.TryGetLatestFactBefore(symbol, position, out var isRootProvider))
        {
            return isRootProvider;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Services" &&
            wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(expression).Type))
        {
            return true;
        }

        return false;
    }

    private static bool IsScopedProviderExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        int position)
    {
        expression = Unwrap(expression);

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not null &&
            providerFacts.TryGetLatestFactBefore(symbol, position, out var isRootProvider))
        {
            return !isRootProvider;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text == "RequestServices" &&
                wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(expression).Type))
            {
                return true;
            }

            if (memberAccess.Name.Identifier.Text == "ServiceProvider")
            {
                var ownerType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                return wellKnownTypes.IsServiceScope(ownerType) ||
                       wellKnownTypes.IsAsyncServiceScope(ownerType);
            }
        }

        return false;
    }

    private static bool IsBuildServiceProviderInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        return sourceMethod.Name == "BuildServiceProvider" &&
               sourceMethod.IsExtensionMethod &&
               sourceMethod.Parameters.Length > 0 &&
               sourceMethod.Parameters[0].Type.Name == "IServiceCollection" &&
               sourceMethod.Parameters[0].Type.ContainingNamespace.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsInsideSingletonImplementation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ImmutableHashSet<INamedTypeSymbol> singletonImplementationTypes)
    {
        foreach (var typeDeclaration in invocation.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol &&
                singletonImplementationTypes.Contains(typeSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInServiceRegistrationFactoryContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        for (var node = invocation.Parent; node is not null; node = node.Parent)
        {
            if (node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                return IsFactoryRegistrationContext(node, semanticModel);
            }

            if (node is MethodDeclarationSyntax or TypeDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsFactoryRegistrationContext(SyntaxNode lambda, SemanticModel semanticModel)
    {
        for (var parent = lambda.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is InvocationExpressionSyntax parentInvocation &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol parentMethod)
            {
                var sourceMethod = parentMethod.ReducedFrom ?? parentMethod;
                if ((sourceMethod.Name.StartsWith("Add") || sourceMethod.Name == "TryAdd") &&
                    sourceMethod.IsExtensionMethod &&
                    sourceMethod.Parameters.Length > 0 &&
                    sourceMethod.Parameters[0].Type.Name == "IServiceCollection" &&
                    sourceMethod.Parameters[0].Type.ContainingNamespace.ToDisplayString() ==
                    "Microsoft.Extensions.DependencyInjection")
                {
                    return true;
                }
            }

            if (parent is MethodDeclarationSyntax or TypeDeclarationSyntax)
            {
                break;
            }
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

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
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

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        Location location,
        ITypeSymbol requestedType,
        ScopedDependencyGraph.ScopedDependencyMatch scopedMatch)
    {
        var requestedTypeName = requestedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var scopedTypeName = scopedMatch.ScopedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ScopedDependencyTypeNamePropertyName, scopedTypeName);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RootScopedResolution,
            location,
            additionalLocations: null,
            properties: properties,
            requestedTypeName,
            scopedTypeName);

        context.ReportDiagnostic(diagnostic);
    }
}
