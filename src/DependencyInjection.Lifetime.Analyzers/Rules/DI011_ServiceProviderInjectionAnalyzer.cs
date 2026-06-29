using System;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects IServiceProvider or IServiceScopeFactory being injected into constructors.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI011_ServiceProviderInjectionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ServiceProviderInjection);

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

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for IServiceProvider/IServiceScopeFactory injection at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeServiceProviderInjection(endContext, registrationCollector, wellKnownTypes));
        });
    }

    private static void AnalyzeServiceProviderInjection(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes)
    {
        var resolutionEngine = new DependencyResolutionEngine(registrationCollector, wellKnownTypes);

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.HasImplementationInstance ||
                registration.FactoryExpression is not null)
            {
                continue;
            }

            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                continue;
            }

            var activationType = implementationType.IsUnboundGenericType
                ? implementationType.OriginalDefinition
                : implementationType;

            // Skip factory-shaped classes, but do not let name-only factory markers
            // suppress provider-injection diagnostics.
            if (IsFactoryClass(activationType))
            {
                continue;
            }

            // Skip real ASP.NET Core middleware classes.
            if (IsMiddlewareClass(activationType))
            {
                continue;
            }

            // Skip infrastructure abstractions that are expected to resolve services dynamically.
            if (IsHostedServiceClass(activationType) || IsEndpointFilterFactoryClass(activationType))
            {
                continue;
            }

            var constructors = ConstructorSelection.GetLikelyActivationConstructors(
                activationType,
                parameter => IsResolvableConstructorParameter(parameter, resolutionEngine));

            foreach (var constructor in constructors)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    var parameterType = parameter.Type;

                    // Check if parameter is IServiceProvider, IServiceScopeFactory, or IKeyedServiceProvider
                    if (wellKnownTypes.IsServiceProvider(parameterType))
                    {
                        ReportDiagnostic(context, registration, activationType, "IServiceProvider");
                    }
                    else if (wellKnownTypes.IsServiceScopeFactory(parameterType) &&
                             registration.Lifetime != ServiceLifetime.Singleton)
                    {
                        ReportDiagnostic(context, registration, activationType, "IServiceScopeFactory");
                    }
                    else if (wellKnownTypes.IsKeyedServiceProvider(parameterType))
                    {
                        ReportDiagnostic(context, registration, activationType, "IKeyedServiceProvider");
                    }
                }
            }
        }
    }

    private static bool IsResolvableConstructorParameter(
        IParameterSymbol parameter,
        DependencyResolutionEngine resolutionEngine)
    {
        var (key, isKeyed) = GetServiceKey(parameter);
        return resolutionEngine.ResolveServiceRequest(
                parameter.Type,
                key,
                isKeyed,
                assumeFrameworkServicesRegistered: true)
            .IsResolvable;
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        INamedTypeSymbol implementationType,
        string injectedTypeName)
    {
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ServiceProviderInjection,
            registration.Location,
            implementationType.Name,
            injectedTypeName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsFactoryClass(INamedTypeSymbol type)
    {
        if (!IsFactoryNamedType(type) &&
            !type.AllInterfaces.Any(IsFactoryNamedType))
        {
            return false;
        }

        return HasFactoryLikeMember(type) ||
               type.AllInterfaces
                   .Where(IsFactoryNamedType)
                   .Any(HasFactoryLikeMember);
    }

    private static bool IsFactoryNamedType(INamedTypeSymbol type)
    {
        return type.Name.EndsWith("Factory", StringComparison.Ordinal);
    }

    private static bool HasFactoryLikeMember(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IMethodSymbol>().Any(IsFactoryLikeMethod))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFactoryLikeMethod(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary ||
            method.ReturnsVoid)
        {
            return false;
        }

        if (method.ReturnType is INamedTypeSymbol namedType &&
            IsNonGenericTaskLikeType(namedType))
        {
            return false;
        }

        return true;
    }

    private static bool IsNonGenericTaskLikeType(INamedTypeSymbol type)
    {
        return MiddlewareHelpers.IsNonGenericTaskLikeType(type);
    }

    private static bool IsMiddlewareClass(INamedTypeSymbol type)
    {
        return MiddlewareHelpers.IsMiddlewareClass(type);
    }

    private static bool IsHostedServiceClass(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.Name == "IHostedService" &&
            i.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Hosting");
    }

    private static bool IsEndpointFilterFactoryClass(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.Name == "IEndpointFilterFactory" &&
            i.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Http");
    }

    private static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                attribute.AttributeClass.ContainingNamespace.ToDisplayString() ==
                "Microsoft.Extensions.DependencyInjection")
            {
                return (attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value
                    : null, true);
            }
        }

        return (null, false);
    }
}
