using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects captive dependencies - when a singleton captures a scoped or transient dependency.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI003_CaptiveDependencyAnalyzer : DiagnosticAnalyzer
{
    internal const string DependencyLifetimePropertyName = "DependencyLifetime";

    private readonly struct ReportedDiagnosticKey : System.IEquatable<ReportedDiagnosticKey>
    {
        public int RegistrationStart { get; }
        public ITypeSymbol DependencyType { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }

        public ReportedDiagnosticKey(
            int registrationStart,
            ITypeSymbol dependencyType,
            object? key,
            bool isKeyed)
        {
            RegistrationStart = registrationStart;
            DependencyType = dependencyType;
            Key = key;
            IsKeyed = isKeyed;
        }

        public bool Equals(ReportedDiagnosticKey other)
        {
            return RegistrationStart == other.RegistrationStart &&
                   SymbolEqualityComparer.Default.Equals(DependencyType, other.DependencyType) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = RegistrationStart;
                hashCode = (hashCode * 397) ^ SymbolEqualityComparer.Default.GetHashCode(DependencyType);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                return hashCode;
            }
        }

        public override bool Equals(object? obj) =>
            obj is ReportedDiagnosticKey other && Equals(other);
    }

    private sealed class FactoryKeyContext
    {
        public static readonly FactoryKeyContext None = new(
            keyParameter: null,
            inheritedKey: null);

        public FactoryKeyContext(
            IParameterSymbol? keyParameter,
            object? inheritedKey)
        {
            KeyParameter = keyParameter;
            InheritedKey = inheritedKey;
        }

        public IParameterSymbol? KeyParameter { get; }

        public object? InheritedKey { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CaptiveDependency);

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

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(
                WellKnownTypes.Create(compilationContext.Compilation));

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeCaptiveDependencies(endContext, registrationCollector, lifetimeClassifier));
        });
    }

    private static void AnalyzeCaptiveDependencies(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier)
    {
        var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
        var reportedDiagnostics = new HashSet<ReportedDiagnosticKey>();

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            // Limit DI003 to singleton consumers. Scoped -> transient is common and generally safe,
            // so warning on it creates more noise than signal.
            if (registration.Lifetime != ServiceLifetime.Singleton)
            {
                continue;
            }

            if (registration.FactoryExpression != null)
            {
                AnalyzeFactoryRegistration(
                    context,
                    registration,
                    registrationCollector,
                    lifetimeClassifier,
                    semanticModelsByTree,
                    reportedDiagnostics);
            }
            else if (!registration.HasImplementationInstance && registration.ImplementationType != null)
            {
                AnalyzeConstructorRegistration(
                    context,
                    registration,
                    registrationCollector,
                    lifetimeClassifier,
                    reportedDiagnostics);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var factory = registration.FactoryExpression;
        if (factory == null)
        {
            return;
        }

        var semanticModel = GetSemanticModel(factory.SyntaxTree, context.Compilation, semanticModelsByTree);
        var invocations = FactoryAnalysis.GetFactoryInvocations(factory, semanticModel);
        var factoryKeyContext = CreateFactoryKeyContext(factory, semanticModel, registration);

        foreach (var invocation in invocations)
        {
            var invocationSemanticModel = GetSemanticModel(invocation.SyntaxTree, context.Compilation, semanticModelsByTree);
            var symbolInfo = invocationSemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
            var methodName = sourceMethod.Name;
            bool isKeyedResolution =
                methodName is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices";
            bool isEnumerableResolution = methodName is "GetServices" or "GetKeyedServices";

            if (!IsServiceResolutionMethod(methodSymbol))
            {
                if (FactoryAnalysis.TryGetActivatorUtilitiesImplementationType(
                        invocation,
                        invocationSemanticModel,
                        out var implementationType,
                        out var hasExplicitConstructorArguments) &&
                    !hasExplicitConstructorArguments)
                {
                    AnalyzeActivatorUtilitiesConstruction(
                        context,
                        registration,
                        registrationCollector,
                        lifetimeClassifier,
                        implementationType,
                        invocation.GetLocation(),
                        reportedDiagnostics);
                }

                continue;
            }

            var dependencyType = GetResolvedDependencyType(invocation, methodSymbol, invocationSemanticModel);
            if (dependencyType == null)
            {
                continue;
            }

            object? key = null;
            bool isKeyed = false;
            if (isKeyedResolution)
            {
                if (!TryExtractKeyFromResolution(
                        invocation,
                        methodSymbol,
                        invocationSemanticModel,
                        factoryKeyContext,
                        out key))
                {
                    continue;
                }

                isKeyed = true;
            }

            if (!TryGetCaptiveDependencyLifetime(
                    registration.Lifetime,
                    registrationCollector,
                    lifetimeClassifier,
                    dependencyType,
                    key,
                    isKeyed,
                    isEnumerableResolution,
                    out var dependencyLifetime))
            {
                continue;
            }

            ReportDiagnostic(
                context,
                registration,
                invocation.GetLocation(),
                registration.ServiceType.Name,
                dependencyType,
                dependencyLifetime,
                key,
                isKeyed,
                reportedDiagnostics);
        }
    }

    private static ITypeSymbol? GetResolvedDependencyType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            return methodSymbol.TypeArguments[0];
        }

        // Non-generic overloads pass the service type as a System.Type argument.
        var serviceTypeExpression = GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType");
        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        return null;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetServices" or
            "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"))
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
                IsKeyedServiceProvider(receiverType))
            {
                return true;
            }
        }

        return IsSystemIServiceProvider(containingType) ||
               IsKeyedServiceProvider(containingType);
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type) =>
        type.Name == "IServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "System";

    private static bool IsKeyedServiceProvider(ITypeSymbol type) =>
        type.Name == "IKeyedServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static bool TryExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        FactoryKeyContext factoryKeyContext,
        out object? key)
    {
        key = null;
        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        // Fallback for simplified test stubs or unusual signatures.
        if (keyExpression is null)
        {
            if (invocation.ArgumentList.Arguments.Count == 1)
            {
                keyExpression = invocation.ArgumentList.Arguments[0].Expression;
            }
            else if (invocation.ArgumentList.Arguments.Count >= 2)
            {
                keyExpression = invocation.ArgumentList.Arguments[1].Expression;
            }
        }

        if (keyExpression is null)
        {
            return false;
        }

        if (TryGetInheritedFactoryKey(keyExpression, semanticModel, factoryKeyContext, out key))
        {
            return true;
        }

        return SyntaxValueHelpers.TryExtractServiceKeyValue(keyExpression, semanticModel, out key, out _) &&
               !SyntaxValueHelpers.IsKeyedServiceAnyKey(key);
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

            // Reduced extension method calls omit the receiver argument from the invocation argument list.
            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static void AnalyzeConstructorRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var implementationType = registration.ImplementationType!;
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);

        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerableDependency);
                var serviceKey = GetServiceKey(parameter, registration);
                if (serviceKey.IsUnknown ||
                    SyntaxValueHelpers.IsKeyedServiceAnyKey(serviceKey.Key))
                {
                    continue;
                }

                if (TryGetCaptiveDependencyLifetime(
                        registration.Lifetime,
                        registrationCollector,
                        lifetimeClassifier,
                        parameterType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        isEnumerableDependency,
                        out var dependencyLifetime))
                {
                    ReportDiagnostic(
                        context,
                        registration,
                        registration.Location,
                        implementationType.Name,
                        parameterType,
                        dependencyLifetime,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        reportedDiagnostics);
                }
            }
        }
    }

    private static void AnalyzeActivatorUtilitiesConstruction(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        INamedTypeSymbol implementationType,
        Location diagnosticLocation,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);
        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerableDependency);
                var serviceKey = GetServiceKey(parameter);
                if (serviceKey.IsUnknown ||
                    SyntaxValueHelpers.IsKeyedServiceAnyKey(serviceKey.Key) ||
                    !TryGetCaptiveDependencyLifetime(
                        registration.Lifetime,
                        registrationCollector,
                        lifetimeClassifier,
                        parameterType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        isEnumerableDependency,
                        out var dependencyLifetime))
                {
                    continue;
                }

                ReportDiagnostic(
                    context,
                    registration,
                    diagnosticLocation,
                    registration.ServiceType.Name,
                    parameterType,
                    dependencyLifetime,
                    serviceKey.Key,
                    serviceKey.IsKeyed,
                    reportedDiagnostics);
            }
        }
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        Location location,
        string consumerName,
        ITypeSymbol dependencyType,
        ServiceLifetime dependencyLifetime,
        object? key,
        bool isKeyed,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var reportKey = new ReportedDiagnosticKey(
            registration.Location.SourceSpan.Start,
            dependencyType,
            key,
            isKeyed);
        if (!reportedDiagnostics.Add(reportKey))
        {
            return;
        }

        var lifetimeName = dependencyLifetime.ToString().ToLowerInvariant();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DependencyLifetimePropertyName, lifetimeName);
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.CaptiveDependency,
            location,
            additionalLocations: null,
            properties: properties,
            consumerName,
            lifetimeName,
            dependencyType.Name);

        context.ReportDiagnostic(diagnostic);
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

    private static KeyedServiceHelpers.ServiceKeyRequest GetServiceKey(
        IParameterSymbol parameter,
        ServiceRegistration registration) =>
        KeyedServiceHelpers.GetServiceKey(
            parameter,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);

    private static KeyedServiceHelpers.ServiceKeyRequest GetServiceKey(IParameterSymbol parameter) =>
        KeyedServiceHelpers.GetServiceKey(
            parameter,
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null);

    private static bool TryGetCaptiveDependencyLifetime(
        ServiceLifetime consumerLifetime,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        bool isEnumerableDependency,
        out ServiceLifetime dependencyLifetime)
    {
        if (isEnumerableDependency &&
            TryGetEnumerableCaptiveDependencyLifetime(
                consumerLifetime,
                registrationCollector,
                dependencyType,
                key,
                isKeyed,
                out dependencyLifetime))
        {
            return true;
        }

        var registeredLifetime = GetDependencyLifetime(
            registrationCollector,
            lifetimeClassifier,
            dependencyType,
            key,
            isKeyed);
        if (registeredLifetime is not null &&
            IsCaptiveDependency(consumerLifetime, registeredLifetime.Value))
        {
            dependencyLifetime = registeredLifetime.Value;
            return true;
        }

        dependencyLifetime = default;
        return false;
    }

    private static bool TryGetEnumerableCaptiveDependencyLifetime(
        ServiceLifetime consumerLifetime,
        RegistrationCollector registrationCollector,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        out ServiceLifetime dependencyLifetime)
    {
        foreach (var registration in GetMatchingRegistrations(
                     registrationCollector,
                     dependencyType,
                     key,
                     isKeyed))
        {
            if (!IsCaptiveDependency(consumerLifetime, registration.Lifetime))
            {
                continue;
            }

            dependencyLifetime = registration.Lifetime;
            return true;
        }

        dependencyLifetime = default;
        return false;
    }

    private static IEnumerable<ServiceRegistration> GetMatchingRegistrations(
        RegistrationCollector registrationCollector,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, dependencyType))
            {
                yield return registration;
                continue;
            }

            if (dependencyType is not INamedTypeSymbol namedDependencyType ||
                !namedDependencyType.IsGenericType ||
                namedDependencyType.IsUnboundGenericType)
            {
                continue;
            }

            var openDependencyType = namedDependencyType.ConstructUnboundGenericType();
            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, openDependencyType))
            {
                yield return registration;
            }
        }
    }

    private static ServiceLifetime? GetDependencyLifetime(
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        var registeredLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
        if (registeredLifetime is not null)
        {
            return registeredLifetime;
        }

        return lifetimeClassifier.TryGetLifetime(dependencyType, isKeyed, out var knownLifetime)
            ? knownLifetime
            : null;
    }

    private static ITypeSymbol UnwrapEnumerableDependency(ITypeSymbol dependencyType, out bool isEnumerableDependency)
    {
        isEnumerableDependency = false;
        if (dependencyType is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            isEnumerableDependency = true;
            return namedType.TypeArguments[0];
        }

        return dependencyType;
    }

    private static FactoryKeyContext CreateFactoryKeyContext(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        ServiceRegistration registration)
    {
        if (!registration.IsKeyed ||
            SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key) ||
            !TryGetFactoryKeyParameter(factoryExpression, semanticModel, out var keyParameter))
        {
            return FactoryKeyContext.None;
        }

        return new FactoryKeyContext(keyParameter, registration.Key);
    }

    private static bool TryGetFactoryKeyParameter(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out IParameterSymbol keyParameter)
    {
        keyParameter = null!;
        factoryExpression = UnwrapExpression(factoryExpression);

        switch (factoryExpression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                when parenthesizedLambda.ParameterList.Parameters.Count >= 2:
                return TryGetParameterSymbol(
                    parenthesizedLambda.ParameterList.Parameters[1],
                    semanticModel,
                    out keyParameter);

            case AnonymousMethodExpressionSyntax anonymousMethod
                when anonymousMethod.ParameterList?.Parameters.Count >= 2:
                return TryGetParameterSymbol(
                    anonymousMethod.ParameterList.Parameters[1],
                    semanticModel,
                    out keyParameter);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                var symbolInfo = semanticModel.GetSymbolInfo(factoryExpression);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                                   symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (methodSymbol?.Parameters.Length >= 2)
                {
                    keyParameter = methodSymbol.Parameters[1];
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetParameterSymbol(
        ParameterSyntax parameterSyntax,
        SemanticModel semanticModel,
        out IParameterSymbol parameterSymbol)
    {
        if (semanticModel.GetDeclaredSymbol(parameterSyntax) is IParameterSymbol symbol)
        {
            parameterSymbol = symbol;
            return true;
        }

        parameterSymbol = null!;
        return false;
    }

    private static bool TryGetInheritedFactoryKey(
        ExpressionSyntax keyExpression,
        SemanticModel semanticModel,
        FactoryKeyContext factoryKeyContext,
        out object? key)
    {
        key = null;
        if (factoryKeyContext.KeyParameter is null ||
            semanticModel.GetSymbolInfo(keyExpression).Symbol is not IParameterSymbol parameterSymbol ||
            !SymbolEqualityComparer.Default.Equals(parameterSymbol, factoryKeyContext.KeyParameter))
        {
            return false;
        }

        key = factoryKeyContext.InheritedKey;
        return true;
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
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

    private static bool IsCaptiveDependency(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        return consumerLifetime == ServiceLifetime.Singleton &&
               dependencyLifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;
    }
}
