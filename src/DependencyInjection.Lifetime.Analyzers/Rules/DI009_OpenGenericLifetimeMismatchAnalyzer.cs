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
    private static readonly object UnknownConcreteKey = new();

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
        var knownLifetimeClassifier = new KnownServiceLifetimeClassifier(wellKnownTypes);
        var registrations = registrationCollector.AllRegistrations.ToList();
        var registrationCandidates = registrationCollector.RegistrationCandidates.ToList();
        var definitelyRemovedRegistrations = DefinitelyRemovedRegistrationSet.Create(
            context.Compilation,
            registrations,
            registrationCandidates,
            registrationCollector.OrderedMutations);
        var effectiveRegistrations =
            definitelyRemovedRegistrations.GetEffectiveRegistrations(
                registrations,
                registrationCandidates);
        var resolutionEngine = new DependencyResolutionEngine(
            registrationCollector,
            wellKnownTypes,
            availableRegistrations: effectiveRegistrations);

        foreach (var registration in effectiveRegistrations)
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
            var captiveDependencies =
                new Dictionary<string, (INamedTypeSymbol DependencyType, ServiceLifetime Lifetime)>();

            foreach (var activationKey in GetPotentialActivationKeys(
                         registration,
                         effectiveRegistrations))
            {
                var constructors = ConstructorSelection.GetLikelyActivationConstructors(
                        genericDefinition,
                        parameter => IsResolvableConstructorParameter(
                            parameter,
                            resolutionEngine,
                            activationKey,
                            registration.IsKeyed))
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
                                GetServiceKey(
                                    parameter,
                                    activationKey,
                                    registration.IsKeyed),
                                effectiveRegistrations,
                                knownLifetimeClassifier,
                                context.Compilation,
                                out var dependencyType,
                                out var dependencyLifetime) ||
                            dependencyLifetime is not { } dependencyLifetimeValue)
                        {
                            continue;
                        }

                        // Check for captive dependency: singleton capturing scoped or transient
                        if (dependencyLifetimeValue <= ServiceLifetime.Singleton)
                        {
                            continue;
                        }

                        var reportKey =
                            $"{constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}|" +
                            $"{parameter.Ordinal}";
                        if (captiveDependencies.TryGetValue(reportKey, out var existingDependency) &&
                            existingDependency.Lifetime >= dependencyLifetimeValue)
                        {
                            continue;
                        }

                        captiveDependencies[reportKey] = (dependencyType, dependencyLifetimeValue);
                    }
                }
            }

            foreach (var captiveDependency in captiveDependencies.Values)
            {
                var lifetimeName = captiveDependency.Lifetime.ToString().ToLowerInvariant();
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericLifetimeMismatch,
                    registration.Location,
                    ImmutableDictionary<string, string?>.Empty.Add(DependencyLifetimePropertyName, lifetimeName),
                    registration.ImplementationType.Name,
                    lifetimeName,
                    captiveDependency.DependencyType.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static IEnumerable<object?> GetPotentialActivationKeys(
        ServiceRegistration registration,
        IReadOnlyList<ServiceRegistration> registrations)
    {
        if (!registration.IsKeyed ||
            !SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key))
        {
            yield return registration.Key;
            yield break;
        }

        var matchingAnyKeyRegistrations = registrations
            .Where(candidate =>
                candidate.IsKeyed &&
                SyntaxValueHelpers.IsKeyedServiceAnyKey(candidate.Key) &&
                SymbolEqualityComparer.Default.Equals(
                    candidate.ServiceType,
                    registration.ServiceType))
            .ToList();
        if (!ReferenceEquals(
                registration,
                DependencyResolutionEngine.SelectEffectiveSingleServiceRegistration(
                    matchingAnyKeyRegistrations)))
        {
            yield break;
        }

        foreach (var concreteKey in registrations
                     .Where(static candidate =>
                         candidate.IsKeyed &&
                         !SyntaxValueHelpers.IsKeyedServiceAnyKey(candidate.Key))
                     .Select(static candidate => candidate.Key)
                     .Distinct())
        {
            if (registrations.Any(candidate =>
                    candidate.IsKeyed &&
                    Equals(candidate.Key, concreteKey) &&
                    SymbolEqualityComparer.Default.Equals(
                        candidate.ServiceType,
                        registration.ServiceType)))
            {
                continue;
            }

            yield return concreteKey;
        }

        // Preserve the unknown-concrete-key fallback case without reusing AnyKey,
        // which represents aggregation across known concrete keys for enumerable
        // lifetime analysis. Single-service requests with this sentinel still
        // fall back to AnyKey because no exact registration can match it.
        yield return UnknownConcreteKey;
    }

    private static bool IsResolvableConstructorParameter(
        IParameterSymbol parameter,
        DependencyResolutionEngine resolutionEngine,
        object? inheritedKey,
        bool inheritedIsKeyed)
    {
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        var (key, isKeyed) = GetServiceKey(
            parameter,
            inheritedKey,
            inheritedIsKeyed);
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
        IReadOnlyList<ServiceRegistration> effectiveRegistrations,
        KnownServiceLifetimeClassifier knownLifetimeClassifier,
        Compilation compilation,
        out INamedTypeSymbol dependencyType,
        out ServiceLifetime? dependencyLifetime)
    {
        dependencyType = null!;
        dependencyLifetime = null;

        if (TryGetEnumerableElementType(parameterType, out var elementType))
        {
            dependencyType = elementType;
            var userElementLifetime = GetWorstEffectiveLifetime(
                effectiveRegistrations,
                elementType,
                serviceKey.key,
                serviceKey.isKeyed,
                compilation);
            var knownElementLifetime = knownLifetimeClassifier.TryGetLifetime(elementType, serviceKey.isKeyed, out var classifierLifetime)
                ? (ServiceLifetime?)classifierLifetime
                : null;

            // IEnumerable<T> resolution concatenates every matching registration,
            // so the captive risk is the worst (shortest-lived) among them. An
            // explicit closed singleton does not hide an open-generic framework
            // scoped element that the container still includes.
            dependencyLifetime = WorstLifetime(userElementLifetime, knownElementLifetime);
            return dependencyLifetime is not null;
        }

        var nonGenericType = GetNonGenericTypeFromParameter(parameterType);
        if (nonGenericType is null)
        {
            return false;
        }

        dependencyType = nonGenericType;

        // Closed-generic user registration wins over the open-generic lookup and the
        // known-framework classifier, so an explicit
        // `services.AddSingleton<IOptionsSnapshot<MyOptions>, MySnapshot>()`
        // is respected instead of being overridden by the framework default.
        if (parameterType is INamedTypeSymbol closedNamedType &&
            closedNamedType.IsGenericType &&
            !closedNamedType.IsUnboundGenericType &&
            !SymbolEqualityComparer.Default.Equals(closedNamedType, nonGenericType))
        {
            var closedLifetime = GetEffectiveLifetime(
                effectiveRegistrations,
                closedNamedType,
                serviceKey.key,
                serviceKey.isKeyed);
            if (closedLifetime is not null)
            {
                dependencyLifetime = closedLifetime;
                return true;
            }
        }

        dependencyLifetime = GetEffectiveLifetime(
            effectiveRegistrations,
            nonGenericType,
            serviceKey.key,
            serviceKey.isKeyed);
        if (dependencyLifetime is null &&
            knownLifetimeClassifier.TryGetLifetime(parameterType, serviceKey.isKeyed, out var knownLifetime))
        {
            dependencyLifetime = knownLifetime;
        }
        return dependencyLifetime is not null;
    }

    private static ServiceLifetime? GetEffectiveLifetime(
        IReadOnlyList<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed)
    {
        if (isKeyed &&
            SyntaxValueHelpers.IsKeyedServiceAnyKey(key))
        {
            var possibleLifetime = registrations
                .Where(static registration =>
                    registration.IsKeyed &&
                    !SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key))
                .Select(static registration => registration.Key)
                .Distinct()
                .Select(candidateKey =>
                    GetEffectiveLifetimeForConcreteKey(
                        registrations,
                        serviceType,
                        candidateKey,
                        allowAnyKeyFallback: true))
                .Aggregate(
                    (ServiceLifetime?)null,
                    WorstLifetime);
            var anyKeyFallbackLifetime = GetEffectiveLifetimeForConcreteKey(
                registrations,
                serviceType,
                KeyedServiceAnyKey.Instance,
                allowAnyKeyFallback: false);
            return WorstLifetime(
                possibleLifetime,
                anyKeyFallbackLifetime);
        }

        return GetEffectiveLifetimeForConcreteKey(
            registrations,
            serviceType,
            key,
            allowAnyKeyFallback: isKeyed);
    }

    private static ServiceLifetime? GetEffectiveLifetimeForConcreteKey(
        IReadOnlyList<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool allowAnyKeyFallback)
    {
        var selectedLifetime = GetEffectiveLifetimeForServiceAndKey(
            registrations,
            serviceType,
            key,
            isKeyed: allowAnyKeyFallback ||
                SyntaxValueHelpers.IsKeyedServiceAnyKey(key));
        if (selectedLifetime is not null)
        {
            return selectedLifetime;
        }

        if (allowAnyKeyFallback)
        {
            selectedLifetime = GetEffectiveLifetimeForServiceAndKey(
                registrations,
                serviceType,
                KeyedServiceAnyKey.Instance,
                isKeyed: true);
            if (selectedLifetime is not null)
            {
                return selectedLifetime;
            }
        }

        if (serviceType is not INamedTypeSymbol closedGenericType ||
            !closedGenericType.IsGenericType ||
            closedGenericType.IsUnboundGenericType)
        {
            return null;
        }

        var openGenericType = closedGenericType.ConstructUnboundGenericType();
        selectedLifetime = GetEffectiveLifetimeForServiceAndKey(
            registrations,
            openGenericType,
            key,
            isKeyed: allowAnyKeyFallback ||
                SyntaxValueHelpers.IsKeyedServiceAnyKey(key));
        if (selectedLifetime is not null || !allowAnyKeyFallback)
        {
            return selectedLifetime;
        }

        return GetEffectiveLifetimeForServiceAndKey(
            registrations,
            openGenericType,
            KeyedServiceAnyKey.Instance,
            isKeyed: true);
    }

    private static ServiceLifetime? GetEffectiveLifetimeForServiceAndKey(
        IReadOnlyList<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed)
    {
        var matchingRegistrations = GetMatchingRegistrations(
                registrations,
                serviceType,
                key,
                isKeyed)
            .ToList();
        return DependencyResolutionEngine.SelectEffectiveSingleServiceRegistration(
                matchingRegistrations)
            ?.Lifetime;
    }

    private static ServiceLifetime? GetWorstEffectiveLifetime(
        IReadOnlyList<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed,
        Compilation compilation)
    {
        if (isKeyed &&
            SyntaxValueHelpers.IsKeyedServiceAnyKey(key))
        {
            return registrations
                .Where(static registration =>
                    registration.IsKeyed &&
                    !SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key))
                .Select(static registration => registration.Key)
                .Distinct()
                .Select(candidateKey =>
                    GetWorstEffectiveLifetimeForExactKey(
                        registrations,
                        serviceType,
                        candidateKey,
                        isKeyed: true,
                        compilation))
                .Aggregate(
                    (ServiceLifetime?)null,
                    WorstLifetime);
        }

        return GetWorstEffectiveLifetimeForExactKey(
            registrations,
            serviceType,
            key,
            isKeyed,
            compilation);
    }

    private static ServiceLifetime? GetWorstEffectiveLifetimeForExactKey(
        IReadOnlyList<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed,
        Compilation compilation)
    {
        var matchingRegistrations = GetMatchingRegistrations(
                registrations,
                serviceType,
                key,
                isKeyed)
            .ToList();
        if (serviceType is INamedTypeSymbol closedGenericType &&
            closedGenericType.IsGenericType &&
            !closedGenericType.IsUnboundGenericType)
        {
            var openGenericType = closedGenericType.ConstructUnboundGenericType();
            matchingRegistrations.AddRange(
                GetMatchingRegistrations(
                        registrations,
                        openGenericType,
                        key,
                        isKeyed)
                    .Where(registration =>
                        IsApplicableOpenGenericRegistration(
                            registration,
                            closedGenericType,
                            compilation)));
        }

        return matchingRegistrations
            .Select(static registration =>
                (ServiceLifetime?)registration.Lifetime)
            .Aggregate(
                (ServiceLifetime?)null,
                WorstLifetime);
    }

    private static bool IsApplicableOpenGenericRegistration(
        ServiceRegistration registration,
        INamedTypeSymbol closedServiceType,
        Compilation compilation)
    {
        if (registration.ImplementationType is not
            {
                IsGenericType: true,
                IsUnboundGenericType: true
            } implementationType)
        {
            return true;
        }

        var implementationDefinition = implementationType.OriginalDefinition;
        var implementationTypeParameters =
            GetAllTypeParameters(implementationDefinition);
        var serviceTypeArguments = GetAllTypeArguments(closedServiceType);
        if (implementationTypeParameters.Length != serviceTypeArguments.Length)
        {
            return false;
        }

        for (var index = 0; index < implementationTypeParameters.Length; index++)
        {
            if (!SatisfiesRuntimeGenericConstraints(
                    implementationTypeParameters[index],
                    serviceTypeArguments[index],
                    implementationTypeParameters,
                    serviceTypeArguments,
                    compilation))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<ITypeParameterSymbol> GetAllTypeParameters(
        INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeParameterSymbol>();
        AddTypeParameters(type, builder);
        return builder.ToImmutable();
    }

    private static void AddTypeParameters(
        INamedTypeSymbol type,
        ImmutableArray<ITypeParameterSymbol>.Builder builder)
    {
        if (type.ContainingType is not null)
        {
            AddTypeParameters(type.ContainingType, builder);
        }

        builder.AddRange(type.TypeParameters);
    }

    private static ImmutableArray<ITypeSymbol> GetAllTypeArguments(
        INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        AddTypeArguments(type, builder);
        return builder.ToImmutable();
    }

    private static void AddTypeArguments(
        INamedTypeSymbol type,
        ImmutableArray<ITypeSymbol>.Builder builder)
    {
        if (type.ContainingType is not null)
        {
            AddTypeArguments(type.ContainingType, builder);
        }

        builder.AddRange(type.TypeArguments);
    }

    private static bool SatisfiesRuntimeGenericConstraints(
        ITypeParameterSymbol typeParameter,
        ITypeSymbol typeArgument,
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        ImmutableArray<ITypeSymbol> typeArguments,
        Compilation compilation)
    {
        if (typeArgument is ITypeParameterSymbol requestedTypeParameter)
        {
            return CanTypeParameterConstraintSetsOverlap(
                typeParameter,
                requestedTypeParameter,
                typeParameters,
                typeArguments,
                compilation);
        }

        if (typeParameter.HasReferenceTypeConstraint &&
            !IsKnownReferenceType(typeArgument))
        {
            return false;
        }

        if (typeParameter.HasValueTypeConstraint &&
            !IsKnownNonNullableValueType(typeArgument))
        {
            return false;
        }

        if (typeParameter.HasConstructorConstraint &&
            !HasPublicParameterlessConstructor(typeArgument))
        {
            return false;
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            var substitutedConstraint = SubstituteTypeParameters(
                constraintType,
                typeParameters,
                typeArguments,
                compilation);
            if (!CanConvertToConstraint(
                    typeArgument,
                    substitutedConstraint,
                    compilation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanTypeParameterConstraintSetsOverlap(
        ITypeParameterSymbol requiredTypeParameter,
        ITypeParameterSymbol requestedTypeParameter,
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        ImmutableArray<ITypeSymbol> typeArguments,
        Compilation compilation)
    {
        if (requiredTypeParameter.HasReferenceTypeConstraint &&
            requestedTypeParameter.HasValueTypeConstraint ||
            requiredTypeParameter.HasValueTypeConstraint &&
            requestedTypeParameter.HasReferenceTypeConstraint)
        {
            return false;
        }

        var requiredBaseType = GetConcreteBaseTypeConstraint(requiredTypeParameter);
        var requestedBaseType = GetConcreteBaseTypeConstraint(requestedTypeParameter);
        if (requiredBaseType is not null &&
            requestedTypeParameter.HasValueTypeConstraint &&
            !IsValueTypeBaseConstraint(requiredBaseType) ||
            requestedBaseType is not null &&
            requiredTypeParameter.HasValueTypeConstraint &&
            !IsValueTypeBaseConstraint(requestedBaseType))
        {
            return false;
        }

        if (requiredBaseType is not null &&
            requestedBaseType is not null &&
            !HasRuntimeConstraintConversion(
                requiredBaseType,
                requestedBaseType,
                compilation) &&
            !HasRuntimeConstraintConversion(
                requestedBaseType,
                requiredBaseType,
                compilation))
        {
            return false;
        }

        if (requiredBaseType is { IsSealed: true } &&
            !SatisfiesKnownConstraints(
                requiredBaseType,
                requestedTypeParameter,
                compilation))
        {
            return false;
        }

        if (requestedBaseType is { IsSealed: true } &&
            !SatisfiesKnownConstraints(
                requestedBaseType,
                requiredTypeParameter,
                compilation))
        {
            return false;
        }

        foreach (var constraintType in requiredTypeParameter.ConstraintTypes)
        {
            var substitutedConstraint = SubstituteTypeParameters(
                constraintType,
                typeParameters,
                typeArguments,
                compilation);
            if (!CanTypeParameterSatisfyConstraint(
                    requestedTypeParameter,
                    substitutedConstraint,
                    compilation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanTypeParameterSatisfyConstraint(
        ITypeParameterSymbol requestedTypeParameter,
        ITypeSymbol constraintType,
        Compilation compilation)
    {
        if (constraintType is ITypeParameterSymbol constraintTypeParameter)
        {
            return CanTypeParameterConstraintSetsOverlap(
                constraintTypeParameter,
                requestedTypeParameter,
                ImmutableArray.Create(constraintTypeParameter),
                ImmutableArray.Create<ITypeSymbol>(constraintTypeParameter),
                compilation);
        }

        if (ContainsTypeParameter(constraintType))
        {
            return true;
        }

        var requestedBaseType = GetConcreteBaseTypeConstraint(requestedTypeParameter);
        if (constraintType.TypeKind == TypeKind.Class &&
            requestedBaseType is not null &&
            !HasRuntimeConstraintConversion(
                requestedBaseType,
                constraintType,
                compilation) &&
            !HasRuntimeConstraintConversion(
                constraintType,
                requestedBaseType,
                compilation))
        {
            return false;
        }

        return requestedBaseType is not { IsSealed: true } ||
            HasRuntimeConstraintConversion(
                requestedBaseType,
                constraintType,
                compilation);
    }

    private static INamedTypeSymbol? GetConcreteBaseTypeConstraint(
        ITypeParameterSymbol typeParameter) =>
        typeParameter.ConstraintTypes
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(constraint =>
                constraint.TypeKind == TypeKind.Class &&
                !ContainsTypeParameter(constraint));

    private static bool IsValueTypeBaseConstraint(INamedTypeSymbol type) =>
        type.SpecialType is SpecialType.System_Enum or
            SpecialType.System_ValueType;

    private static bool SatisfiesKnownConstraints(
        ITypeSymbol candidateType,
        ITypeParameterSymbol typeParameter,
        Compilation compilation)
    {
        if (typeParameter.HasReferenceTypeConstraint &&
            !candidateType.IsReferenceType ||
            typeParameter.HasValueTypeConstraint &&
            !IsKnownNonNullableValueType(candidateType) ||
            typeParameter.HasConstructorConstraint &&
            !HasPublicParameterlessConstructor(candidateType))
        {
            return false;
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            if (!ContainsTypeParameter(constraintType) &&
                !HasRuntimeConstraintConversion(
                    candidateType,
                    constraintType,
                    compilation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanConvertToConstraint(
        ITypeSymbol typeArgument,
        ITypeSymbol constraintType,
        Compilation compilation)
    {
        if (HasRuntimeConstraintConversion(
                typeArgument,
                constraintType,
                compilation))
        {
            return true;
        }

        if (constraintType is ITypeParameterSymbol openConstraint)
        {
            return CanConvertToPotentialTypeParameter(
                typeArgument,
                openConstraint,
                compilation);
        }

        if (ContainsTypeParameter(typeArgument) &&
            CanOpenCompositePotentiallySatisfyConstraint(
                typeArgument,
                constraintType,
                compilation))
        {
            return true;
        }

        // A composite constraint that still contains a consumer type parameter
        // may become applicable for a later closed construction. Keep it unless
        // the fully concrete conversion above proved the boundary.
        return ContainsTypeParameter(constraintType);
    }

    private static bool CanOpenCompositePotentiallySatisfyConstraint(
        ITypeSymbol typeArgument,
        ITypeSymbol constraintType,
        Compilation compilation)
    {
        if (constraintType is not INamedTypeSymbol namedConstraint)
        {
            return false;
        }

        if (typeArgument is INamedTypeSymbol namedArgument)
        {
            for (var candidate = namedArgument;
                 candidate is not null;
                 candidate = candidate.BaseType)
            {
                if (CanOpenNamedTypePotentiallyMatch(
                        candidate,
                        namedConstraint,
                        compilation))
                {
                    return true;
                }
            }
        }

        return typeArgument.AllInterfaces.Any(candidate =>
            CanOpenNamedTypePotentiallyMatch(
                candidate,
                namedConstraint,
                compilation));
    }

    private static bool CanOpenNamedTypePotentiallyMatch(
        INamedTypeSymbol source,
        INamedTypeSymbol destination,
        Compilation compilation)
    {
        if (!SymbolEqualityComparer.Default.Equals(
                source.OriginalDefinition,
                destination.OriginalDefinition) ||
            source.TypeArguments.Length != destination.TypeArguments.Length)
        {
            return false;
        }

        if (source.ContainingType is not null ||
            destination.ContainingType is not null)
        {
            if (source.ContainingType is null ||
                destination.ContainingType is null ||
                !CanTypesPotentiallyEqual(
                    source.ContainingType,
                    destination.ContainingType))
            {
                return false;
            }
        }

        for (var index = 0; index < source.TypeArguments.Length; index++)
        {
            var sourceArgument = source.TypeArguments[index];
            var destinationArgument = destination.TypeArguments[index];
            if (SymbolEqualityComparer.Default.Equals(
                    sourceArgument,
                    destinationArgument))
            {
                continue;
            }

            var variance = source.OriginalDefinition.TypeParameters[index].Variance;
            if (variance == VarianceKind.None
                    ? CanTypesPotentiallyEqual(
                        sourceArgument,
                        destinationArgument)
                    : CanTypesPotentiallyConvert(
                        variance == VarianceKind.Out
                            ? sourceArgument
                            : destinationArgument,
                        variance == VarianceKind.Out
                            ? destinationArgument
                            : sourceArgument,
                        compilation))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool CanTypesPotentiallyEqual(
        ITypeSymbol source,
        ITypeSymbol destination)
    {
        if (SymbolEqualityComparer.Default.Equals(source, destination) ||
            source is ITypeParameterSymbol ||
            destination is ITypeParameterSymbol)
        {
            return true;
        }

        if (source is IArrayTypeSymbol sourceArray &&
            destination is IArrayTypeSymbol destinationArray)
        {
            return sourceArray.Rank == destinationArray.Rank &&
                CanTypesPotentiallyEqual(
                    sourceArray.ElementType,
                    destinationArray.ElementType);
        }

        if (source is not INamedTypeSymbol sourceNamed ||
            destination is not INamedTypeSymbol destinationNamed ||
            !SymbolEqualityComparer.Default.Equals(
                sourceNamed.OriginalDefinition,
                destinationNamed.OriginalDefinition) ||
            sourceNamed.TypeArguments.Length != destinationNamed.TypeArguments.Length)
        {
            return false;
        }

        if (sourceNamed.ContainingType is not null ||
            destinationNamed.ContainingType is not null)
        {
            if (sourceNamed.ContainingType is null ||
                destinationNamed.ContainingType is null ||
                !CanTypesPotentiallyEqual(
                    sourceNamed.ContainingType,
                    destinationNamed.ContainingType))
            {
                return false;
            }
        }

        for (var index = 0; index < sourceNamed.TypeArguments.Length; index++)
        {
            if (!CanTypesPotentiallyEqual(
                    sourceNamed.TypeArguments[index],
                    destinationNamed.TypeArguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanTypesPotentiallyConvert(
        ITypeSymbol source,
        ITypeSymbol destination,
        Compilation compilation)
    {
        if (HasRuntimeConstraintConversion(source, destination, compilation) ||
            source is ITypeParameterSymbol ||
            destination is ITypeParameterSymbol)
        {
            return true;
        }

        if (!ContainsTypeParameter(source) &&
            !ContainsTypeParameter(destination))
        {
            return false;
        }

        return CanOpenCompositePotentiallySatisfyConstraint(
            source,
            destination,
            compilation);
    }

    private static bool CanConvertToPotentialTypeParameter(
        ITypeSymbol typeArgument,
        ITypeParameterSymbol targetTypeParameter,
        Compilation compilation)
    {
        if (targetTypeParameter.HasReferenceTypeConstraint &&
            !typeArgument.IsReferenceType ||
            targetTypeParameter.HasValueTypeConstraint &&
            !IsKnownNonNullableValueType(typeArgument))
        {
            return false;
        }

        foreach (var constraintType in targetTypeParameter.ConstraintTypes)
        {
            if (!ContainsTypeParameter(constraintType) &&
                !HasRuntimeConstraintConversion(
                    typeArgument,
                    constraintType,
                    compilation))
            {
                return false;
            }
        }

        var sealedBaseType = GetConcreteBaseTypeConstraint(targetTypeParameter);
        return targetTypeParameter.HasConstructorConstraint &&
            sealedBaseType is { IsSealed: true }
            ? HasPublicParameterlessConstructor(sealedBaseType)
            : true;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol arrayType => ContainsTypeParameter(arrayType.ElementType),
            IPointerTypeSymbol pointerType => ContainsTypeParameter(pointerType.PointedAtType),
            INamedTypeSymbol namedType =>
                namedType.TypeArguments.Any(ContainsTypeParameter) ||
                namedType.ContainingType is not null &&
                ContainsTypeParameter(namedType.ContainingType),
            _ => false,
        };

    private static bool HasRuntimeConstraintConversion(
        ITypeSymbol source,
        ITypeSymbol destination,
        Compilation compilation)
    {
        var conversion = compilation.ClassifyConversion(source, destination);
        return conversion.IsIdentity ||
            conversion.IsReference ||
            conversion.IsBoxing;
    }

    private static bool IsKnownReferenceType(ITypeSymbol typeArgument) =>
        typeArgument.IsReferenceType ||
        typeArgument is ITypeParameterSymbol { HasReferenceTypeConstraint: true };

    private static bool IsKnownNonNullableValueType(ITypeSymbol typeArgument)
    {
        if (typeArgument is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.HasValueTypeConstraint;
        }

        return typeArgument.IsValueType &&
            typeArgument is not INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            };
    }

    private static bool HasPublicParameterlessConstructor(ITypeSymbol typeArgument)
    {
        if (typeArgument.IsValueType)
        {
            return true;
        }

        if (typeArgument is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.HasConstructorConstraint ||
                typeParameter.HasValueTypeConstraint;
        }

        return typeArgument is INamedTypeSymbol { IsAbstract: false } namedType &&
            namedType.InstanceConstructors.Any(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.IsEmpty);
    }

    private static ITypeSymbol SubstituteTypeParameters(
        ITypeSymbol type,
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        ImmutableArray<ITypeSymbol> typeArguments,
        Compilation compilation)
    {
        if (type is ITypeParameterSymbol typeParameter)
        {
            for (var index = 0; index < typeParameters.Length; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(typeParameters[index], typeParameter))
                {
                    return typeArguments[index];
                }
            }

            return type;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return compilation.CreateArrayTypeSymbol(
                SubstituteTypeParameters(
                    arrayType.ElementType,
                    typeParameters,
                    typeArguments,
                    compilation),
                arrayType.Rank);
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return compilation.CreatePointerTypeSymbol(
                SubstituteTypeParameters(
                    pointerType.PointedAtType,
                    typeParameters,
                    typeArguments,
                    compilation));
        }

        if (type is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType &&
            namedType.ContainingType is null)
        {
            return type;
        }

        INamedTypeSymbol definition;
        if (namedType.ContainingType is not null)
        {
            var substitutedContainingType = (INamedTypeSymbol)SubstituteTypeParameters(
                namedType.ContainingType,
                typeParameters,
                typeArguments,
                compilation);
            definition = substitutedContainingType
                .GetTypeMembers(namedType.Name, namedType.Arity)
                .FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(
                        candidate.OriginalDefinition,
                        namedType.OriginalDefinition))
                ?? namedType.OriginalDefinition;
        }
        else
        {
            definition = namedType.OriginalDefinition;
        }

        if (namedType.Arity == 0)
        {
            return definition;
        }

        var substitutedArguments = namedType.TypeArguments
            .Select(typeArgument =>
                SubstituteTypeParameters(
                    typeArgument,
                    typeParameters,
                    typeArguments,
                    compilation))
            .ToArray();
        return definition.Construct(substitutedArguments);
    }

    private static IEnumerable<ServiceRegistration> GetMatchingRegistrations(
        IEnumerable<ServiceRegistration> registrations,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed) =>
        registrations.Where(registration =>
            SymbolEqualityComparer.Default.Equals(
                registration.ServiceType,
                serviceType) &&
            registration.IsKeyed == isKeyed &&
            Equals(registration.Key, key));

    private static ServiceLifetime? WorstLifetime(ServiceLifetime? left, ServiceLifetime? right)
    {
        if (left is null)
        {
            return right;
        }
        if (right is null)
        {
            return left;
        }

        // ServiceLifetime ordering: Singleton (0) < Scoped (1) < Transient (2).
        // The shortest-lived registration is the captive risk for a singleton consumer.
        return (int)left.Value >= (int)right.Value ? left : right;
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

    private static (object? key, bool isKeyed) GetServiceKey(
        IParameterSymbol parameter,
        object? inheritedKey,
        bool inheritedIsKeyed)
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

                return (inheritedKey, inheritedIsKeyed);
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
