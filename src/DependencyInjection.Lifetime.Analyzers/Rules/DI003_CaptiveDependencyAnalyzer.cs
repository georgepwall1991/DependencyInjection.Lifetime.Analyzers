using System.Collections.Immutable;
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
        foreach (var registration in registrationCollector.Registrations)
        {
            // Only check singletons and scoped services for captive dependencies
            if (registration.Lifetime == ServiceLifetime.Transient)
            {
                continue;
            }

            if (registration.FactoryExpression != null)
            {
                AnalyzeFactoryRegistration(context, registration, registrationCollector);
            }
            else if (registration.ImplementationType != null)
            {
                AnalyzeConstructorRegistration(context, registration, registrationCollector);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector)
    {
        var factory = registration.FactoryExpression;
        if (factory == null) return;

        // Scan for GetService/GetRequiredService calls within the factory
        var invocations = factory.DescendantNodes().OfType<InvocationExpressionSyntax>();
        #pragma warning disable RS1030
        var semanticModel = context.Compilation.GetSemanticModel(factory.SyntaxTree);
        #pragma warning restore RS1030

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var methodName = methodSymbol.Name;
            bool isKeyedResolution = methodName == "GetKeyedService" || methodName == "GetRequiredKeyedService";

            if (methodName != "GetService" && methodName != "GetRequiredService" && !isKeyedResolution)
            {
                continue;
            }

            var dependencyType = GetResolvedDependencyType(invocation, methodSymbol, semanticModel);
            if (dependencyType == null)
            {
                continue;
            }

            object? key = null;
            bool isKeyed = false;
            if (isKeyedResolution)
            {
                key = ExtractKeyFromResolution(invocation, methodSymbol, semanticModel);
                isKeyed = true;
            }

            var dependencyLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
            if (dependencyLifetime == null) continue;

            if (IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
            {
                var lifetimeName = dependencyLifetime.Value.ToString().ToLowerInvariant();
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.CaptiveDependency,
                    invocation.GetLocation(),
                    registration.ServiceType.Name, // Use ServiceType name for factories
                    lifetimeName,
                    dependencyType.Name);

                context.ReportDiagnostic(diagnostic);
            }
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

        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            if (methodSymbol.Parameters[i].Name == parameterName &&
                i < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[i].Expression;
            }
        }

        return null;
    }

    private static void AnalyzeConstructorRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector)
    {
        // Analyze the implementation type's constructor parameters
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
                    var lifetimeName = dependencyLifetime.Value.ToString().ToLowerInvariant();
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.CaptiveDependency,
                        registration.Location,
                        implementationType.Name,
                        lifetimeName,
                        parameterType.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
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
        // A captive dependency occurs when a longer-lived service captures a shorter-lived one
        // Lifetime order: Singleton (longest) > Scoped > Transient (shortest)
        return consumerLifetime < dependencyLifetime;
    }
}
