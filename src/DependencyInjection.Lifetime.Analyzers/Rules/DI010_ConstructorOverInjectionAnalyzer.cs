using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects constructor over-injection - when a registered service has too many dependencies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI010_ConstructorOverInjectionAnalyzer : DiagnosticAnalyzer
{
    private const int DefaultMaxDependencies = 4;
    private const string MaxDependenciesOption = "dotnet_code_quality.DI010.max_dependencies";

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
            var maxDependenciesResolver = CreateMaxDependenciesResolver(
                compilationContext.Options.AnalyzerConfigOptionsProvider);
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeConstructorInjection(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    maxDependenciesResolver,
                    semanticModelsByTree));
        });
    }

    private static void AnalyzeConstructorInjection(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        Func<SyntaxTree?, int> maxDependenciesResolver,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        var resolutionEngine = new DependencyResolutionEngine(registrationCollector, wellKnownTypes);

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.HasImplementationInstance)
            {
                continue;
            }

            var maxDependencies = maxDependenciesResolver(registration.Location.SourceTree);

            if (registration.FactoryExpression is not null)
            {
                AnalyzeFactoryRegistration(
                    context,
                    registration,
                    semanticModelsByTree,
                    resolutionEngine,
                    wellKnownTypes,
                    maxDependencies);
                continue;
            }

            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                continue;
            }

            var constructors = ConstructorSelection.GetLikelyActivationConstructors(
                implementationType,
                parameter => IsResolvableConstructorParameter(parameter, resolutionEngine));

            foreach (var constructor in constructors)
            {
                ReportIfOverInjected(
                    context,
                    registration.Location,
                    implementationType.Name,
                    constructor,
                    wellKnownTypes,
                    maxDependencies);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        DependencyResolutionEngine resolutionEngine,
        WellKnownTypes? wellKnownTypes,
        int maxDependencies)
    {
        if (!semanticModelsByTree.TryGetValue(registration.FactoryExpression!.SyntaxTree, out var semanticModel))
        {
            return;
        }

        if (!FactoryAnalysis.TryGetFactoryReturnExpression(
                registration.FactoryExpression,
                semanticModel,
                out var returnExpression))
        {
            return;
        }

        if (TryGetConstructedImplementationFromExpression(
                returnExpression,
                semanticModel,
                out var constructor,
                out var implementationType))
        {
            ReportIfOverInjected(
                context,
                registration.Location,
                implementationType.Name,
                constructor,
                wellKnownTypes,
                maxDependencies);
            return;
        }

        if (!TryGetActivatorUtilitiesImplementationFromExpression(
                returnExpression,
                semanticModel,
                out implementationType))
        {
            return;
        }

        var constructors = ConstructorSelection.GetLikelyActivationConstructors(
            implementationType,
            parameter => IsResolvableConstructorParameter(parameter, resolutionEngine));

        foreach (var likelyConstructor in constructors)
        {
            ReportIfOverInjected(
                context,
                registration.Location,
                implementationType.Name,
                likelyConstructor,
                wellKnownTypes,
                maxDependencies);
        }
    }

    private static bool TryGetConstructedImplementationFromExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out IMethodSymbol constructor,
        out INamedTypeSymbol implementationType)
    {
        constructor = null!;
        implementationType = null!;

        var operation = semanticModel.GetOperation(expression);
        while (operation is IConversionOperation conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        if (operation is not IObjectCreationOperation
            {
                Constructor: not null,
                Type: INamedTypeSymbol namedImplementationType
            } objectCreationOperation)
        {
            return false;
        }

        constructor = objectCreationOperation.Constructor;
        implementationType = namedImplementationType;
        return true;
    }

    private static bool TryGetActivatorUtilitiesImplementationFromExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out INamedTypeSymbol implementationType)
    {
        implementationType = null!;

        if (expression is not InvocationExpressionSyntax invocation ||
            !FactoryAnalysis.TryGetActivatorUtilitiesImplementationType(
                invocation,
                semanticModel,
                out implementationType,
                out var hasExplicitConstructorArguments) ||
            hasExplicitConstructorArguments)
        {
            return false;
        }

        return true;
    }

    private static bool IsResolvableConstructorParameter(
        IParameterSymbol parameter,
        DependencyResolutionEngine resolutionEngine)
    {
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        var (key, isKeyed) = KeyedServiceHelpers.GetServiceKey(parameter);
        return resolutionEngine.ResolveServiceRequest(
                parameter.Type,
                key,
                isKeyed,
                assumeFrameworkServicesRegistered: true)
            .IsResolvable;
    }

    private static void ReportIfOverInjected(
        CompilationAnalysisContext context,
        Location location,
        string implementationName,
        IMethodSymbol constructor,
        WellKnownTypes? wellKnownTypes,
        int maxDependencies)
    {
        var dependencyCount = CountMeaningfulDependencies(constructor, wellKnownTypes);
        if (dependencyCount <= maxDependencies)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ConstructorOverInjection,
            location,
            implementationName,
            dependencyCount);

        context.ReportDiagnostic(diagnostic);
    }

    private static int CountMeaningfulDependencies(IMethodSymbol constructor, WellKnownTypes? wellKnownTypes)
    {
        var count = 0;

        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
            {
                continue;
            }

            if (IsExcludedType(parameter.Type, wellKnownTypes))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static bool IsExcludedType(ITypeSymbol type, WellKnownTypes? wellKnownTypes)
    {
        if (type.SpecialType != SpecialType.None || type.IsValueType)
        {
            return true;
        }

        if (wellKnownTypes is null)
        {
            return false;
        }

        return wellKnownTypes.IsLogger(type) ||
               wellKnownTypes.IsOptionsAbstraction(type) ||
               wellKnownTypes.IsConfiguration(type) ||
               wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type);
    }

    private static Func<SyntaxTree?, int> CreateMaxDependenciesResolver(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var valuesByTree = new ConcurrentDictionary<SyntaxTree, int>();
        var hasGlobalValue = TryParseMaxDependencies(
            optionsProvider.GlobalOptions,
            out var globalValue);

        return syntaxTree =>
        {
            if (syntaxTree is null)
            {
                return hasGlobalValue ? globalValue : DefaultMaxDependencies;
            }

            return valuesByTree.GetOrAdd(syntaxTree, tree =>
            {
                if (TryParseMaxDependencies(optionsProvider.GetOptions(tree), out var treeValue))
                {
                    return treeValue;
                }

                return hasGlobalValue ? globalValue : DefaultMaxDependencies;
            });
        };
    }

    private static bool TryParseMaxDependencies(
        AnalyzerConfigOptions options,
        out int maxDependencies)
    {
        maxDependencies = DefaultMaxDependencies;

        if (!options.TryGetValue(MaxDependenciesOption, out var optionValue))
        {
            return false;
        }

        if (!int.TryParse(optionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) ||
            parsedValue < 0)
        {
            return false;
        }

        maxDependencies = parsedValue;
        return true;
    }
}
