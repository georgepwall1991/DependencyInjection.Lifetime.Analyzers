using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects an ASP.NET Core framework-activated type requesting a service
/// the container cannot resolve.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI038_FrameworkActivatedDependencyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.FrameworkActivatedDependency);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(
                compilationContext.Compilation
            );
            if (registrationCollector is null)
            {
                return;
            }

            var controllerBase = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Mvc.ControllerBase"
            );
            var pageModel = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Mvc.RazorPages.PageModel"
            );
            var fromServices = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Mvc.FromServicesAttribute"
            );
            var fromKeyedServices = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"
            );

            if (
                controllerBase is null
                && pageModel is null
                && fromServices is null
                && fromKeyedServices is null
            )
            {
                return;
            }

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            var invocationObservations =
                new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();
            var sawCollectionInvocation = 0;
            var sawAlternateContainer = 0;
            var sawMvcActivation = 0;

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel
                    );

                    if (IsAlternateContainerConfiguration(invocation, syntaxContext.SemanticModel))
                    {
                        Interlocked.Exchange(ref sawAlternateContainer, 1);
                    }

                    if (IsMvcActivationMethod(GetInvokedName(invocation)))
                    {
                        Interlocked.Exchange(ref sawMvcActivation, 1);
                    }

                    if (
                        !ServiceCollectionReachabilityAnalyzer.IsPotentialServiceCollectionWrapperInvocation(
                            invocation,
                            syntaxContext.SemanticModel
                        )
                    )
                    {
                        return;
                    }

                    Interlocked.Exchange(ref sawCollectionInvocation, 1);
                    invocationObservations.Enqueue(
                        new ServiceCollectionReachabilityAnalyzer.InvocationObservation(
                            invocation,
                            syntaxContext.SemanticModel
                        )
                    );
                },
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
                AnalyzeCompilation(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    controllerBase,
                    pageModel,
                    fromServices,
                    fromKeyedServices,
                    invocationObservations,
                    sawCollectionInvocation,
                    sawAlternateContainer,
                    sawMvcActivation
                )
            );
        });
    }

    private static void AnalyzeCompilation(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes? wellKnownTypes,
        INamedTypeSymbol? controllerBase,
        INamedTypeSymbol? pageModel,
        INamedTypeSymbol? fromServices,
        INamedTypeSymbol? fromKeyedServices,
        ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation> invocationObservations,
        int sawCollectionInvocation,
        int sawAlternateContainer,
        int sawMvcActivation
    )
    {
        if (sawCollectionInvocation == 0 || sawAlternateContainer != 0 || sawMvcActivation == 0)
        {
            return;
        }

        var observations = invocationObservations.ToImmutableArray();
        if (HasUnexpandedOpaqueWrapper(observations))
        {
            return;
        }

        var registrations = registrationCollector.AllRegistrations.ToList();
        var registrationCandidates = registrationCollector.RegistrationCandidates.ToList();
        var definitelyRemovedRegistrations = DefinitelyRemovedRegistrationSet.Create(
            context.Compilation,
            registrations,
            registrationCandidates,
            registrationCollector.OrderedMutations
        );
        var effectiveRegistrations = definitelyRemovedRegistrations.GetEffectiveRegistrations(
            registrations,
            registrationCandidates
        );
        var reachabilityAnalyzer = ServiceCollectionReachabilityAnalyzer.Create(
            context.Compilation,
            observations,
            registrations
        );
        var availableRegistrations = effectiveRegistrations
            .Where(registration => reachabilityAnalyzer.IsReachable(registration.Location))
            .ToList();
        var resolutionEngine = new DependencyResolutionEngine(
            registrationCollector,
            wellKnownTypes,
            availableRegistrations: availableRegistrations
        );
        var registeredTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var registration in availableRegistrations)
        {
            if (registration.ImplementationType is { } implementationType)
            {
                registeredTypes.Add(implementationType);
            }

            if (registration.ServiceType is { } serviceType)
            {
                registeredTypes.Add(serviceType);
            }
        }

        var reported = new HashSet<ReportedFinding>();
        var hasCustomControllerActivator =
            HasCustomActivator(
                availableRegistrations,
                "IControllerActivator",
                "Microsoft.AspNetCore.Mvc.Controllers"
            )
            || HasCustomActivator(
                availableRegistrations,
                "IControllerFactory",
                "Microsoft.AspNetCore.Mvc.Controllers"
            );
        var hasCustomPageActivator =
            HasCustomActivator(
                availableRegistrations,
                "IPageModelActivatorProvider",
                "Microsoft.AspNetCore.Mvc.RazorPages"
            )
            || HasCustomActivator(
                availableRegistrations,
                "IPageModelFactoryProvider",
                "Microsoft.AspNetCore.Mvc.RazorPages"
            );

        foreach (var type in GetAllNamedTypes(context.Compilation.Assembly.GlobalNamespace))
        {
            if (!IsDiscoverableActivatedType(type, controllerBase, pageModel))
            {
                continue;
            }

            var skipConstructor =
                (IsAssignableTo(type, controllerBase) && hasCustomControllerActivator)
                || (IsAssignableTo(type, pageModel) && hasCustomPageActivator);
            if (!registeredTypes.Contains(type) && !skipConstructor)
            {
                ReportActivatedTypeDependencies(context, resolutionEngine, type, reported);
            }

            ReportAttributedParameters(
                context,
                resolutionEngine,
                type,
                controllerBase,
                pageModel,
                fromServices,
                fromKeyedServices,
                reported
            );
        }
    }

    private static void ReportActivatedTypeDependencies(
        CompilationAnalysisContext context,
        DependencyResolutionEngine resolutionEngine,
        INamedTypeSymbol type,
        HashSet<ReportedFinding> reported
    )
    {
        var resolutionResult = resolutionEngine.ResolveActivatedImplementation(
            type,
            assumeFrameworkServicesRegistered: true
        );
        if (
            resolutionResult.IsResolvable
            || resolutionResult.Confidence != ResolutionConfidence.High
            || resolutionResult.MissingDependencies.IsDefaultOrEmpty
        )
        {
            return;
        }

        foreach (var missingDependency in resolutionResult.MissingDependencies)
        {
            if (IsFrameworkOwnedService(missingDependency.Type))
            {
                continue;
            }

            var location =
                FindConstructorParameterLocation(type, missingDependency)
                ?? type.Locations.FirstOrDefault();
            if (location is null || !location.IsInSource)
            {
                continue;
            }

            Report(
                context,
                location,
                type.Name,
                DependencyResolutionEngine.FormatDependencyName(
                    missingDependency.Type,
                    missingDependency.Key,
                    missingDependency.IsKeyed
                ),
                reported
            );
        }
    }

    private static void ReportAttributedParameters(
        CompilationAnalysisContext context,
        DependencyResolutionEngine resolutionEngine,
        INamedTypeSymbol type,
        INamedTypeSymbol? controllerBase,
        INamedTypeSymbol? pageModel,
        INamedTypeSymbol? fromServices,
        INamedTypeSymbol? fromKeyedServices,
        HashSet<ReportedFinding> reported
    )
    {
        foreach (var method in GetPublicInstanceActions(type, controllerBase, pageModel))
        {
            foreach (var parameter in method.Parameters)
            {
                if (
                    !TryGetAttributedServiceRequest(
                        parameter,
                        fromServices,
                        fromKeyedServices,
                        out var request
                    )
                )
                {
                    continue;
                }

                if (request.IsUnknown || IsOptionalServiceParameter(parameter))
                {
                    continue;
                }

                var resolutionResult = resolutionEngine.ResolveServiceRequest(
                    parameter.Type,
                    request.Key,
                    request.IsKeyed,
                    assumeFrameworkServicesRegistered: true
                );
                if (
                    resolutionResult.IsResolvable
                    || resolutionResult.Confidence != ResolutionConfidence.High
                    || resolutionResult.MissingDependencies.IsDefaultOrEmpty
                )
                {
                    continue;
                }

                var missing = resolutionResult.MissingDependencies[0];
                if (IsFrameworkOwnedService(missing.Type))
                {
                    continue;
                }

                var location = GetParameterLocation(parameter);
                if (location is null || !location.IsInSource)
                {
                    location = type.Locations.FirstOrDefault();
                }

                if (location is null || !location.IsInSource)
                {
                    continue;
                }

                Report(
                    context,
                    location,
                    type.Name,
                    DependencyResolutionEngine.FormatDependencyName(
                        missing.Type,
                        missing.Key,
                        missing.IsKeyed
                    ),
                    reported
                );
            }
        }
    }

    private static void Report(
        CompilationAnalysisContext context,
        Location location,
        string activatedTypeName,
        string missingDependencyName,
        HashSet<ReportedFinding> reported
    )
    {
        var finding = new ReportedFinding(
            location.SourceTree?.FilePath,
            location.SourceSpan.Start,
            location.SourceSpan.End,
            activatedTypeName,
            missingDependencyName
        );
        if (!reported.Add(finding))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.FrameworkActivatedDependency,
                location,
                activatedTypeName,
                missingDependencyName
            )
        );
    }

    private static bool TryGetAttributedServiceRequest(
        IParameterSymbol parameter,
        INamedTypeSymbol? fromServices,
        INamedTypeSymbol? fromKeyedServices,
        out KeyedServiceHelpers.ServiceKeyRequest request
    )
    {
        var keyedRequest = KeyedServiceHelpers.GetServiceKey(
            parameter,
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );
        if (keyedRequest.IsKeyed)
        {
            request = keyedRequest;
            return true;
        }

        request = default;
        if (!HasFromServicesAttribute(parameter, fromServices, fromKeyedServices))
        {
            return false;
        }

        request = new KeyedServiceHelpers.ServiceKeyRequest(
            key: null,
            isKeyed: false,
            isUnknown: false,
            keyLiteral: null
        );
        return true;
    }

    private static bool HasFromServicesAttribute(
        IParameterSymbol parameter,
        INamedTypeSymbol? fromServices,
        INamedTypeSymbol? fromKeyedServices
    )
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                continue;
            }

            if (
                fromServices is not null
                && SymbolEqualityComparer.Default.Equals(attributeClass, fromServices)
            )
            {
                return true;
            }

            if (
                fromKeyedServices is not null
                && SymbolEqualityComparer.Default.Equals(attributeClass, fromKeyedServices)
            )
            {
                return true;
            }

            if (attributeClass.Name != "FromServicesAttribute")
            {
                continue;
            }

            var containingNamespace = attributeClass.ContainingNamespace?.ToDisplayString();
            if (
                containingNamespace is not null
                && containingNamespace.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static Location? GetParameterLocation(IParameterSymbol parameter)
    {
        foreach (var syntaxReference in parameter.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is ParameterSyntax parameterSyntax)
            {
                if (parameterSyntax.Type is { } typeSyntax)
                {
                    return Location.Create(
                        parameterSyntax.SyntaxTree,
                        Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                            typeSyntax.SpanStart,
                            parameterSyntax.Span.End
                        )
                    );
                }

                return parameterSyntax.GetLocation();
            }
        }

        return parameter.Locations.FirstOrDefault();
    }

    private static Location? FindConstructorParameterLocation(
        INamedTypeSymbol type,
        MissingDependency missingDependency
    )
    {
        foreach (var constructor in ConstructorSelection.GetConstructorsToAnalyze(type))
        {
            foreach (var parameter in constructor.Parameters)
            {
                var request = KeyedServiceHelpers.GetServiceKey(
                    parameter,
                    inheritedKey: null,
                    hasInheritedKey: false,
                    inheritedKeyLiteral: null
                );
                if (
                    SymbolEqualityComparer.Default.Equals(parameter.Type, missingDependency.Type)
                    && request.IsKeyed == missingDependency.IsKeyed
                    && Equals(request.Key, missingDependency.Key)
                )
                {
                    return GetParameterLocation(parameter);
                }
            }
        }

        return null;
    }

    private static bool IsFrameworkOwnedService(ITypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        if (namespaceName is null || IsExplicitFrameworkRegistrationRequired(type, namespaceName))
        {
            return false;
        }

        return IsFrameworkOwnedNamespace(namespaceName, "Microsoft.AspNetCore")
            || IsFrameworkOwnedNamespace(namespaceName, "Microsoft.Extensions");
    }

    private static bool IsFrameworkOwnedNamespace(string namespaceName, string root) =>
        namespaceName == root || namespaceName.StartsWith(root + ".", StringComparison.Ordinal);

    private static bool IsExplicitFrameworkRegistrationRequired(
        ITypeSymbol type,
        string namespaceName
    ) =>
        (type.Name == "IHttpClientFactory" && namespaceName == "System.Net.Http")
        || (type.Name == "IMemoryCache" && namespaceName == "Microsoft.Extensions.Caching.Memory")
        || (type.Name == "IHttpContextAccessor" && namespaceName == "Microsoft.AspNetCore.Http");

    private static bool HasUnexpandedOpaqueWrapper(
        ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation> observations
    )
    {
        foreach (var observation in observations)
        {
            if (
                !ServiceCollectionReachabilityAnalyzer.TryGetInvocationTarget(
                    observation.Invocation,
                    observation.SemanticModel,
                    out var targetMethod
                )
            )
            {
                if (IsRegistrationMethodName(GetInvokedName(observation.Invocation)))
                {
                    return true;
                }

                continue;
            }

            var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
            if (
                !IsServiceCollectionRegistrationMethod(originalMethod)
                || IsFrameworkOwnedRegistrationExtension(originalMethod)
            )
            {
                continue;
            }

            if (
                originalMethod.DeclaringSyntaxReferences.Length == 0
                || IsGeneratedRegistrationMethod(originalMethod)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServiceCollectionRegistrationMethod(IMethodSymbol method)
    {
        if (ServiceCollectionReachabilityAnalyzer.IsCustomServiceCollectionExtensionByName(method))
        {
            return true;
        }

        return method.IsStatic
            && method.Parameters.Length > 0
            && ServiceCollectionReachabilityAnalyzer.IsServiceCollectionTypeByName(
                method.Parameters[0].Type
            );
    }

    private static bool IsOptionalServiceParameter(IParameterSymbol parameter) =>
        parameter.HasExplicitDefaultValue
        || parameter.NullableAnnotation == NullableAnnotation.Annotated;

    private static bool IsMvcActivationMethod(string? name) =>
        name
            is "AddControllers"
                or "AddControllersWithViews"
                or "AddMvc"
                or "AddMvcCore"
                or "AddRazorPages"
                or "MapControllers"
                or "MapRazorPages";

    private static bool IsAlternateContainerConfiguration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        if (
            !ServiceCollectionReachabilityAnalyzer.TryGetInvocationTarget(
                invocation,
                semanticModel,
                out var targetMethod
            )
        )
        {
            return false;
        }

        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        if (originalMethod.Name is not ("UseServiceProviderFactory" or "ConfigureContainer"))
        {
            return false;
        }

        var namespaceName = originalMethod.ContainingType?.ContainingNamespace?.ToDisplayString();
        return namespaceName
            is "Microsoft.Extensions.Hosting"
                or "Microsoft.AspNetCore.Builder"
                or "Microsoft.AspNetCore.Hosting";
    }

    private static bool HasCustomActivator(
        List<ServiceRegistration> availableRegistrations,
        string serviceName,
        string namespaceName
    )
    {
        foreach (var registration in availableRegistrations)
        {
            var serviceType = registration.ServiceType;
            if (
                !registration.IsKeyed
                && serviceType.Name == serviceName
                && serviceType.ContainingNamespace?.ToDisplayString() == namespaceName
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRegistrationMethodName(string? name) =>
        name is not null
        && (
            name.StartsWith("Add", StringComparison.Ordinal)
            || name.StartsWith("TryAdd", StringComparison.Ordinal)
            || name.StartsWith("Register", StringComparison.Ordinal)
            || name is "Configure" or "Replace" or "Insert" or "Scan"
        );

    private static bool IsFrameworkOwnedRegistrationExtension(IMethodSymbol method)
    {
        var assemblyName = method.ContainingAssembly?.Name;
        return assemblyName is not null
            && (
                assemblyName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || assemblyName.Equals(
                    "Microsoft.Extensions.DependencyInjection",
                    StringComparison.Ordinal
                )
            );
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

    private static bool IsDiscoverableActivatedType(
        INamedTypeSymbol type,
        INamedTypeSymbol? controllerBase,
        INamedTypeSymbol? pageModel
    ) =>
        IsConcreteClass(type)
        && type.ContainingType is null
        && type.DeclaredAccessibility == Accessibility.Public
        && !type.IsGenericType
        && !HasInheritedMvcAttribute(type, "NonControllerAttribute")
        && IsFrameworkActivatedType(type, controllerBase, pageModel);

    private static bool IsFrameworkActivatedType(
        INamedTypeSymbol type,
        INamedTypeSymbol? controllerBase,
        INamedTypeSymbol? pageModel
    ) => IsAssignableTo(type, controllerBase) || IsAssignableTo(type, pageModel);

    private static IEnumerable<IMethodSymbol> GetPublicInstanceActions(
        INamedTypeSymbol type,
        INamedTypeSymbol? controllerBase,
        INamedTypeSymbol? pageModel
    )
    {
        var seenSlots = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                current.SpecialType == SpecialType.System_Object
                || SymbolEqualityComparer.Default.Equals(current, controllerBase)
                || SymbolEqualityComparer.Default.Equals(current, pageModel)
            )
            {
                yield break;
            }

            var isPageModelType = IsAssignableTo(current, pageModel);
            foreach (var member in current.GetMembers())
            {
                if (
                    member is not IMethodSymbol method
                    || method.MethodKind != MethodKind.Ordinary
                    || method.DeclaredAccessibility != Accessibility.Public
                    || method.IsStatic
                    || method.IsGenericMethod
                    || method.IsAbstract
                )
                {
                    continue;
                }

                if (!seenSlots.Add(GetOverrideSlot(method)))
                {
                    continue;
                }

                if (IsObjectMethodOverride(method))
                {
                    continue;
                }

                if (
                    isPageModelType
                        ? HasInheritedMvcMethodAttribute(method, "NonHandlerAttribute")
                        : HasInheritedNonAction(method)
                )
                {
                    continue;
                }

                if (isPageModelType && !IsRazorPageHandler(method.Name))
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    private static IMethodSymbol GetOverrideSlot(IMethodSymbol method)
    {
        var current = method;
        while (current.OverriddenMethod is not null)
        {
            current = current.OverriddenMethod;
        }

        return current.OriginalDefinition;
    }

    private static bool IsObjectMethodOverride(IMethodSymbol method) =>
        GetOverrideSlot(method).ContainingType?.SpecialType == SpecialType.System_Object;

    private static bool IsPubliclyAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasInheritedNonAction(IMethodSymbol method) =>
        HasInheritedMvcMethodAttribute(method, "NonActionAttribute");

    private static bool HasInheritedMvcMethodAttribute(IMethodSymbol method, string attributeName)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            if (HasMvcAttribute(current, attributeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGeneratedRegistrationMethod(IMethodSymbol method)
    {
        if (HasGeneratedCodeAttribute(method) || HasGeneratedCodeAttribute(method.ContainingType))
        {
            return true;
        }

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var filePath = syntaxReference.SyntaxTree.FilePath;
            if (
                filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGeneratedCodeAttribute(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "GeneratedCodeAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInheritedMvcAttribute(INamedTypeSymbol type, string attributeName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (HasMvcAttribute(current, attributeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRazorPageHandler(string methodName)
    {
        if (!methodName.StartsWith("On", StringComparison.Ordinal) || methodName.Length <= 2)
        {
            return false;
        }

        var name = methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(2, methodName.Length - 7)
            : methodName.Substring(2);
        return name.Length > 0 && char.IsUpper(name[0]);
    }

    private static bool HasMvcAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || attributeClass.Name != attributeName)
            {
                continue;
            }

            var containingNamespace = attributeClass.ContainingNamespace?.ToDisplayString();
            if (
                containingNamespace is not null
                && containingNamespace.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignableTo(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConcreteClass(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsUnboundGenericType
        && type.TypeKind != TypeKind.Error;

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var nested in GetTypeAndNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetAllNamedTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypeAndNestedTypes(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var child in GetTypeAndNestedTypes(nestedType))
            {
                yield return child;
            }
        }
    }

    private readonly struct ReportedFinding : IEquatable<ReportedFinding>
    {
        public ReportedFinding(
            string? filePath,
            int start,
            int end,
            string activatedTypeName,
            string missingDependencyName
        )
        {
            FilePath = filePath;
            Start = start;
            End = end;
            ActivatedTypeName = activatedTypeName;
            MissingDependencyName = missingDependencyName;
        }

        public string? FilePath { get; }

        public int Start { get; }

        public int End { get; }

        public string ActivatedTypeName { get; }

        public string MissingDependencyName { get; }

        public bool Equals(ReportedFinding other) =>
            Start == other.Start
            && End == other.End
            && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && string.Equals(ActivatedTypeName, other.ActivatedTypeName, StringComparison.Ordinal)
            && string.Equals(
                MissingDependencyName,
                other.MissingDependencyName,
                StringComparison.Ordinal
            );

        public override bool Equals(object? obj) => obj is ReportedFinding other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FilePath is null ? 0 : StringComparer.Ordinal.GetHashCode(FilePath);
                hashCode = (hashCode * 397) ^ Start;
                hashCode = (hashCode * 397) ^ End;
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ActivatedTypeName);
                hashCode =
                    (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(MissingDependencyName);
                return hashCode;
            }
        }
    }
}
