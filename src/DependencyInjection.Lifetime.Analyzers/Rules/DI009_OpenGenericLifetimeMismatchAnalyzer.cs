using System;
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
        var knownLifetimeClassifier = new KnownServiceLifetimeClassifier(wellKnownTypes);
        var definitelyRemovedRegistrations = BuildDefinitelyRemovedRegistrationLookup(
            registrationCollector.AllRegistrations,
            registrationCollector.OrderedMutations);

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

            if (IsDefinitelyRemoved(registration, definitelyRemovedRegistrations))
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
                            knownLifetimeClassifier,
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
        KnownServiceLifetimeClassifier knownLifetimeClassifier,
        out INamedTypeSymbol dependencyType,
        out ServiceLifetime? dependencyLifetime)
    {
        dependencyType = null!;
        dependencyLifetime = null;

        if (TryGetEnumerableElementType(parameterType, out var elementType))
        {
            dependencyType = elementType;
            var userElementLifetime = registrationCollector.GetLifetime(elementType, serviceKey.key, serviceKey.isKeyed);
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
            var closedLifetime = registrationCollector.GetLifetime(closedNamedType, serviceKey.key, serviceKey.isKeyed);
            if (closedLifetime is not null)
            {
                dependencyLifetime = closedLifetime;
                return true;
            }
        }

        dependencyLifetime = registrationCollector.GetLifetime(nonGenericType, serviceKey.key, serviceKey.isKeyed);
        if (dependencyLifetime is null &&
            knownLifetimeClassifier.TryGetLifetime(parameterType, serviceKey.isKeyed, out var knownLifetime))
        {
            dependencyLifetime = knownLifetime;
        }
        return dependencyLifetime is not null;
    }

    private static HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> BuildDefinitelyRemovedRegistrationLookup(
        IEnumerable<ServiceRegistration> registrations,
        IEnumerable<OrderedRegistrationMutation> mutations)
    {
        var removedRegistrations = new HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey>();
        var registrationsByTree = registrations
            .Where(static registration => registration.FlowKey is not null &&
                                          registration.Location.SourceTree is not null)
            .GroupBy(static registration => registration.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static registration => registration.Location.SourceSpan.Start).ToArray());
        var mutationsByTree = mutations
            .Where(static mutation => mutation.FlowKey is not null &&
                                      mutation.Location.SourceTree is not null)
            .GroupBy(static mutation => mutation.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static mutation => mutation.Location.SourceSpan.Start).ToArray());

        foreach (var tree in registrationsByTree.Keys.Union(mutationsByTree.Keys))
        {
            registrationsByTree.TryGetValue(tree, out var orderedRegistrations);
            mutationsByTree.TryGetValue(tree, out var orderedMutations);
            orderedRegistrations ??= [];
            orderedMutations ??= [];

            var activeRegistrations = new List<ServiceRegistration>();
            var registrationIndex = 0;
            var mutationIndex = 0;

            while (registrationIndex < orderedRegistrations.Length ||
                   mutationIndex < orderedMutations.Length)
            {
                var nextRegistrationStart = registrationIndex < orderedRegistrations.Length
                    ? orderedRegistrations[registrationIndex].Location.SourceSpan.Start
                    : int.MaxValue;
                var nextMutationStart = mutationIndex < orderedMutations.Length
                    ? orderedMutations[mutationIndex].Location.SourceSpan.Start
                    : int.MaxValue;

                if (nextRegistrationStart < nextMutationStart)
                {
                    activeRegistrations.Add(orderedRegistrations[registrationIndex]);
                    registrationIndex++;
                    continue;
                }

                var mutation = orderedMutations[mutationIndex];
                if (CanTreatMutationAsDefinite(mutation))
                {
                    ApplyMutation(
                        mutation,
                        activeRegistrations,
                        removedRegistrations);
                }

                mutationIndex++;
            }
        }

        return removedRegistrations;
    }

    private static bool CanTreatMutationAsDefinite(OrderedRegistrationMutation mutation)
    {
        var sourceTree = mutation.Location.SourceTree;
        if (sourceTree is null)
        {
            return false;
        }

        var root = sourceTree.GetRoot();
        var node = root.FindNode(mutation.Location.SourceSpan, getInnermostNodeForTie: true);
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case IfStatementSyntax:
                case SwitchStatementSyntax:
                case SwitchExpressionSyntax:
                case ConditionalExpressionSyntax:
                case ConditionalAccessExpressionSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case ForEachVariableStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case TryStatementSyntax:
                case CatchClauseSyntax:
                case FinallyClauseSyntax:
                case LocalFunctionStatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                    return false;
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return true;
            }
        }

        return false;
    }

    private static void ApplyMutation(
        OrderedRegistrationMutation mutation,
        List<ServiceRegistration> activeRegistrations,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> removedRegistrations)
    {
        if (mutation.Kind == RegistrationMutationKind.RemoveAll)
        {
            for (var i = activeRegistrations.Count - 1; i >= 0; i--)
            {
                if (!CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
                {
                    continue;
                }

                AddRemovedRegistration(activeRegistrations[i], removedRegistrations);
                activeRegistrations.RemoveAt(i);
            }

            return;
        }

        for (var i = 0; i < activeRegistrations.Count; i++)
        {
            if (!CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
            {
                continue;
            }

            AddRemovedRegistration(activeRegistrations[i], removedRegistrations);
            activeRegistrations.RemoveAt(i);
            return;
        }
    }

    private static bool CanMutationRemoveRegistration(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration) =>
        string.Equals(mutation.FlowKey, registration.FlowKey, StringComparison.Ordinal) &&
        SymbolEqualityComparer.Default.Equals(mutation.ServiceType, registration.ServiceType) &&
        mutation.IsKeyed == registration.IsKeyed &&
        Equals(mutation.Key, registration.Key) &&
        IsSameExecutionScope(mutation.Location, registration.Location) &&
        IsSameStraightLineBlock(mutation.Location, registration.Location);

    private static bool IsSameExecutionScope(Location mutationLocation, Location registrationLocation)
    {
        var mutationScope = GetExecutionScopeKey(mutationLocation);
        var registrationScope = GetExecutionScopeKey(registrationLocation);
        return mutationScope.HasValue &&
               registrationScope.HasValue &&
               mutationScope.Value.Equals(registrationScope.Value);
    }

    private static ServiceCollectionReachabilityAnalyzer.LocationKey? GetExecutionScopeKey(Location location)
    {
        var sourceTree = location.SourceTree;
        if (sourceTree is null)
        {
            return null;
        }

        var root = sourceTree.GetRoot();
        var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case CompilationUnitSyntax:
                    return ServiceCollectionReachabilityAnalyzer.LocationKey.Create(ancestor.GetLocation());
            }
        }

        return null;
    }

    private static bool IsSameStraightLineBlock(Location mutationLocation, Location registrationLocation)
    {
        var mutationStatement = GetEnclosingStatement(mutationLocation);
        var registrationStatement = GetEnclosingStatement(registrationLocation);
        if (mutationStatement is null ||
            registrationStatement is null ||
            mutationStatement.Parent is not BlockSyntax mutationBlock ||
            !ReferenceEquals(mutationBlock, registrationStatement.Parent))
        {
            return false;
        }

        var statements = mutationBlock.Statements;
        var registrationIndex = statements.IndexOf(registrationStatement);
        var mutationIndex = statements.IndexOf(mutationStatement);
        if (registrationIndex < 0 || mutationIndex <= registrationIndex)
        {
            return false;
        }

        for (var i = registrationIndex + 1; i < mutationIndex; i++)
        {
            if (CanBypassLaterMutation(statements[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static StatementSyntax? GetEnclosingStatement(Location location)
    {
        var sourceTree = location.SourceTree;
        if (sourceTree is null)
        {
            return null;
        }

        var root = sourceTree.GetRoot();
        var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        return node.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
    }

    private static bool CanBypassLaterMutation(StatementSyntax statement) =>
        statement is IfStatementSyntax or
            SwitchStatementSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            ForEachVariableStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax or
            TryStatementSyntax or
            ReturnStatementSyntax or
            ThrowStatementSyntax or
            BreakStatementSyntax or
            ContinueStatementSyntax or
            GotoStatementSyntax or
            LocalFunctionStatementSyntax;

    private static void AddRemovedRegistration(
        ServiceRegistration registration,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> removedRegistrations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location);
        if (locationKey.HasValue)
        {
            removedRegistrations.Add(locationKey.Value);
        }
    }

    private static bool IsDefinitelyRemoved(
        ServiceRegistration registration,
        HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> definitelyRemovedRegistrations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(registration.Location);
        return locationKey.HasValue &&
               definitelyRemovedRegistrations.Contains(locationKey.Value);
    }

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
