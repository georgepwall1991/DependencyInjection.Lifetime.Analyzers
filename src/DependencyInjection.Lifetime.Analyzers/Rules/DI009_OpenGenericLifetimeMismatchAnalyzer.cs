using System.Collections.Immutable;
using System.Collections.Generic;
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
    internal const string DependencyLifetimePropertyName = "DependencyLifetime";

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

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for open generic captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeOpenGenericRegistrations(endContext, registrationCollector, wellKnownTypes));
        });
    }

    private static void AnalyzeOpenGenericRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes)
    {
        var resolutionEngine = new DependencyResolutionEngine(registrationCollector, wellKnownTypes);

        foreach (var registration in registrationCollector.AllRegistrations)
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
            var constructors = ConstructorSelection.GetLikelyActivationConstructors(
                genericDefinition,
                parameter => IsResolvableConstructorParameter(parameter, resolutionEngine))
                .ToList();

            // Multiple equally-greedy activation candidates are ambiguous at runtime.
            // Stay quiet rather than reporting a captive dependency on a constructor
            // the container may not actually select.
            if (constructors.Count != 1)
            {
                continue;
            }

            foreach (var constructor in constructors)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    if (!TryGetDependencyInfo(
                            parameter.Type,
                            GetServiceKey(parameter),
                            registrationCollector,
                            out var dependencyType,
                            out var dependencyLifetime) ||
                        dependencyLifetime is not { } dependencyLifetimeValue)
                    {
                        continue;
                    }

                    // Check for captive dependency: singleton capturing scoped or transient
                    if (dependencyLifetimeValue > ServiceLifetime.Singleton)
                    {
                        var lifetimeName = dependencyLifetimeValue.ToString().ToLowerInvariant();
                        var diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.OpenGenericLifetimeMismatch,
                            registration.Location,
                            ImmutableDictionary<string, string?>.Empty.Add(DependencyLifetimePropertyName, lifetimeName),
                            registration.ImplementationType.Name,
                            lifetimeName,
                            dependencyType.Name);

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static bool IsResolvableConstructorParameter(
        IParameterSymbol parameter,
        DependencyResolutionEngine resolutionEngine)
    {
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        var (key, isKeyed) = GetServiceKey(parameter);
        return resolutionEngine.ResolveServiceRequest(
                parameter.Type,
                key,
                isKeyed,
                assumeFrameworkServicesRegistered: true)
            .IsResolvable;
    }

    private static bool TryGetDependencyInfo(
        ITypeSymbol parameterType,
        (object? key, bool isKeyed) serviceKey,
        RegistrationCollector registrationCollector,
        out INamedTypeSymbol dependencyType,
        out ServiceLifetime? dependencyLifetime)
    {
        dependencyType = null!;
        dependencyLifetime = null;

        if (TryGetEnumerableElementType(parameterType, out var elementType))
        {
            dependencyType = elementType;
            dependencyLifetime = registrationCollector.GetLifetime(elementType, serviceKey.key, serviceKey.isKeyed);
            return dependencyLifetime is not null;
        }

        var nonGenericType = GetNonGenericTypeFromParameter(parameterType);
        if (nonGenericType is null)
        {
            return false;
        }

        dependencyType = nonGenericType;
        dependencyLifetime = registrationCollector.GetLifetime(nonGenericType, serviceKey.key, serviceKey.isKeyed);
        return dependencyLifetime is not null;
    }

    private static bool TryGetEnumerableElementType(
        ITypeSymbol parameterType,
        out INamedTypeSymbol elementType)
    {
        elementType = null!;

        if (parameterType is not INamedTypeSymbol
            {
                IsGenericType: true,
                TypeArguments.Length: 1
            } namedType)
        {
            return false;
        }

        if (namedType.Name != "IEnumerable" ||
            namedType.ContainingNamespace.ToDisplayString() != "System.Collections.Generic" ||
            namedType.TypeArguments[0] is not INamedTypeSymbol namedElementType)
        {
            return false;
        }

        elementType = namedElementType;
        return true;
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
