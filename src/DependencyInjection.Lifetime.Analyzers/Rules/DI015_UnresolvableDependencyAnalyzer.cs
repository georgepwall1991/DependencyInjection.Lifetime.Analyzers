using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
                    assumeFrameworkServicesRegisteredResolver,
                    semanticModelsByTree));
        });
    }

    private static void AnalyzeRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        foreach (var registration in registrationCollector.Registrations)
        {
            if (registration.FactoryExpression is not null)
            {
                // Factory registrations control activation, so constructor analysis would be duplicate/noisy.
                AnalyzeFactoryRegistration(
                    context,
                    registrationCollector,
                    registration,
                    wellKnownTypes,
                    assumeFrameworkServicesRegisteredResolver,
                    semanticModelsByTree);

                continue;
            }

            if (registration.ImplementationType is not null)
            {
                var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(
                    registration.Location.SourceTree);
                AnalyzeConstructorRegistration(
                    context,
                    registrationCollector,
                    registration,
                    wellKnownTypes,
                    assumeFrameworkServicesRegistered);
            }
        }
    }

    private static void AnalyzeConstructorRegistration(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        ServiceRegistration registration,
        WellKnownTypes? wellKnownTypes,
        bool assumeFrameworkServicesRegistered)
    {
        if (!IsServiceImplementationCompatible(registration.ServiceType, registration.ImplementationType!))
        {
            // DI013 handles incompatible service/implementation pairs separately.
            return;
        }

        var bestMissingDependencies = GetBestMissingDependenciesForType(
            registration.ImplementationType!,
            registrationCollector,
            wellKnownTypes,
            assumeFrameworkServicesRegistered);
        if (bestMissingDependencies is null)
        {
            return;
        }

        foreach (var missingDependency in bestMissingDependencies)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UnresolvableDependency,
                registration.Location,
                registration.ServiceType.Name,
                FormatDependencyName(missingDependency.Type, missingDependency.Key, missingDependency.IsKeyed));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        ServiceRegistration registration,
        WellKnownTypes? wellKnownTypes,
        Func<SyntaxTree?, bool> assumeFrameworkServicesRegisteredResolver,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        if (!semanticModelsByTree.TryGetValue(registration.FactoryExpression!.SyntaxTree, out var semanticModel))
        {
            return;
        }

        var invocations = GetFactoryInvocations(registration.FactoryExpression, semanticModel);

        foreach (var invocation in invocations)
        {
            var assumeFrameworkServicesRegistered = assumeFrameworkServicesRegisteredResolver(invocation.SyntaxTree);

            if (!semanticModelsByTree.TryGetValue(invocation.SyntaxTree, out var invocationSemanticModel))
            {
                continue;
            }

            if (!TryGetRequiredResolutionInfo(
                    invocation,
                    invocationSemanticModel,
                    wellKnownTypes,
                    out var dependencyType,
                    out var key,
                    out var isKeyed))
            {
                if (!TryGetActivatorUtilitiesMissingDependencies(
                        invocation,
                        invocationSemanticModel,
                        registrationCollector,
                        wellKnownTypes,
                        assumeFrameworkServicesRegistered,
                        out var missingDependencies))
                {
                    continue;
                }

                foreach (var missingDependency in missingDependencies)
                {
                    var activatorDiagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.UnresolvableDependency,
                        invocation.GetLocation(),
                        registration.ServiceType.Name,
                        FormatDependencyName(missingDependency.Type, missingDependency.Key, missingDependency.IsKeyed));

                    context.ReportDiagnostic(activatorDiagnostic);
                }

                continue;
            }

            if (ShouldSkipDependencyCheck(
                    dependencyType,
                    parameter: null,
                    wellKnownTypes,
                    assumeFrameworkServicesRegistered))
            {
                continue;
            }

            if (IsDependencyRegistered(dependencyType, key, isKeyed, registrationCollector))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UnresolvableDependency,
                invocation.GetLocation(),
                registration.ServiceType.Name,
                FormatDependencyName(dependencyType, key, isKeyed));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static List<(ITypeSymbol Type, object? Key, bool IsKeyed)>? GetBestMissingDependenciesForType(
        INamedTypeSymbol implementationType,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        bool assumeFrameworkServicesRegistered)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType).ToArray();
        if (constructors.Length == 0)
        {
            return null;
        }

        List<(ITypeSymbol Type, object? Key, bool IsKeyed)>? bestMissingDependencies = null;
        var bestMissingCount = int.MaxValue;
        var bestParameterCount = -1;

        foreach (var constructor in constructors)
        {
            var missingDependencies = new List<(ITypeSymbol Type, object? Key, bool IsKeyed)>();

            foreach (var parameter in constructor.Parameters)
            {
                if (ShouldSkipDependencyCheck(
                        parameter.Type,
                        parameter,
                        wellKnownTypes,
                        assumeFrameworkServicesRegistered))
                {
                    continue;
                }

                var (key, isKeyed) = GetServiceKey(parameter);
                if (IsDependencyRegistered(parameter.Type, key, isKeyed, registrationCollector))
                {
                    continue;
                }

                missingDependencies.Add((parameter.Type, key, isKeyed));
            }

            // If any constructor is fully resolvable, DI can activate the service.
            if (missingDependencies.Count == 0)
            {
                return null;
            }

            if (missingDependencies.Count < bestMissingCount ||
                (missingDependencies.Count == bestMissingCount &&
                 constructor.Parameters.Length > bestParameterCount))
            {
                bestMissingDependencies = missingDependencies;
                bestMissingCount = missingDependencies.Count;
                bestParameterCount = constructor.Parameters.Length;
            }
        }

        return bestMissingDependencies;
    }

    private static bool TryGetActivatorUtilitiesMissingDependencies(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        bool assumeFrameworkServicesRegistered,
        out List<(ITypeSymbol Type, object? Key, bool IsKeyed)> missingDependencies)
    {
        missingDependencies = null!;

        if (!TryGetActivatorUtilitiesImplementationType(
                invocation,
                semanticModel,
                out var implementationType,
                out var hasExplicitConstructorArguments))
        {
            return false;
        }

        if (hasExplicitConstructorArguments)
        {
            return false;
        }

        var bestMissingDependencies = GetBestMissingDependenciesForType(
            implementationType,
            registrationCollector,
            wellKnownTypes,
            assumeFrameworkServicesRegistered);
        if (bestMissingDependencies is null || bestMissingDependencies.Count == 0)
        {
            return false;
        }

        missingDependencies = bestMissingDependencies;
        return true;
    }

    private static bool TryGetActivatorUtilitiesImplementationType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out INamedTypeSymbol implementationType,
        out bool hasExplicitConstructorArguments)
    {
        implementationType = null!;
        hasExplicitConstructorArguments = false;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (sourceMethod.Name != "CreateInstance" ||
            sourceMethod.ContainingType?.Name != "ActivatorUtilities" ||
            sourceMethod.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            if (methodSymbol.TypeArguments[0] is not INamedTypeSymbol namedType)
            {
                return false;
            }

            implementationType = namedType;
            hasExplicitConstructorArguments = invocation.ArgumentList.Arguments.Count > 1;
            return true;
        }

        var instanceTypeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "instanceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");
        if (instanceTypeExpression is null)
        {
            return false;
        }

        if (instanceTypeExpression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeOfExpression.Type);
        if (typeInfo.Type is not INamedTypeSymbol nonGenericNamedType)
        {
            return false;
        }

        implementationType = nonGenericNamedType;
        hasExplicitConstructorArguments = invocation.ArgumentList.Arguments.Count > 2;
        return true;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetFactoryInvocations(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel)
    {
        var unwrappedFactoryExpression = UnwrapFactoryExpression(factoryExpression);

        if (unwrappedFactoryExpression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            foreach (var invocation in unwrappedFactoryExpression.DescendantNodesAndSelf()
                         .OfType<InvocationExpressionSyntax>())
            {
                yield return invocation;
            }

            yield break;
        }

        if (!IsMethodGroupExpression(unwrappedFactoryExpression, semanticModel))
        {
            yield break;
        }

        if (!TryGetFactoryMethodBodyNode(unwrappedFactoryExpression, semanticModel, out var bodyNode))
        {
            yield break;
        }

        foreach (var invocation in bodyNode.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            yield return invocation;
        }
    }

    private static ExpressionSyntax UnwrapFactoryExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    expression = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsMethodGroupExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        if (symbolInfo.Symbol is IMethodSymbol)
        {
            return true;
        }

        return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Any();
    }

    private static bool TryGetFactoryMethodBodyNode(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out SyntaxNode bodyNode)
    {
        bodyNode = null!;

        var symbolInfo = semanticModel.GetSymbolInfo(factoryExpression);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol is null)
        {
            return false;
        }

        var declaration = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        switch (declaration)
        {
            case MethodDeclarationSyntax methodDeclaration:
                var methodBody = (SyntaxNode?)methodDeclaration.Body ?? methodDeclaration.ExpressionBody?.Expression;
                if (methodBody is null)
                {
                    return false;
                }

                bodyNode = methodBody;
                return true;
            case LocalFunctionStatementSyntax localFunction:
                var localFunctionBody = (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression;
                if (localFunctionBody is null)
                {
                    return false;
                }

                bodyNode = localFunctionBody;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetRequiredResolutionInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes? wellKnownTypes,
        out ITypeSymbol dependencyType,
        out object? key,
        out bool isKeyed)
    {
        dependencyType = null!;
        key = null;
        isKeyed = false;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!IsRequiredResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return false;
        }

        if (!TryGetResolvedDependencyType(invocation, methodSymbol, semanticModel, out dependencyType))
        {
            return false;
        }

        isKeyed = IsKeyedRequiredResolutionMethod(methodSymbol);
        if (!isKeyed)
        {
            return true;
        }

        return TryExtractKeyFromResolution(invocation, methodSymbol, semanticModel, out key);
    }

    private static bool IsRequiredResolutionMethod(
        IMethodSymbol methodSymbol,
        WellKnownTypes? wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (sourceMethod.Name is not ("GetRequiredService" or "GetRequiredKeyedService"))
        {
            return false;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            if (wellKnownTypes?.IServiceProvider is not null &&
                SymbolEqualityComparer.Default.Equals(receiverType, wellKnownTypes.IServiceProvider))
            {
                return true;
            }

            if (wellKnownTypes?.IKeyedServiceProvider is not null &&
                SymbolEqualityComparer.Default.Equals(receiverType, wellKnownTypes.IKeyedServiceProvider))
            {
                return true;
            }

            // Fallback for reduced test stubs when well-known symbols are unavailable.
            if (wellKnownTypes is null &&
                receiverType.Name is "IServiceProvider" or "IKeyedServiceProvider")
            {
                return true;
            }
        }

        if (wellKnownTypes?.IKeyedServiceProvider is not null &&
            SymbolEqualityComparer.Default.Equals(sourceMethod.ContainingType, wellKnownTypes.IKeyedServiceProvider) &&
            sourceMethod.Name == "GetRequiredKeyedService")
        {
            return true;
        }

        if (wellKnownTypes is null &&
            sourceMethod.ContainingType?.Name == "IKeyedServiceProvider" &&
            sourceMethod.Name == "GetRequiredKeyedService")
        {
            return true;
        }

        return false;
    }

    private static bool IsKeyedRequiredResolutionMethod(IMethodSymbol methodSymbol)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        return sourceMethod.Name == "GetRequiredKeyedService";
    }

    private static bool TryGetResolvedDependencyType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out ITypeSymbol dependencyType)
    {
        dependencyType = null!;

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            dependencyType = methodSymbol.TypeArguments[0];
            return true;
        }

        var typeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");

        if (typeExpression is null && invocation.ArgumentList.Arguments.Count > 0)
        {
            typeExpression = invocation.ArgumentList.Arguments[0].Expression;
        }

        if (typeExpression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeOfExpression.Type);
        if (typeInfo.Type is null)
        {
            return false;
        }

        dependencyType = typeInfo.Type;
        return true;
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

            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static bool TryExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out object? key)
    {
        key = null;

        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            return false;
        }

        var constantValue = semanticModel.GetConstantValue(keyExpression);
        if (!constantValue.HasValue)
        {
            return false;
        }

        key = constantValue.Value;
        return true;
    }

    private static bool ShouldSkipDependencyCheck(
        ITypeSymbol dependencyType,
        IParameterSymbol? parameter,
        WellKnownTypes? wellKnownTypes,
        bool assumeFrameworkServicesRegistered)
    {
        if (parameter?.HasExplicitDefaultValue == true)
        {
            return true;
        }

        if (wellKnownTypes is not null &&
            wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(dependencyType))
        {
            return true;
        }

        if (IsContainerProvidedDependency(dependencyType))
        {
            return true;
        }

        return assumeFrameworkServicesRegistered && IsFrameworkProvidedDependency(dependencyType);
    }

    private static bool IsContainerProvidedDependency(ITypeSymbol dependencyType)
    {
        if (dependencyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        if (namedType.Name == "IEnumerable" &&
            namespaceName == "System.Collections.Generic" &&
            namedType.IsGenericType)
        {
            return true;
        }

        return false;
    }

    private static bool IsFrameworkProvidedDependency(ITypeSymbol dependencyType)
    {
        if (dependencyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();

        if (namedType.Name == "IConfiguration" &&
            namespaceName == "Microsoft.Extensions.Configuration")
        {
            return true;
        }

        if (namedType.Name == "ILoggerFactory" &&
            namespaceName == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.Name is "IHostEnvironment" or "IWebHostEnvironment" &&
            (namespaceName == "Microsoft.Extensions.Hosting" ||
             namespaceName == "Microsoft.AspNetCore.Hosting"))
        {
            return true;
        }

        if (namedType.Name == "ILogger" &&
            namespaceName == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.IsGenericType &&
            namedType.ConstructedFrom.Name == "ILogger" &&
            namedType.ConstructedFrom.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Logging")
        {
            return true;
        }

        if (namedType.Name is "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor" &&
            namespaceName == "Microsoft.Extensions.Options")
        {
            return true;
        }

        return false;
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

    private static bool IsDependencyRegistered(
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        RegistrationCollector registrationCollector)
    {
        if (registrationCollector.GetLifetime(dependencyType, key, isKeyed).HasValue)
        {
            return true;
        }

        if (dependencyType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.IsUnboundGenericType)
        {
            return false;
        }

        var openGenericType = namedType.ConstructUnboundGenericType();
        return registrationCollector.GetLifetime(openGenericType, key, isKeyed).HasValue;
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

    private static string FormatDependencyName(ITypeSymbol dependencyType, object? key, bool isKeyed)
    {
        var typeName = dependencyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (!isKeyed)
        {
            return typeName;
        }

        return $"{typeName} (key: {key ?? "null"})";
    }

    private static bool IsServiceImplementationCompatible(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType)
    {
        if (SymbolEqualityComparer.Default.Equals(serviceType, implementationType))
        {
            return true;
        }

        if (serviceType.IsUnboundGenericType)
        {
            if (!implementationType.IsGenericType)
            {
                return false;
            }

            var originalService = serviceType.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(implementationType.OriginalDefinition, originalService))
            {
                return true;
            }

            foreach (var iface in implementationType.OriginalDefinition.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, originalService))
                {
                    return true;
                }
            }

            var currentBaseType = implementationType.OriginalDefinition.BaseType;
            while (currentBaseType is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(currentBaseType.OriginalDefinition, originalService))
                {
                    return true;
                }

                currentBaseType = currentBaseType.BaseType;
            }

            return false;
        }

        foreach (var iface in implementationType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, serviceType))
            {
                return true;
            }
        }

        var baseType = implementationType.BaseType;
        while (baseType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, serviceType))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }
}
