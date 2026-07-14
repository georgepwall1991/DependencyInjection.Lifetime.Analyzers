using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects scoped services resolved from a root service provider.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI019_RootScopedResolutionAnalyzer : DiagnosticAnalyzer
{
    internal const string ScopedDependencyTypeNamePropertyName = "ScopedDependencyTypeName";
    internal const string ResolutionPathPropertyName = "ResolutionPath";

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
                !TryGetResolutionReceiver(invocation, methodSymbol, semanticModel, out var receiver))
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
        if (!isEnumerableRequest &&
            TryUnwrapEnumerableServiceType(serviceType, out var elementType))
        {
            serviceType = elementType;
            isEnumerableRequest = true;
        }

        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices";
        if (isKeyed &&
            !TryExtractKeyFromResolution(invocation, methodSymbol, semanticModel, out key))
        {
            return false;
        }

        return true;
    }

    private static bool TryUnwrapEnumerableServiceType(
        ITypeSymbol serviceType,
        out ITypeSymbol elementType)
    {
        if (serviceType is INamedTypeSymbol
            {
                IsGenericType: true
            } namedType &&
            (namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
             namedType.Name == "IEnumerable" &&
             namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        elementType = serviceType;
        return false;
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
               wellKnownTypes.IsKeyedServiceProvider(sourceMethod.ContainingType) ||
               IsConcreteServiceProvider(sourceMethod.ContainingType);
    }

    private static bool IsConcreteServiceProvider(ITypeSymbol? type) =>
        type?.Name == "ServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

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
        SemanticModel semanticModel,
        out ExpressionSyntax receiver)
    {
        receiver = null!;

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (methodSymbol.ReducedFrom is null &&
            sourceMethod.IsExtensionMethod &&
            semanticModel.GetOperation(invocation) is IInvocationOperation invocationOperation &&
            invocationOperation.Arguments.FirstOrDefault(
                argument => argument.Parameter?.Ordinal == 0)?.Value.Syntax is ExpressionSyntax staticReceiver)
        {
            receiver = staticReceiver;
            return true;
        }

        // The provider is the receiver of the resolution member itself. Check the member access
        // first: in `host?.Services.GetRequiredService<T>()` the conditional access's WhenNotNull is
        // the whole invocation, but the provider is the `.Services` member binding, not `host`.
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull == invocation)
        {
            receiver = conditionalAccess.Expression;
            return true;
        }

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

        if (expression is MemberAccessExpressionSyntax rootProviderProperty &&
            IsKnownRootProviderProperty(rootProviderProperty, semanticModel, wellKnownTypes))
        {
            return true;
        }

        // Conditional-access form: `host?.Services.GetRequiredService<T>()` exposes the `.Services`
        // receiver as a MemberBindingExpressionSyntax whose owner is the conditional access's
        // expression.
        if (expression is MemberBindingExpressionSyntax rootProviderBinding &&
            IsKnownRootProviderProperty(rootProviderBinding, semanticModel, wellKnownTypes))
        {
            return true;
        }

        return false;
    }

    private static bool IsKnownRootProviderProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (!wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(memberAccess).Type))
        {
            return false;
        }

        var ownerType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return IsKnownRootProviderPropertyName(memberAccess.Name.Identifier.Text, ownerType);
    }

    private static bool IsKnownRootProviderProperty(
        MemberBindingExpressionSyntax memberBinding,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (!wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(memberBinding).Type))
        {
            return false;
        }

        if (TryGetConditionalAccessReceiver(memberBinding) is not { } owner)
        {
            return false;
        }

        var ownerType = semanticModel.GetTypeInfo(owner).Type;
        return IsKnownRootProviderPropertyName(memberBinding.Name.Identifier.Text, ownerType);
    }

    private static bool IsKnownRootProviderPropertyName(string propertyName, ITypeSymbol? ownerType) =>
        propertyName switch
        {
            "Services" => IsKnownRootServicesOwner(ownerType),
            "ApplicationServices" => IsNamedOrImplements(
                ownerType,
                "Microsoft.AspNetCore.Builder",
                "IApplicationBuilder"),
            "ServiceProvider" => IsNamedOrImplements(
                ownerType,
                "Microsoft.AspNetCore.Routing",
                "IEndpointRouteBuilder"),
            _ => false
        };

    /// <summary>
    /// Resolves the receiver that a conditional-access member binding is evaluated against: for
    /// `host?.Services` the `.Services` binding's receiver is `host`. The owning conditional access
    /// is the nearest ancestor whose <c>WhenNotNull</c> contains the binding -- the binding can also
    /// be the <c>Expression</c> of an inner chained conditional access (`host?.Services?.X`), which
    /// is not its owner.
    /// </summary>
    private static ExpressionSyntax? TryGetConditionalAccessReceiver(MemberBindingExpressionSyntax memberBinding)
    {
        for (SyntaxNode? node = memberBinding.Parent; node is not null; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(memberBinding.Span))
            {
                return conditionalAccess.Expression;
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static bool IsKnownRootServicesOwner(ITypeSymbol? ownerType)
    {
        if (ownerType is INamedTypeSymbol namedOwner)
        {
            return IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.Builder", "WebApplication") ||
                   IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.Mvc.Testing", "WebApplicationFactory") ||
                   IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.TestHost", "TestServer") ||
                   IsNamedOrImplements(namedOwner, "Microsoft.Extensions.Hosting", "IHost") ||
                   IsNamedOrImplements(namedOwner, "Microsoft.AspNetCore.Hosting", "IWebHost");
        }

        if (ownerType is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(IsKnownRootServicesOwner);
        }

        return false;
    }

    private static bool IsNamedOrImplements(
        ITypeSymbol? type,
        string namespaceName,
        string typeName)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return IsKnownType(namedType, namespaceName, typeName) ||
               namedType.AllInterfaces.Any(iface => IsKnownType(iface, namespaceName, typeName));
    }

    private static bool IsKnownType(
        INamedTypeSymbol type,
        string namespaceName,
        string typeName) =>
        type.Name == typeName &&
        type.ContainingNamespace.ToDisplayString() == namespaceName;

    private static bool IsKnownTypeOrBaseType(
        INamedTypeSymbol type,
        string namespaceName,
        string typeName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (IsKnownType(current, namespaceName, typeName))
            {
                return true;
            }
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

        // Conditional-access form: `httpContext?.RequestServices...` / `scope?.ServiceProvider...`
        // expose the scoped-provider property as a MemberBindingExpressionSyntax.
        if (expression is MemberBindingExpressionSyntax memberBinding)
        {
            if (memberBinding.Name.Identifier.Text == "RequestServices" &&
                wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(expression).Type))
            {
                return true;
            }

            if (memberBinding.Name.Identifier.Text == "ServiceProvider" &&
                TryGetConditionalAccessReceiver(memberBinding) is { } scopeOwner)
            {
                var ownerType = semanticModel.GetTypeInfo(scopeOwner).Type;
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
                case PostfixUnaryExpressionSyntax postfixUnaryExpression
                    when postfixUnaryExpression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfixUnaryExpression.Operand;
                    continue;
                case ConditionalAccessExpressionSyntax conditionalAccessExpression:
                    // `app?.Services` classifies like its WhenNotNull member binding: the value the
                    // expression produces (when not null) is that member's value.
                    expression = conditionalAccessExpression.WhenNotNull;
                    continue;
                case BinaryExpressionSyntax binaryExpression
                    when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                         binaryExpression.Right is ThrowExpressionSyntax:
                    expression = binaryExpression.Left;
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
        var resolutionPath = FormatResolutionPath(scopedMatch);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ScopedDependencyTypeNamePropertyName, scopedTypeName)
            .Add(ResolutionPathPropertyName, resolutionPath);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RootScopedResolution,
            location,
            additionalLocations: null,
            properties: properties,
            requestedTypeName,
            resolutionPath);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Renders the activation chain from the requested service to the captured scoped service as
    /// <c>Requested -&gt; Intermediate -&gt; Scoped</c>. This turns an otherwise opaque transitive
    /// warning into a precise map of exactly how the root provider reaches a scoped dependency.
    /// </summary>
    private static string FormatResolutionPath(
        ScopedDependencyGraph.ScopedDependencyMatch scopedMatch)
    {
        var path = scopedMatch.Path;
        if (path.IsDefaultOrEmpty)
        {
            return scopedMatch.ScopedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return string.Join(
            " -> ",
            path.Select(node => node.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
