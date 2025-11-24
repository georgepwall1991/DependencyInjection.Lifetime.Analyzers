using System.Collections.Immutable;
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

            // Analyze the implementation type's constructor parameters
            var implementationType = registration.ImplementationType;
            var constructors = implementationType.Constructors;

            foreach (var constructor in constructors)
            {
                if (constructor.IsStatic || constructor.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }

                foreach (var parameter in constructor.Parameters)
                {
                    var parameterType = parameter.Type;
                    var dependencyLifetime = registrationCollector.GetLifetime(parameterType);

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
    }

    private static bool IsCaptiveDependency(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        // A captive dependency occurs when a longer-lived service captures a shorter-lived one
        // Lifetime order: Singleton (longest) > Scoped > Transient (shortest)
        return consumerLifetime < dependencyLifetime;
    }
}
