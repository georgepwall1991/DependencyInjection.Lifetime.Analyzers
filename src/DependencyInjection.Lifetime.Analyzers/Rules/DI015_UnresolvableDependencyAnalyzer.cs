using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects registered services with dependencies that are not registered.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI015_UnresolvableDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string AssumeFrameworkServicesRegisteredOption =
        "dotnet_code_quality.DI015.assume_framework_services_registered";

    internal const string MissingDependencyTypeNamePropertyName = "MissingDependencyTypeName";
    internal const string MissingDependencyCanSelfBindPropertyName = "MissingDependencyCanSelfBind";
    internal const string MissingDependencyPathLengthPropertyName = "MissingDependencyPathLength";
    internal const string MissingDependencyCountPropertyName = "MissingDependencyCount";
    internal const string MissingDependencyIsKeyedPropertyName = "MissingDependencyIsKeyed";
    internal const string RegistrationLifetimePropertyName = "RegistrationLifetime";
    internal const string DependencySourceKindPropertyName = "DependencySourceKind";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UnresolvableDependency);

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
            var resolutionEngine = new DependencyResolutionEngine(registrationCollector, wellKnownTypes);
            var assumeFrameworkServicesRegisteredResolver = CreateAssumeFrameworkServicesRegisteredResolver(
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
                endContext => AnalyzeRegistrations(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    resolutionEngine,
                    assumeFrameworkServicesRegisteredResolver,
                    semanticModelsByTree));
        });
    }

    private static void AnalyzeRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        DependencyResolutionEngine resolutionEngine,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.FactoryExpression is not null)
            {
                AnalyzeFactoryRegistration(
                    context,
                    registration,
                    wellKnownTypes,
                    resolutionEngine,
                    assumeFrameworkServicesRegisteredResolver,
                    semanticModelsByTree);
                continue;
            }

            if (registration.ImplementationType is null)
            {
                continue;
            }

            var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(
                registration.Location.SourceTree);
            var resolutionResult = resolutionEngine.ResolveRegistration(
                registration,
                assumeFrameworkServicesRegistered);

            ReportMissingDependencies(
                context,
                registration.Location,
                registration.ServiceType.Name,
                registration.Lifetime,
                DependencySourceKind.ConstructorParameter,
                resolutionResult);
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        WellKnownTypes? wellKnownTypes,
        DependencyResolutionEngine resolutionEngine,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (!semanticModelsByTree.TryGetValue(registration.FactoryExpression!.SyntaxTree, out var semanticModel))
        {
            return;
        }

        var requests = FactoryDependencyAnalysis.GetDependencyRequests(
            registration.FactoryExpression,
            semanticModel,
            wellKnownTypes);

        foreach (var request in requests)
        {
            var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(
                request.SourceLocation.SourceTree);
            var resolutionResult = resolutionEngine.ResolveFactoryRequest(
                registration,
                request,
                assumeFrameworkServicesRegistered);

            ReportMissingDependencies(
                context,
                request.SourceLocation,
                registration.ServiceType.Name,
                registration.Lifetime,
                request.SourceKind,
                resolutionResult);
        }
    }

    private static void ReportMissingDependencies(
        CompilationAnalysisContext context,
        Location location,
        string serviceTypeName,
        ServiceLifetime registrationLifetime,
        DependencySourceKind sourceKind,
        ResolutionResult resolutionResult)
    {
        if (resolutionResult.IsResolvable ||
            resolutionResult.Confidence != ResolutionConfidence.High ||
            resolutionResult.MissingDependencies.IsDefaultOrEmpty)
        {
            return;
        }

        var missingDependencyCount = resolutionResult.MissingDependencies.Length.ToString(CultureInfo.InvariantCulture);

        foreach (var missingDependency in resolutionResult.MissingDependencies)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(
                    MissingDependencyTypeNamePropertyName,
                    DependencyResolutionEngine.GetGlobalTypeDisplayName(missingDependency.Type))
                .Add(
                    MissingDependencyCanSelfBindPropertyName,
                    DependencyResolutionEngine.CanSelfBind(missingDependency.Type)
                        .ToString(CultureInfo.InvariantCulture))
                .Add(
                    MissingDependencyPathLengthPropertyName,
                    missingDependency.PathLength.ToString(CultureInfo.InvariantCulture))
                .Add(MissingDependencyCountPropertyName, missingDependencyCount)
                .Add(
                    MissingDependencyIsKeyedPropertyName,
                    missingDependency.IsKeyed.ToString(CultureInfo.InvariantCulture))
                .Add(RegistrationLifetimePropertyName, registrationLifetime.ToString())
                .Add(DependencySourceKindPropertyName, sourceKind.ToString());

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UnresolvableDependency,
                location,
                properties,
                serviceTypeName,
                DependencyResolutionEngine.FormatDependencyName(
                    missingDependency.Type,
                    missingDependency.Key,
                    missingDependency.IsKeyed));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static Func<SyntaxTree?, bool> CreateAssumeFrameworkServicesRegisteredResolver(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var valuesByTree = new ConcurrentDictionary<SyntaxTree, bool>();
        var hasGlobalValue = TryParseAssumeFrameworkServicesRegistered(
            optionsProvider.GlobalOptions,
            out var globalValue);

        return syntaxTree =>
        {
            if (syntaxTree is null)
            {
                return hasGlobalValue ? globalValue : true;
            }

            return valuesByTree.GetOrAdd(syntaxTree, tree =>
            {
                if (TryParseAssumeFrameworkServicesRegistered(
                        optionsProvider.GetOptions(tree),
                        out var treeValue))
                {
                    return treeValue;
                }

                return hasGlobalValue ? globalValue : true;
            });
        };
    }

    private static bool TryParseAssumeFrameworkServicesRegistered(
        AnalyzerConfigOptions options,
        out bool value)
    {
        value = true;

        if (!options.TryGetValue(AssumeFrameworkServicesRegisteredOption, out var optionValue))
        {
            return false;
        }

        if (bool.TryParse(optionValue, out value))
        {
            return true;
        }

        switch (optionValue)
        {
            case "1":
                value = true;
                return true;
            case "0":
                value = false;
                return true;
            default:
                return false;
        }
    }
}
