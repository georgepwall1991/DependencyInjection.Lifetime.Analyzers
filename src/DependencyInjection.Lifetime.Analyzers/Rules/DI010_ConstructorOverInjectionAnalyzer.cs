using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects constructor over-injection - when a registered service has too many dependencies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI010_ConstructorOverInjectionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The maximum number of constructor dependencies before triggering a warning.
    /// </summary>
    private const int MaxDependencies = 4;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ConstructorOverInjection);

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

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for over-injection at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeConstructorInjection(endContext, registrationCollector, wellKnownTypes));
        });
    }

    private static void AnalyzeConstructorInjection(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes)
    {
        foreach (var registration in registrationCollector.Registrations)
        {
            var implementationType = registration.ImplementationType;
            if (implementationType is null)
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

                // Count meaningful dependencies (exclude primitives, ILogger, CancellationToken, etc.)
                var dependencyCount = CountMeaningfulDependencies(constructor, wellKnownTypes);

                if (dependencyCount > MaxDependencies)
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.ConstructorOverInjection,
                        registration.Location,
                        implementationType.Name,
                        dependencyCount);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static int CountMeaningfulDependencies(IMethodSymbol constructor, WellKnownTypes? wellKnownTypes)
    {
        var count = 0;

        foreach (var parameter in constructor.Parameters)
        {
            var parameterType = parameter.Type;

            // Skip excluded types
            if (IsExcludedType(parameterType, wellKnownTypes))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static bool IsExcludedType(ITypeSymbol type, WellKnownTypes? wellKnownTypes)
    {
        // Exclude primitive types and strings
        if (type.SpecialType != SpecialType.None)
        {
            return true;
        }

        // Exclude value types (structs like CancellationToken, TimeSpan, etc.)
        if (type.IsValueType)
        {
            return true;
        }

        // Exclude string
        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        // Exclude ILogger and ILogger<T>
        var typeName = type.Name;
        if (typeName == "ILogger" || (type is INamedTypeSymbol { IsGenericType: true } namedType &&
                                       namedType.ConstructedFrom.Name == "ILogger"))
        {
            return true;
        }

        // Exclude IOptions<T>, IOptionsSnapshot<T>, IOptionsMonitor<T>
        if (typeName is "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor")
        {
            return true;
        }

        // Exclude IConfiguration
        if (typeName == "IConfiguration")
        {
            return true;
        }

        // Exclude IServiceProvider, IServiceScopeFactory, IKeyedServiceProvider
        // These are already warned about by DI011, so we don't want to double-penalize
        if (wellKnownTypes is not null && wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            return true;
        }

        return false;
    }
}
