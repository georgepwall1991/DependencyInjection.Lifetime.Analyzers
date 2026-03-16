using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public int DiagnosticStart { get; }

        public ReportedDiagnosticKey(
            int registrationStart,
            ITypeSymbol dependencyType,
            object? key,
            bool isKeyed,
            int diagnosticStart)
        {
            RegistrationStart = registrationStart;
            DependencyType = dependencyType;
            Key = key;
            IsKeyed = isKeyed;
            DiagnosticStart = diagnosticStart;
        }

        public bool Equals(ReportedDiagnosticKey other)
        {
            return RegistrationStart == other.RegistrationStart &&
                   SymbolEqualityComparer.Default.Equals(DependencyType, other.DependencyType) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed &&
                   DiagnosticStart == other.DiagnosticStart;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = RegistrationStart;
                hashCode = (hashCode * 397) ^ SymbolEqualityComparer.Default.GetHashCode(DependencyType);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                hashCode = (hashCode * 397) ^ DiagnosticStart;
                return hashCode;
            }
        }

        public override bool Equals(object? obj) =>
            obj is ReportedDiagnosticKey other && Equals(other);
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

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeCaptiveDependencies(endContext, registrationCollector));
        });
    }

    private static void AnalyzeCaptiveDependencies(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector)
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
                    semanticModelsByTree,
                    reportedDiagnostics);
            }
            else if (registration.ImplementationType != null && !registration.HasImplementationInstance)
            {
                AnalyzeConstructorRegistration(
                    context,
                    registration,
                    registrationCollector,
                    reportedDiagnostics);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
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

        foreach (var invocation in invocations)
        {
            var invocationSemanticModel = GetSemanticModel(invocation.SyntaxTree, context.Compilation, semanticModelsByTree);
            var symbolInfo = invocationSemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var methodName = methodSymbol.Name;
            bool isKeyedResolution = methodName == "GetKeyedService" || methodName == "GetRequiredKeyedService";

            if (methodName != "GetService" && methodName != "GetRequiredService" && !isKeyedResolution)
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
                key = ExtractKeyFromResolution(invocation, methodSymbol, invocationSemanticModel);
                isKeyed = true;
            }

            var dependencyLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
            if (dependencyLifetime is null ||
                !IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
            {
                continue;
            }

            ReportDiagnostic(
                context,
                registration,
                invocation.GetLocation(),
                registration.ServiceType.Name,
                dependencyType,
                dependencyLifetime.Value,
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

    private static object? ExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
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

        return keyExpression is null
            ? null
            : ExtractConstantValue(keyExpression, semanticModel);
    }

    private static object? ExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var constantValue = semanticModel.GetConstantValue(expr);
        if (constantValue.HasValue)
        {
            return constantValue.Value;
        }
        return null;
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
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var implementationType = registration.ImplementationType!;
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);

        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = parameter.Type;
                var (key, isKeyed) = GetServiceKey(parameter);
                var dependencyLifetime = registrationCollector.GetLifetime(parameterType, key, isKeyed);

                if (dependencyLifetime is null)
                {
                    // Unknown dependency - don't report
                    continue;
                }

                // Check for captive dependency: longer-lived service capturing shorter-lived dependency
                if (IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
                {
                    ReportDiagnostic(
                        context,
                        registration,
                        registration.Location,
                        implementationType.Name,
                        parameterType,
                        dependencyLifetime.Value,
                        key,
                        isKeyed,
                        reportedDiagnostics);
                }
            }
        }
    }

    private static void AnalyzeActivatorUtilitiesConstruction(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        INamedTypeSymbol implementationType,
        Location diagnosticLocation,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);
        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var (key, isKeyed) = GetServiceKey(parameter);
                var dependencyLifetime = registrationCollector.GetLifetime(parameter.Type, key, isKeyed);
                if (dependencyLifetime is null ||
                    !IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
                {
                    continue;
                }

                ReportDiagnostic(
                    context,
                    registration,
                    diagnosticLocation,
                    registration.ServiceType.Name,
                    parameter.Type,
                    dependencyLifetime.Value,
                    key,
                    isKeyed,
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
            isKeyed,
            location.SourceSpan.Start);
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

    private static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                (attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection"))
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    return (attribute.ConstructorArguments[0].Value, true);
                }
            }
        }
        return (null, false);
    }

    private static bool IsCaptiveDependency(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        return consumerLifetime == ServiceLifetime.Singleton &&
               dependencyLifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;
    }
}
