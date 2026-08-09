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
/// Analyzer that detects container-created services implementing only <c>IAsyncDisposable</c>.
/// The container tracks them for disposal, but a synchronous <c>Dispose()</c> on the provider or
/// scope cannot dispose them and throws <c>InvalidOperationException</c> instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI032_AsyncOnlyDisposableRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.AsyncOnlyDisposableRegistration);

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

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            var invocationObservations = new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel
                    );

                    if (ServiceCollectionReachabilityAnalyzer.IsPotentialServiceCollectionWrapperInvocation(
                            invocation,
                            syntaxContext.SemanticModel))
                    {
                        invocationObservations.Enqueue(
                            new ServiceCollectionReachabilityAnalyzer.InvocationObservation(
                                invocation,
                                syntaxContext.SemanticModel));
                    }
                },
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                var mutations = registrationCollector.OrderedMutations.ToImmutableArray();
                var registrations = registrationCollector.AllRegistrations.ToImmutableArray();
                var unalignableWrapperLocations = BuildUnalignableWrapperLocations(
                    invocationObservations.ToImmutableArray());

                foreach (var registration in registrations)
                {
                    // Only instances the container creates are tracked for disposal; a pre-built
                    // instance is the caller's to dispose (DI033).
                    if (
                        registration.HasImplementationInstance
                        // Transient disposables are DI008's finding: it already reports the
                        // whole tracking-and-disposal problem for them, and a second diagnostic
                        // on the same registration is noise.
                        || registration.Lifetime
                            is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped)
                    )
                    {
                        continue;
                    }

                    // A factory result is created by the container and tracked exactly like a type
                    // registration, so a lambda that constructs a known type counts too.
                    var implementationType =
                        registration.ImplementationType
                        ?? registration.FactoryConstructedType;
                    if (implementationType is null)
                    {
                        continue;
                    }

                    // A descriptor removed or replaced after it was added never reaches the
                    // provider, so it cannot make disposal throw.
                    if (IsRegistrationDefinitelyRemoved(
                            registration,
                            mutations,
                            unalignableWrapperLocations))
                    {
                        continue;
                    }

                    if (
                        !wellKnownTypes.ImplementsIAsyncDisposable(implementationType)
                        || wellKnownTypes.ImplementsIDisposable(implementationType)
                    )
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(
                        Diagnostic.Create(
                            DiagnosticDescriptors.AsyncOnlyDisposableRegistration,
                            registration.Location,
                            implementationType.Name
                        )
                    );
                }
            });
        });
    }

    private static HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey> BuildUnalignableWrapperLocations(
        ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation> observations)
    {
        var locations = new HashSet<ServiceCollectionReachabilityAnalyzer.LocationKey>();
        foreach (var observation in observations)
        {
            var containingMethod = ServiceCollectionReachabilityAnalyzer.NormalizeContainingMethod(
                observation.SemanticModel.GetEnclosingSymbol(observation.Invocation.SpanStart) as IMethodSymbol);
            if (containingMethod is null ||
                !ServiceCollectionReachabilityAnalyzer.IsSourceDefinedCustomServiceCollectionWrapper(containingMethod))
            {
                continue;
            }

            var location = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(
                observation.Invocation.GetLocation());
            if (location.HasValue)
            {
                locations.Add(location.Value);
            }
        }

        return locations;
    }

    private static bool IsRegistrationDefinitelyRemoved(
        ServiceRegistration registration,
        ImmutableArray<OrderedRegistrationMutation> mutations,
        ISet<ServiceCollectionReachabilityAnalyzer.LocationKey> unalignableWrapperLocations)
    {
        if (IsUnalignableWrapperLocation(registration.Location, unalignableWrapperLocations))
        {
            return true;
        }

        return mutations.Any(mutation =>
            SymbolEqualityComparer.Default.Equals(mutation.ServiceType, registration.ServiceType)
            && mutation.IsKeyed == registration.IsKeyed
            && object.Equals(mutation.Key, registration.Key)
            && (IsUnalignableWrapperLocation(mutation.Location, unalignableWrapperLocations)
                || (string.Equals(
                        mutation.FlowKey,
                        registration.FlowKey,
                        StringComparison.Ordinal)
                    && IsAfter(mutation.Location, registration.Location))));
    }

    private static bool IsUnalignableWrapperLocation(
        Location location,
        ISet<ServiceCollectionReachabilityAnalyzer.LocationKey> unalignableWrapperLocations)
    {
        var locationKey = ServiceCollectionReachabilityAnalyzer.LocationKey.Create(location);
        return locationKey.HasValue && unalignableWrapperLocations.Contains(locationKey.Value);
    }

    /// <summary>Source order, treating locations in different files as incomparable.</summary>
    private static bool IsAfter(Location candidate, Location reference)
    {
        if (candidate.SourceTree?.FilePath != reference.SourceTree?.FilePath)
        {
            return false;
        }

        var byStart = candidate.SourceSpan.Start.CompareTo(reference.SourceSpan.Start);
        return byStart != 0
            ? byStart > 0
            : candidate.SourceSpan.End > reference.SourceSpan.End;
    }
}
