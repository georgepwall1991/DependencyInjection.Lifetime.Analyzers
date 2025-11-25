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
        foreach (var registration in registrationCollector.Registrations)
        {
            var implementationType = registration.ImplementationType;

            // Skip factory classes (name ends with "Factory")
            if (IsFactoryClass(implementationType))
            {
                continue;
            }

            // Skip middleware classes (has Invoke or InvokeAsync method)
            if (IsMiddlewareClass(implementationType))
            {
                continue;
            }

            var constructors = implementationType.Constructors;

            foreach (var constructor in constructors)
            {
                // Skip static and private constructors
                if (constructor.IsStatic || constructor.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }

                foreach (var parameter in constructor.Parameters)
                {
                    var parameterType = parameter.Type;

                    // Check if parameter is IServiceProvider, IServiceScopeFactory, or IKeyedServiceProvider
                    if (wellKnownTypes.IsServiceProvider(parameterType))
                    {
                        ReportDiagnostic(context, registration, implementationType, "IServiceProvider");
                    }
                    else if (wellKnownTypes.IsServiceScopeFactory(parameterType))
                    {
                        ReportDiagnostic(context, registration, implementationType, "IServiceScopeFactory");
                    }
                    else if (wellKnownTypes.IsKeyedServiceProvider(parameterType))
                    {
                        ReportDiagnostic(context, registration, implementationType, "IKeyedServiceProvider");
                    }
                }
            }
        }
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
        // Check if class name ends with "Factory"
        if (type.Name.EndsWith("Factory"))
        {
            return true;
        }

        // Check if any interface implemented ends with "Factory"
        return type.AllInterfaces.Any(i => i.Name.EndsWith("Factory"));
    }

    private static bool IsMiddlewareClass(INamedTypeSymbol type)
    {
        // Check if the class has an Invoke or InvokeAsync method (middleware pattern)
        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.Name is "Invoke" or "InvokeAsync" &&
                      m.DeclaredAccessibility == Accessibility.Public);
    }
}
