using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects open generic singleton services that capture scoped or transient dependencies.
/// Open generic registrations like AddSingleton(typeof(IRepository&lt;&gt;), typeof(Repository&lt;&gt;))
/// can capture shorter-lived dependencies when the generic type is instantiated.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI009_OpenGenericLifetimeMismatchAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.OpenGenericLifetimeMismatch);

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

            // Second pass: check for open generic captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeOpenGenericRegistrations(endContext, registrationCollector));
        });
    }

    private static void AnalyzeOpenGenericRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector)
    {
        foreach (var registration in registrationCollector.Registrations)
        {
            // Only check singletons for open generic captive dependencies
            if (registration.Lifetime != ServiceLifetime.Singleton)
            {
                continue;
            }

            if (registration.ImplementationType is null)
            {
                continue;
            }

            // Check if this is an open generic type
            if (!registration.ImplementationType.IsGenericType ||
                !registration.ImplementationType.IsUnboundGenericType)
            {
                continue;
            }

            // For open generics, we need to analyze the generic type definition's constructor
            var genericDefinition = registration.ImplementationType.OriginalDefinition;
            var constructors = ConstructorSelection.GetConstructorsToAnalyze(genericDefinition);

            foreach (var constructor in constructors)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    var parameterType = GetNonGenericTypeFromParameter(parameter.Type);
                    if (parameterType is null)
                    {
                        continue;
                    }

                    var (key, isKeyed) = GetServiceKey(parameter);
                    var dependencyLifetime = registrationCollector.GetLifetime(parameterType, key, isKeyed);
                    if (dependencyLifetime is null)
                    {
                        // Unknown dependency - don't report
                        continue;
                    }

                    // Check for captive dependency: singleton capturing scoped or transient
                    if (dependencyLifetime.Value > ServiceLifetime.Singleton)
                    {
                        var lifetimeName = dependencyLifetime.Value.ToString().ToLowerInvariant();
                        var diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.OpenGenericLifetimeMismatch,
                            registration.Location,
                            registration.ImplementationType.Name,
                            lifetimeName,
                            parameterType.Name);

                        context.ReportDiagnostic(diagnostic);
                    }
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

    /// <summary>
    /// Extracts the non-generic type from a parameter, handling generic parameters.
    /// For example, if the parameter is ILogger&lt;T&gt; where T is a type parameter,
    /// this returns ILogger (the generic type definition).
    /// </summary>
    private static INamedTypeSymbol? GetNonGenericTypeFromParameter(ITypeSymbol parameterType)
    {
        // If it's a type parameter, we can't determine the actual type at compile time
        if (parameterType is ITypeParameterSymbol)
        {
            return null;
        }

        // If it's a named type, return it directly (or its generic definition if unbound)
        if (parameterType is INamedTypeSymbol namedType)
        {
            // If it's a constructed generic type (e.g., ILogger<T>), return the original definition
            if (namedType.IsGenericType && !namedType.IsUnboundGenericType)
            {
                return namedType.OriginalDefinition;
            }

            return namedType;
        }

        return null;
    }
}
