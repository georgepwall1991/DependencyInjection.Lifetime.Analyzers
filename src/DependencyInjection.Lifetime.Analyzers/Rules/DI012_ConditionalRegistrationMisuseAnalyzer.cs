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
/// Analyzer that detects conditional registration issues:
/// - TryAdd* after Add* for the same service type (TryAdd will be ignored)
/// - Multiple Add* calls for the same service type (later registration overrides earlier)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI012_ConditionalRegistrationMisuseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.TryAddIgnored,
            DiagnosticDescriptors.DuplicateRegistration);

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

            var invocationObservations = new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel);

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
                SyntaxKind.InvocationExpression);

            // Second pass: analyze registration order at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeRegistrationOrder(
                    endContext,
                    registrationCollector,
                    invocationObservations.ToImmutableArray()));
        });
    }

    private static void AnalyzeRegistrationOrder(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        ImmutableArray<ServiceCollectionReachabilityAnalyzer.InvocationObservation> invocationObservations)
    {
        var orderedRegistrations = registrationCollector.OrderedRegistrations.ToImmutableArray();
        if (orderedRegistrations.IsDefaultOrEmpty)
        {
            return;
        }

        var reachabilityAnalyzer = invocationObservations.IsDefaultOrEmpty
            ? null
            : ServiceCollectionReachabilityAnalyzer.Create(
                context.Compilation,
                invocationObservations,
                orderedRegistrations);

        var effectiveOrderedRegistrations = reachabilityAnalyzer is null
            ? OrderedRegistrationOrdering.SortBySourceLocation(orderedRegistrations)
            : reachabilityAnalyzer.AlignOrderedRegistrationsToRootFlows(orderedRegistrations);

        // Group registrations by service type and key (keyed services should be treated independently)
        var registrationsByServiceType = effectiveOrderedRegistrations
            .GroupBy(
                r => new RegistrationGroupKey(r.ServiceType, r.Key, r.IsKeyed, r.FlowKey),
                RegistrationGroupKeyComparer.Instance)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in registrationsByServiceType)
        {
            // The registrations are already grouped in either stable source order (for direct
            // flows) or root-flow execution order (for invoked wrappers/helpers).
            AnalyzeServiceTypeRegistrations(context, group.ToList(), reachabilityAnalyzer);
        }
    }

    private static void AnalyzeServiceTypeRegistrations(
        CompilationAnalysisContext context,
        List<OrderedRegistration> registrations,
        ServiceCollectionReachabilityAnalyzer? reachabilityAnalyzer)
    {
        if (registrations.Count < 2)
        {
            return;
        }

        var serviceTypeName = registrations[0].ServiceType.Name;

        // Track the first Add* registration
        OrderedRegistration? firstAddRegistration = null;

        for (var i = 0; i < registrations.Count; i++)
        {
            var current = registrations[i];
            var currentHasOpaquePredecessor =
                reachabilityAnalyzer?.HasOpaquePredecessor(current.Location) == true;

            if (!current.IsTryAdd)
            {
                // This is an Add* registration
                if (firstAddRegistration is null)
                {
                    firstAddRegistration = current;
                }
                else if (currentHasOpaquePredecessor &&
                         reachabilityAnalyzer?.HasOpaquePredecessor(firstAddRegistration.Location) != true)
                {
                    // An opaque wrapper/helper call may have changed the effective order.
                    // Treat this registration as the new baseline instead of comparing
                    // across the uncertainty boundary.
                    firstAddRegistration = current;
                }
                else
                {
                    // Duplicate Add* registration - later one overrides earlier
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateRegistration,
                        current.Location,
                        serviceTypeName,
                        FormatLocation(firstAddRegistration.Location));

                    context.ReportDiagnostic(diagnostic);
                }
            }
            else
            {
                // This is a TryAdd* registration
                if (firstAddRegistration is not null)
                {
                    if (currentHasOpaquePredecessor &&
                        reachabilityAnalyzer?.HasOpaquePredecessor(firstAddRegistration.Location) != true)
                    {
                        firstAddRegistration = null;
                        continue;
                    }

                    // TryAdd after Add - TryAdd will be ignored
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.TryAddIgnored,
                        current.Location,
                        serviceTypeName,
                        FormatLocation(firstAddRegistration.Location));

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static string FormatLocation(Location location)
    {
        var lineSpan = location.GetLineSpan();
        if (lineSpan.IsValid)
        {
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            return $"line {lineNumber}";
        }

        return "unknown location";
    }

    private readonly struct RegistrationGroupKey : System.IEquatable<RegistrationGroupKey>
    {
        public INamedTypeSymbol ServiceType { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }
        public string? FlowKey { get; }

        public RegistrationGroupKey(INamedTypeSymbol serviceType, object? key, bool isKeyed, string? flowKey)
        {
            ServiceType = serviceType;
            Key = key;
            IsKeyed = isKeyed;
            FlowKey = flowKey;
        }

        public bool Equals(RegistrationGroupKey other)
        {
            return SymbolEqualityComparer.Default.Equals(ServiceType, other.ServiceType)
                   && Equals(Key, other.Key)
                   && IsKeyed == other.IsKeyed
                   && string.Equals(FlowKey, other.FlowKey, System.StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is RegistrationGroupKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SymbolEqualityComparer.Default.GetHashCode(ServiceType);
                hash = (hash * 397) ^ (Key?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ IsKeyed.GetHashCode();
                hash = (hash * 397) ^ (FlowKey is null ? 0 : System.StringComparer.Ordinal.GetHashCode(FlowKey));
                return hash;
            }
        }
    }

    private sealed class RegistrationGroupKeyComparer : IEqualityComparer<RegistrationGroupKey>
    {
        public static readonly RegistrationGroupKeyComparer Instance = new();

        public bool Equals(RegistrationGroupKey x, RegistrationGroupKey y) => x.Equals(y);

        public int GetHashCode(RegistrationGroupKey obj) => obj.GetHashCode();
    }
}
