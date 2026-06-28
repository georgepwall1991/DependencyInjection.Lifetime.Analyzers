using System.Collections.Concurrent;
using System;
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
                r => new RegistrationGroupKey(
                    r.ServiceType,
                    r.Key,
                    r.IsKeyed,
                    NormalizeFlowKey(r.FlowKey)),
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

        // Track the first Add* registration and how many active descriptors the slot holds.
        OrderedRegistration? firstAddRegistration = null;
        OrderedRegistration? complementaryBranchCoverageRegistration = null;
        var activeDescriptorCount = 0;

        for (var i = 0; i < registrations.Count; i++)
        {
            var current = registrations[i];
            var currentHasOpaquePredecessor =
                reachabilityAnalyzer?.HasOpaquePredecessor(current.Location) == true;

            if (current.SkipIfAlreadyRegistered &&
                firstAddRegistration is not null)
            {
                if (currentHasOpaquePredecessor &&
                    reachabilityAnalyzer?.HasOpaquePredecessor(firstAddRegistration.Location) != true)
                {
                    firstAddRegistration = null;
                    complementaryBranchCoverageRegistration = null;
                    continue;
                }

                continue;
            }

            if (!current.IsTryAdd)
            {
                // Replace removes ONE earlier descriptor before adding its own. With at most one
                // prior descriptor that is intentional override semantics, not an accidental
                // duplicate — but with two or more, a descriptor survives the removal and the
                // replacement is still another override of it.
                if (current.MethodName == "Replace")
                {
                    var hasEarlierRegistrationOutsideBranchChain =
                        TryGetRegistrationIfBranch(current, out var replaceIf, out var replaceBranch) &&
                        HasEarlierRegistrationOutsideBranchChain(current, replaceIf, replaceBranch, registrations);

                    if (activeDescriptorCount >= 2 && firstAddRegistration is not null)
                    {
                        var replaceDiagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.DuplicateRegistration,
                            current.Location,
                            serviceTypeName,
                            $"line {registrations[i - 1].Location.GetLineSpan().StartLinePosition.Line + 1}");
                        context.ReportDiagnostic(replaceDiagnostic);
                    }

                    activeDescriptorCount = Math.Max(activeDescriptorCount - 1, 0) + 1;
                    if (!hasEarlierRegistrationOutsideBranchChain)
                    {
                        firstAddRegistration = current;
                        complementaryBranchCoverageRegistration = null;
                    }

                    continue;
                }

                var activeDescriptorCountBeforeCurrent = activeDescriptorCount;
                activeDescriptorCount++;

                // This is an Add* registration
                if (firstAddRegistration is null)
                {
                    firstAddRegistration = current;
                    complementaryBranchCoverageRegistration = null;
                }
                else if (currentHasOpaquePredecessor &&
                         reachabilityAnalyzer?.HasOpaquePredecessor(firstAddRegistration.Location) != true)
                {
                    // An opaque wrapper/helper call may have changed the effective order.
                    // Treat this registration as the new baseline instead of comparing
                    // across the uncertainty boundary.
                    firstAddRegistration = current;
                    complementaryBranchCoverageRegistration = null;
                }
                else
                {
                    if (AreMutuallyExclusiveBranches(firstAddRegistration, current, registrations))
                    {
                        complementaryBranchCoverageRegistration = AreComplementarySiblingGuardRegistrations(firstAddRegistration, current)
                            ? firstAddRegistration
                            : null;
                        firstAddRegistration = current;
                        activeDescriptorCount = Math.Max(1, activeDescriptorCountBeforeCurrent);
                        continue;
                    }

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
                    if (complementaryBranchCoverageRegistration is not null)
                    {
                        if (currentHasOpaquePredecessor &&
                            reachabilityAnalyzer?.HasOpaquePredecessor(complementaryBranchCoverageRegistration.Location) != true)
                        {
                            complementaryBranchCoverageRegistration = null;
                            firstAddRegistration = null;
                            continue;
                        }

                        var complementaryCoverageDiagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.TryAddIgnored,
                            current.Location,
                            serviceTypeName,
                            FormatLocation(complementaryBranchCoverageRegistration.Location));

                        context.ReportDiagnostic(complementaryCoverageDiagnostic);
                        activeDescriptorCount = Math.Max(activeDescriptorCount, 1);
                        continue;
                    }

                    if (IsUnconditionalFallbackAfterGuardedRegistration(
                            firstAddRegistration,
                            current,
                            registrations))
                    {
                        activeDescriptorCount = Math.Max(activeDescriptorCount, 1);
                        continue;
                    }

                    if (AreOppositeBranchesOfSameIfChain(firstAddRegistration, current))
                    {
                        firstAddRegistration = current;
                        complementaryBranchCoverageRegistration = null;
                        activeDescriptorCount = Math.Max(activeDescriptorCount, 1);
                        continue;
                    }

                    if (!ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(firstAddRegistration.FlowKey) &&
                        !ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(current.FlowKey) &&
                        AreComplementarySiblingGuardRegistrations(firstAddRegistration, current))
                    {
                        activeDescriptorCount = Math.Max(activeDescriptorCount, 1);
                        continue;
                    }

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

    private static bool AreMutuallyExclusiveBranches(
        OrderedRegistration firstRegistration,
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations)
    {
        if (ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(firstRegistration.FlowKey) ||
            ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(currentRegistration.FlowKey))
        {
            return false;
        }

        if (AreComplementarySiblingGuardRegistrations(firstRegistration, currentRegistration))
        {
            return !HasEarlierEquivalentSiblingGuardRegistration(currentRegistration, registrations);
        }

        foreach (var firstBranchInfo in EnumerateRegistrationContainingIfBranches(firstRegistration))
        {
            foreach (var currentBranchInfo in EnumerateRegistrationContainingIfBranches(currentRegistration))
            {
                if (firstBranchInfo.IfStatement.SyntaxTree == currentBranchInfo.IfStatement.SyntaxTree &&
                    firstBranchInfo.IfStatement.Span == currentBranchInfo.IfStatement.Span &&
                    firstBranchInfo.Branch.SyntaxTree == currentBranchInfo.Branch.SyntaxTree &&
                    firstBranchInfo.Branch.Span != currentBranchInfo.Branch.Span &&
                    !IsInsideLoop(firstBranchInfo.IfStatement) &&
                    !HasEarlierRegistrationInBranch(
                        currentBranchInfo.IfStatement,
                        currentBranchInfo.Branch,
                        currentRegistration,
                        registrations) &&
                    !HasEarlierRegistrationOutsideBranchChain(
                        firstRegistration,
                        firstBranchInfo.IfStatement,
                        firstBranchInfo.Branch,
                        registrations))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreOppositeBranchesOfSameIfChain(
        OrderedRegistration firstRegistration,
        OrderedRegistration currentRegistration)
    {
        if (ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(firstRegistration.FlowKey) ||
            ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(currentRegistration.FlowKey))
        {
            return false;
        }

        foreach (var firstBranchInfo in EnumerateRegistrationContainingIfBranches(firstRegistration))
        {
            foreach (var currentBranchInfo in EnumerateRegistrationContainingIfBranches(currentRegistration))
            {
                if (firstBranchInfo.IfStatement.SyntaxTree == currentBranchInfo.IfStatement.SyntaxTree &&
                    firstBranchInfo.IfStatement.Span == currentBranchInfo.IfStatement.Span &&
                    firstBranchInfo.Branch.SyntaxTree == currentBranchInfo.Branch.SyntaxTree &&
                    firstBranchInfo.Branch.Span != currentBranchInfo.Branch.Span &&
                    !IsInsideLoop(firstBranchInfo.IfStatement))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreComplementarySiblingGuardRegistrations(
        OrderedRegistration firstRegistration,
        OrderedRegistration currentRegistration)
    {
        foreach (var firstLocation in EnumerateRegistrationAnalysisLocations(firstRegistration))
        {
            foreach (var currentLocation in EnumerateRegistrationAnalysisLocations(currentRegistration))
            {
                if (AreComplementarySiblingGuardLocations(firstLocation, currentLocation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasEarlierEquivalentSiblingGuardRegistration(
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations)
    {
        return registrations.Any(registration =>
            registration.Order < currentRegistration.Order &&
            !registration.IsTryAdd &&
            SameFlowKey(registration.FlowKey, currentRegistration.FlowKey) &&
            AreEquivalentSiblingGuardRegistrations(registration, currentRegistration));
    }

    private static bool AreEquivalentSiblingGuardRegistrations(
        OrderedRegistration firstRegistration,
        OrderedRegistration currentRegistration)
    {
        foreach (var firstLocation in EnumerateRegistrationAnalysisLocations(firstRegistration))
        {
            foreach (var currentLocation in EnumerateRegistrationAnalysisLocations(currentRegistration))
            {
                if (AreEquivalentSiblingGuardLocations(firstLocation, currentLocation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreEquivalentSiblingGuardLocations(
        Location firstLocation,
        Location currentLocation)
    {
        if (!TryGetIfBranch(firstLocation, out var firstIf, out _, out var firstBranch) ||
            !TryGetIfBranch(currentLocation, out var currentIf, out _, out var currentBranch) ||
            firstIf.Else is not null ||
            currentIf.Else is not null ||
            !IsSameBranch(firstBranch, firstIf.Statement) ||
            !IsSameBranch(currentBranch, currentIf.Statement) ||
            firstIf.Parent is not BlockSyntax block ||
            currentIf.Parent != block ||
            IsInsideLoop(firstIf) ||
            IsInsideLoop(currentIf))
        {
            return false;
        }

        var firstIndex = block.Statements.IndexOf(firstIf);
        var currentIndex = block.Statements.IndexOf(currentIf);
        var conditionIdentifiers = new HashSet<string>(System.StringComparer.Ordinal);
        CollectConditionIdentifierNames(firstIf.Condition, conditionIdentifiers);
        CollectConditionIdentifierNames(currentIf.Condition, conditionIdentifiers);
        var invocationMutableIdentifiers = GetIdentifiersPotentiallyMutatedByInvocation(
            firstIf.Condition,
            currentIf.Condition);

        return firstIndex >= 0 &&
               currentIndex > firstIndex &&
               AreEquivalentConditions(firstIf.Condition, currentIf.Condition) &&
               !WritesAnyIdentifier(firstIf.Statement, conditionIdentifiers, invocationMutableIdentifiers) &&
               AreConditionIdentifiersStableBetween(
                   firstIf.Condition,
                   currentIf.Condition,
                   block,
                   firstIndex,
                   currentIndex,
                   invocationMutableIdentifiers);
    }

    private static bool AreComplementarySiblingGuardLocations(
        Location firstLocation,
        Location currentLocation)
    {
        if (!TryGetIfBranch(firstLocation, out var firstIf, out _, out var firstBranch) ||
            !TryGetIfBranch(currentLocation, out var currentIf, out _, out var currentBranch) ||
            firstIf.Else is not null ||
            currentIf.Else is not null ||
            !IsSameBranch(firstBranch, firstIf.Statement) ||
            !IsSameBranch(currentBranch, currentIf.Statement) ||
            firstIf.Parent is not BlockSyntax block ||
            currentIf.Parent != block ||
            IsInsideLoop(firstIf) ||
            IsInsideLoop(currentIf))
        {
            return false;
        }

        var firstIndex = block.Statements.IndexOf(firstIf);
        var currentIndex = block.Statements.IndexOf(currentIf);
        var conditionIdentifiers = new HashSet<string>(System.StringComparer.Ordinal);
        CollectConditionIdentifierNames(firstIf.Condition, conditionIdentifiers);
        CollectConditionIdentifierNames(currentIf.Condition, conditionIdentifiers);
        var invocationMutableIdentifiers = GetIdentifiersPotentiallyMutatedByInvocation(
            firstIf.Condition,
            currentIf.Condition);
        return firstIndex >= 0 &&
               currentIndex > firstIndex &&
               IsNegatedCondition(currentIf.Condition, firstIf.Condition) &&
               !WritesAnyIdentifier(firstIf.Statement, conditionIdentifiers, invocationMutableIdentifiers) &&
               AreConditionIdentifiersStableBetween(
                   firstIf.Condition,
                   currentIf.Condition,
                   block,
                   firstIndex,
                   currentIndex,
                   invocationMutableIdentifiers);
    }

    private static bool HasEarlierRegistrationOutsideBranchChain(
        OrderedRegistration firstRegistration,
        IfStatementSyntax ifStatement,
        SyntaxNode branch,
        List<OrderedRegistration> registrations)
    {
        if (firstRegistration.MethodName != "Replace")
        {
            return false;
        }

        foreach (var registration in registrations)
        {
            if (registration.Order >= firstRegistration.Order ||
                registration.IsTryAdd ||
                !SameFlowKey(registration.FlowKey, firstRegistration.FlowKey))
            {
                continue;
            }

            if (TryGetNode(registration.Location) is { } registrationNode &&
                branch.Span.Contains(registrationNode.SpanStart))
            {
                continue;
            }

            if (TryGetIfBranch(registration.Location, out var registrationIf, out _, out _) &&
                registrationIf.SyntaxTree == ifStatement.SyntaxTree &&
                registrationIf.Span == ifStatement.Span)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsUnconditionalFallbackAfterGuardedRegistration(
        OrderedRegistration guardedRegistration,
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations)
    {
        if (!TryGetRegistrationIfBranch(guardedRegistration, out var guardedIf, out var guardedBranch) ||
            TryGetIfBranch(currentRegistration.Location, out _, out _, out _) ||
            IsInsideLoop(guardedIf) ||
            ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(guardedRegistration.FlowKey) ||
            ServiceCollectionReachabilityAnalyzer.IsLoopedWrapperFlowKey(currentRegistration.FlowKey) ||
            HasInterveningAddRegistration(guardedRegistration, currentRegistration, registrations) ||
            TryGetExecutionNode(currentRegistration) is not { } currentNode)
        {
            return false;
        }

        if (IsInsideLoop(currentNode) ||
            (guardedIf.SyntaxTree == currentNode.SyntaxTree &&
             guardedIf.Span.Contains(currentNode.SpanStart)) ||
            !HasReachablePathWithoutGuardedRegistration(
                guardedIf,
                guardedBranch,
                registrations,
                currentRegistration))
        {
            return false;
        }

        return !HasInterveningComplementExitGuard(guardedIf, guardedBranch, currentNode);
    }

    private static bool TryGetRegistrationIfBranch(
        OrderedRegistration registration,
        out IfStatementSyntax ifStatement,
        out SyntaxNode branchNode)
    {
        foreach (var location in EnumerateRegistrationGuardLocations(registration))
        {
            if (TryGetIfBranch(location, out ifStatement, out _, out branchNode))
            {
                return true;
            }
        }

        ifStatement = null!;
        branchNode = null!;
        return false;
    }

    private static IEnumerable<Location> EnumerateRegistrationGuardLocations(OrderedRegistration registration)
    {
        yield return registration.ExecutionLocation;

        if (!IsSameLocation(registration.ExecutionLocation, registration.BranchLocation))
        {
            yield return registration.BranchLocation;
        }

        if (!IsSameLocation(registration.Location, registration.ExecutionLocation) &&
            !IsSameLocation(registration.Location, registration.BranchLocation))
        {
            yield return registration.Location;
        }
    }

    private static bool HasInterveningAddRegistration(
        OrderedRegistration guardedRegistration,
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations)
    {
        var guardedIndex = FindRegistrationIndex(registrations, guardedRegistration);
        var currentIndex = FindRegistrationIndex(registrations, currentRegistration);
        if (guardedIndex < 0 || currentIndex <= guardedIndex)
        {
            return false;
        }

        for (var i = guardedIndex + 1; i < currentIndex; i++)
        {
            if (IsInterveningAddRegistration(guardedRegistration, registrations[i], currentRegistration, registrations))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInterveningAddRegistration(
        OrderedRegistration guardedRegistration,
        OrderedRegistration interveningRegistration,
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations)
    {
        if (!TryGetRegistrationIfBranch(interveningRegistration, out var interveningIf, out var interveningBranch))
        {
            return true;
        }

        if (IsInsideLoop(interveningIf) ||
            TryGetExecutionNode(currentRegistration) is not { } currentNode ||
            interveningIf.Span.Contains(currentNode.SpanStart))
        {
            return true;
        }

        if (IsComplementaryGuardedRegistration(
                guardedRegistration,
                interveningIf,
                interveningBranch))
        {
            return true;
        }

        return !HasReachablePathWithoutGuardedRegistration(
            interveningIf,
            interveningBranch,
            registrations,
            currentRegistration);
    }

    private static bool IsComplementaryGuardedRegistration(
        OrderedRegistration guardedRegistration,
        IfStatementSyntax interveningIf,
        SyntaxNode interveningBranch)
    {
        return TryGetIfBranch(guardedRegistration.Location, out var guardedIf, out _, out var guardedBranch) &&
               guardedIf.Else is null &&
               interveningIf.Else is null &&
               IsSameBranch(guardedBranch, guardedIf.Statement) &&
               IsSameBranch(interveningBranch, interveningIf.Statement) &&
               IsNegatedCondition(interveningIf.Condition, guardedIf.Condition);
    }

    private static int FindRegistrationIndex(
        List<OrderedRegistration> registrations,
        OrderedRegistration target)
    {
        for (var i = 0; i < registrations.Count; i++)
        {
            if (registrations[i].Order == target.Order &&
                registrations[i].Location == target.Location)
            {
                return i;
            }
        }

        return -1;
    }

    private enum IfBranchKind
    {
        Then,
        ElseIf,
        Else
    }

    private readonly struct IfBranchInfo
    {
        public IfBranchInfo(IfStatementSyntax ifStatement, SyntaxNode branch)
        {
            IfStatement = ifStatement;
            Branch = branch;
        }

        public IfStatementSyntax IfStatement { get; }

        public SyntaxNode Branch { get; }
    }

    private static IEnumerable<IfBranchInfo> EnumerateContainingIfBranches(Location location)
    {
        var node = TryGetNode(location);
        while (node is not null)
        {
            if (node.Parent is IfStatementSyntax parentIf &&
                parentIf.Statement.Span.Contains(node.SpanStart))
            {
                yield return new IfBranchInfo(GetOutermostIfChain(parentIf), parentIf.Statement);
                node = parentIf;
                continue;
            }

            if (node.Parent is ElseClauseSyntax parentElse &&
                parentElse.Statement.Span.Contains(node.SpanStart) &&
                parentElse.Parent is IfStatementSyntax elseOwner)
            {
                yield return new IfBranchInfo(GetOutermostIfChain(elseOwner), parentElse.Statement);
                node = elseOwner;
                continue;
            }

            node = node.Parent;
        }
    }

    private static IEnumerable<IfBranchInfo> EnumerateRegistrationContainingIfBranches(OrderedRegistration registration)
    {
        foreach (var location in EnumerateRegistrationAnalysisLocations(registration))
        {
            foreach (var branch in EnumerateContainingIfBranches(location))
            {
                yield return branch;
            }
        }
    }

    private static IEnumerable<Location> EnumerateRegistrationAnalysisLocations(OrderedRegistration registration)
    {
        yield return registration.Location;

        if (!IsSameLocation(registration.Location, registration.ExecutionLocation))
        {
            yield return registration.ExecutionLocation;
        }

        if (!IsSameLocation(registration.Location, registration.BranchLocation) &&
            !IsSameLocation(registration.ExecutionLocation, registration.BranchLocation))
        {
            yield return registration.BranchLocation;
        }
    }

    private static bool TryGetIfBranch(
        Location location,
        out IfStatementSyntax ifStatement,
        out IfBranchKind branchKind,
        out SyntaxNode branchNode)
    {
        ifStatement = null!;
        branchKind = IfBranchKind.Then;
        branchNode = null!;

        var node = TryGetNode(location);
        while (node is not null)
        {
            if (node.Parent is IfStatementSyntax parentIf &&
                parentIf.Statement.Span.Contains(node.SpanStart))
            {
                ifStatement = GetOutermostIfChain(parentIf);
                branchKind = parentIf == ifStatement
                    ? IfBranchKind.Then
                    : IfBranchKind.ElseIf;
                branchNode = parentIf.Statement;
                return true;
            }

            if (node.Parent is ElseClauseSyntax parentElse &&
                parentElse.Statement.Span.Contains(node.SpanStart) &&
                parentElse.Parent is IfStatementSyntax elseOwner)
            {
                ifStatement = GetOutermostIfChain(elseOwner);
                branchKind = IfBranchKind.Else;
                branchNode = parentElse.Statement;
                return true;
            }

            node = node.Parent;
        }

        return false;
    }

    private static IfStatementSyntax GetOutermostIfChain(IfStatementSyntax ifStatement)
    {
        var current = ifStatement;
        while (current.Parent is ElseClauseSyntax parentElse &&
               parentElse.Statement == current &&
               parentElse.Parent is IfStatementSyntax outerIf)
        {
            current = outerIf;
        }

        return current;
    }

    private static bool HasEarlierRegistrationInBranch(
        IfStatementSyntax ifStatement,
        SyntaxNode branch,
        OrderedRegistration currentRegistration,
        List<OrderedRegistration> registrations) =>
        registrations.Any(registration =>
            registration.Order < currentRegistration.Order &&
            !registration.IsTryAdd &&
            SameFlowKey(registration.FlowKey, currentRegistration.FlowKey) &&
            EnumerateRegistrationContainingIfBranches(registration).Any(registrationBranch =>
                registrationBranch.IfStatement.SyntaxTree == ifStatement.SyntaxTree &&
                registrationBranch.IfStatement.Span == ifStatement.Span &&
                IsSameBranch(registrationBranch.Branch, branch)));

    private static bool HasInterveningComplementExitGuard(
        IfStatementSyntax guardedIf,
        SyntaxNode guardedBranch,
        SyntaxNode currentNode)
    {
        if (guardedIf.Else is not null ||
            !IsSameBranch(guardedBranch, guardedIf.Statement) ||
            guardedIf.Parent is not BlockSyntax block)
        {
            return false;
        }

        var currentStatement = currentNode
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent == block);
        if (currentStatement is null)
        {
            return false;
        }

        var guardedIndex = block.Statements.IndexOf(guardedIf);
        var currentIndex = block.Statements.IndexOf(currentStatement);
        if (guardedIndex < 0 || currentIndex <= guardedIndex)
        {
            return false;
        }

        for (var i = guardedIndex + 1; i < currentIndex; i++)
        {
            if (block.Statements[i] is IfStatementSyntax candidate &&
                ExitsComplementaryPath(
                    guardedIf.Condition,
                    candidate,
                    currentNode,
                    block,
                    guardedIndex,
                    i))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExitsComplementaryPath(
        ExpressionSyntax guardedCondition,
        IfStatementSyntax candidate,
        SyntaxNode currentNode,
        BlockSyntax block,
        int guardedIndex,
        int candidateIndex)
    {
        if (IsNegatedCondition(candidate.Condition, guardedCondition) &&
            AreConditionIdentifiersStableBetween(guardedCondition, candidate.Condition, block, guardedIndex, candidateIndex) &&
            !CanCompleteNormally(candidate.Statement, currentNode))
        {
            return true;
        }

        return candidate.Else is { } elseClause &&
               AreEquivalentConditions(candidate.Condition, guardedCondition) &&
               AreConditionIdentifiersStableBetween(guardedCondition, candidate.Condition, block, guardedIndex, candidateIndex) &&
               !CanCompleteNormally(elseClause.Statement, currentNode);
    }

    private static bool AreConditionIdentifiersStableBetween(
        ExpressionSyntax guardedCondition,
        ExpressionSyntax candidateCondition,
        BlockSyntax block,
        int guardedIndex,
        int candidateIndex,
        HashSet<string>? invocationMutableIdentifiers = null)
    {
        var identifiers = new HashSet<string>(System.StringComparer.Ordinal);
        CollectConditionIdentifierNames(guardedCondition, identifiers);
        CollectConditionIdentifierNames(candidateCondition, identifiers);
        if (identifiers.Count == 0)
        {
            return true;
        }

        for (var i = guardedIndex + 1; i < candidateIndex; i++)
        {
            if (WritesAnyIdentifier(
                    block.Statements[i],
                    identifiers,
                    invocationMutableIdentifiers ?? GetIdentifiersPotentiallyMutatedByInvocation(
                        guardedCondition,
                        candidateCondition)))
            {
                return false;
            }
        }

        return true;
    }

    private static void CollectConditionIdentifierNames(ExpressionSyntax expression, HashSet<string> identifiers)
    {
        expression = StripParentheses(expression);

        if (expression is IdentifierNameSyntax identifier)
        {
            identifiers.Add(identifier.Identifier.ValueText);
            return;
        }

        if (expression is PrefixUnaryExpressionSyntax prefix &&
            prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            CollectConditionIdentifierNames(prefix.Operand, identifiers);
            return;
        }

        if (expression is BinaryExpressionSyntax binary)
        {
            CollectConditionIdentifierNames(binary.Left, identifiers);
            CollectConditionIdentifierNames(binary.Right, identifiers);
        }
    }

    private static bool WritesAnyIdentifier(
        SyntaxNode node,
        HashSet<string> identifiers,
        HashSet<string>? invocationMutableIdentifiers = null) =>
        WritesAnyIdentifierDirectly(node, identifiers) ||
        EnumerateImmediateExecutionNodes(node)
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsPotentialInvocationMutation(invocation, identifiers, invocationMutableIdentifiers));

    private static bool WritesAnyIdentifierDirectly(SyntaxNode node, HashSet<string> identifiers) =>
        EnumerateImmediateExecutionNodes(node)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => IsIdentifierWrite(assignment.Left, identifiers)) ||
        EnumerateImmediateExecutionNodes(node)
            .OfType<PrefixUnaryExpressionSyntax>()
            .Any(prefix =>
                (prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                 prefix.IsKind(SyntaxKind.PreDecrementExpression)) &&
                IsIdentifierWrite(prefix.Operand, identifiers)) ||
        EnumerateImmediateExecutionNodes(node)
            .OfType<PostfixUnaryExpressionSyntax>()
            .Any(postfix =>
                (postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                 postfix.IsKind(SyntaxKind.PostDecrementExpression)) &&
                IsIdentifierWrite(postfix.Operand, identifiers)) ||
        EnumerateImmediateExecutionNodes(node)
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                 argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                IsIdentifierWrite(argument.Expression, identifiers));

    private static IEnumerable<SyntaxNode> EnumerateImmediateExecutionNodes(SyntaxNode node) =>
        node.DescendantNodesAndSelf(descendIntoChildren: current =>
            ReferenceEquals(current, node) ||
            current is not AnonymousFunctionExpressionSyntax &&
            current is not LocalFunctionStatementSyntax);

    private static bool IsIdentifierWrite(ExpressionSyntax expression, HashSet<string> identifiers)
    {
        expression = StripParentheses(expression);
        return expression switch
        {
            IdentifierNameSyntax identifier =>
                identifiers.Contains(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax memberAccess =>
                IsThisOrBaseReceiver(memberAccess.Expression) &&
                identifiers.Contains(memberAccess.Name.Identifier.ValueText),
            _ => false
        };
    }

    private static bool IsThisOrBaseReceiver(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        return expression.IsKind(SyntaxKind.ThisExpression) ||
               expression.IsKind(SyntaxKind.BaseExpression);
    }

    private static bool IsPotentialThisMutation(InvocationExpressionSyntax invocation)
    {
        var expression = StripParentheses(invocation.Expression);
        return expression is IdentifierNameSyntax ||
               expression is MemberAccessExpressionSyntax memberAccess &&
               IsThisOrBaseReceiver(memberAccess.Expression);
    }

    private static bool IsPotentialInvocationMutation(
        InvocationExpressionSyntax invocation,
        HashSet<string> identifiers,
        HashSet<string>? invocationMutableIdentifiers)
    {
        if (invocationMutableIdentifiers is { Count: > 0 } &&
            IsPotentialThisMutation(invocation))
        {
            return true;
        }

        return IsLocalInvocationThatWritesIdentifier(invocation, identifiers);
    }

    private static bool IsLocalInvocationThatWritesIdentifier(
        InvocationExpressionSyntax invocation,
        HashSet<string> identifiers)
    {
        var expression = StripParentheses(invocation.Expression);
        if (expression is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        var name = identifier.Identifier.ValueText;
        foreach (var block in invocation.Ancestors().OfType<BlockSyntax>())
        {
            if (block.Statements
                    .OfType<LocalFunctionStatementSyntax>()
                    .Any(localFunction =>
                        localFunction.Identifier.ValueText == name &&
                        LocalFunctionWritesIdentifier(localFunction, identifiers)))
            {
                return true;
            }

            if (block.Statements
                    .OfType<LocalDeclarationStatementSyntax>()
                    .SelectMany(statement => statement.Declaration.Variables)
                    .Any(variable =>
                        variable.Identifier.ValueText == name &&
                        variable.Initializer?.Value is AnonymousFunctionExpressionSyntax lambda &&
                        LambdaWritesIdentifier(lambda, identifiers)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalFunctionWritesIdentifier(
        LocalFunctionStatementSyntax localFunction,
        HashSet<string> identifiers)
    {
        if (localFunction.Body is { } body &&
            WritesAnyIdentifierDirectly(body, identifiers))
        {
            return true;
        }

        return localFunction.ExpressionBody?.Expression is { } expression &&
               WritesAnyIdentifierDirectly(expression, identifiers);
    }

    private static bool LambdaWritesIdentifier(
        AnonymousFunctionExpressionSyntax lambda,
        HashSet<string> identifiers)
    {
        return lambda switch
        {
            ParenthesizedLambdaExpressionSyntax { Body: { } body } =>
                WritesAnyIdentifierDirectly(body, identifiers),
            SimpleLambdaExpressionSyntax { Body: { } body } =>
                WritesAnyIdentifierDirectly(body, identifiers),
            AnonymousMethodExpressionSyntax { Block: { } block } =>
                WritesAnyIdentifierDirectly(block, identifiers),
            _ => false
        };
    }

    private static HashSet<string> GetIdentifiersPotentiallyMutatedByInvocation(
        ExpressionSyntax guardedCondition,
        ExpressionSyntax candidateCondition)
    {
        var identifiers = new HashSet<string>(System.StringComparer.Ordinal);
        CollectConditionIdentifierNames(guardedCondition, identifiers);
        CollectConditionIdentifierNames(candidateCondition, identifiers);
        if (identifiers.Count == 0)
        {
            return identifiers;
        }

        var localOrParameterNames = new HashSet<string>(System.StringComparer.Ordinal);
        CollectNearestCallableParameterNames(guardedCondition, localOrParameterNames);
        CollectScopedLocalNamesBefore(guardedCondition, localOrParameterNames);

        identifiers.ExceptWith(localOrParameterNames);
        return identifiers;
    }

    private static void CollectNearestCallableParameterNames(
        SyntaxNode node,
        HashSet<string> names)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case BaseMethodDeclarationSyntax method:
                    AddParameterNames(method.ParameterList, names);
                    return;
                case LocalFunctionStatementSyntax localFunction:
                    AddParameterNames(localFunction.ParameterList, names);
                    return;
                case ParenthesizedLambdaExpressionSyntax lambda:
                    AddParameterNames(lambda.ParameterList, names);
                    return;
                case SimpleLambdaExpressionSyntax lambda:
                    names.Add(lambda.Parameter.Identifier.ValueText);
                    return;
            }
        }
    }

    private static void AddParameterNames(
        ParameterListSyntax parameterList,
        HashSet<string> names)
    {
        foreach (var parameter in parameterList.Parameters)
        {
            names.Add(parameter.Identifier.ValueText);
        }
    }

    private static void CollectScopedLocalNamesBefore(
        SyntaxNode node,
        HashSet<string> names)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current.Parent is not BlockSyntax block)
            {
                continue;
            }

            var containingStatement = current
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent == block);
            if (containingStatement is null)
            {
                continue;
            }

            var statementIndex = block.Statements.IndexOf(containingStatement);
            if (statementIndex < 0)
            {
                continue;
            }

            for (var i = 0; i < statementIndex; i++)
            {
                if (block.Statements[i] is LocalDeclarationStatementSyntax localDeclaration)
                {
                    foreach (var variable in localDeclaration.Declaration.Variables)
                    {
                        names.Add(variable.Identifier.ValueText);
                    }
                }
            }
        }
    }

    private static bool IsNegatedCondition(
        ExpressionSyntax possibleNegation,
        ExpressionSyntax condition)
    {
        var possibleNegationCore = StripParentheses(possibleNegation);
        var conditionCore = StripParentheses(condition);
        if (possibleNegationCore is PrefixUnaryExpressionSyntax prefix &&
            prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return AreEquivalentConditions(prefix.Operand, condition) ||
                   IsPositiveBooleanComparison(conditionCore, prefix.Operand);
        }

        if (possibleNegationCore is BinaryExpressionSyntax possibleBinary &&
            conditionCore is BinaryExpressionSyntax conditionBinary &&
            AreComplementaryBinaryConditions(possibleBinary, conditionBinary))
        {
            return true;
        }

        if (IsBooleanComparisonNegation(possibleNegationCore, conditionCore) ||
            IsBooleanComparisonNegation(conditionCore, possibleNegationCore))
        {
            return true;
        }

        return conditionCore is PrefixUnaryExpressionSyntax guardedPrefix &&
               guardedPrefix.IsKind(SyntaxKind.LogicalNotExpression) &&
               (AreEquivalentConditions(possibleNegation, guardedPrefix.Operand) ||
                IsPositiveBooleanComparison(possibleNegationCore, guardedPrefix.Operand));
    }

    private static bool AreOppositeEqualityKinds(SyntaxKind left, SyntaxKind right) =>
        (left == SyntaxKind.EqualsExpression && right == SyntaxKind.NotEqualsExpression) ||
        (left == SyntaxKind.NotEqualsExpression && right == SyntaxKind.EqualsExpression);

    private static bool AreComplementaryBinaryConditions(
        BinaryExpressionSyntax left,
        BinaryExpressionSyntax right) =>
        (AreOppositeEqualityKinds(left.Kind(), right.Kind()) &&
         AreEquivalentBinaryOperands(left, right)) ||
        AreOppositeRelationalBinaryConditions(left, right);

    private static bool AreOppositeRelationalBinaryConditions(
        BinaryExpressionSyntax left,
        BinaryExpressionSyntax right)
    {
        if (AreEquivalentConditions(left.Left, right.Left) &&
            AreEquivalentConditions(left.Right, right.Right))
        {
            return AreOppositeRelationalKinds(left.Kind(), right.Kind());
        }

        return AreEquivalentConditions(left.Left, right.Right) &&
               AreEquivalentConditions(left.Right, right.Left) &&
               TryInvertRelationalKind(right.Kind(), out var invertedRightKind) &&
               AreOppositeRelationalKinds(left.Kind(), invertedRightKind);
    }

    private static bool AreOppositeRelationalKinds(SyntaxKind left, SyntaxKind right) =>
        (left == SyntaxKind.LessThanExpression && right == SyntaxKind.GreaterThanOrEqualExpression) ||
        (left == SyntaxKind.GreaterThanOrEqualExpression && right == SyntaxKind.LessThanExpression) ||
        (left == SyntaxKind.LessThanOrEqualExpression && right == SyntaxKind.GreaterThanExpression) ||
        (left == SyntaxKind.GreaterThanExpression && right == SyntaxKind.LessThanOrEqualExpression);

    private static bool TryInvertRelationalKind(SyntaxKind kind, out SyntaxKind invertedKind)
    {
        invertedKind = kind switch
        {
            SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
            _ => SyntaxKind.None
        };

        return invertedKind != SyntaxKind.None;
    }

    private static bool AreEquivalentBinaryOperands(
        BinaryExpressionSyntax left,
        BinaryExpressionSyntax right) =>
        (AreEquivalentConditions(left.Left, right.Left) &&
         AreEquivalentConditions(left.Right, right.Right)) ||
        (AreEquivalentConditions(left.Left, right.Right) &&
         AreEquivalentConditions(left.Right, right.Left));

    private static bool IsBooleanComparisonNegation(
        ExpressionSyntax possibleComparison,
        ExpressionSyntax condition)
    {
        if (possibleComparison is not BinaryExpressionSyntax binary ||
            !TryGetBooleanComparisonOperand(binary, out var comparedExpression, out var literalValue))
        {
            return false;
        }

        var comparisonIsNegated =
            (binary.IsKind(SyntaxKind.EqualsExpression) && !literalValue) ||
            (binary.IsKind(SyntaxKind.NotEqualsExpression) && literalValue);

        return comparisonIsNegated &&
               AreEquivalentConditions(comparedExpression, condition);
    }

    private static bool IsPositiveBooleanComparison(
        ExpressionSyntax possibleComparison,
        ExpressionSyntax condition)
    {
        if (possibleComparison is not BinaryExpressionSyntax binary ||
            !TryGetBooleanComparisonOperand(binary, out var comparedExpression, out var literalValue))
        {
            return false;
        }

        var comparisonIsPositive =
            (binary.IsKind(SyntaxKind.EqualsExpression) && literalValue) ||
            (binary.IsKind(SyntaxKind.NotEqualsExpression) && !literalValue);

        return comparisonIsPositive &&
               AreEquivalentConditions(comparedExpression, condition);
    }

    private static bool TryGetBooleanComparisonOperand(
        BinaryExpressionSyntax binary,
        out ExpressionSyntax comparedExpression,
        out bool literalValue)
    {
        if (binary.IsKind(SyntaxKind.EqualsExpression) ||
            binary.IsKind(SyntaxKind.NotEqualsExpression))
        {
            if (TryGetBooleanLiteral(binary.Left, out literalValue))
            {
                comparedExpression = binary.Right;
                return true;
            }

            if (TryGetBooleanLiteral(binary.Right, out literalValue))
            {
                comparedExpression = binary.Left;
                return true;
            }
        }

        comparedExpression = binary;
        literalValue = false;
        return false;
    }

    private static bool TryGetBooleanLiteral(ExpressionSyntax expression, out bool value)
    {
        if (IsTrueLiteral(expression))
        {
            value = true;
            return true;
        }

        if (IsFalseLiteral(expression))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool AreEquivalentConditions(
        ExpressionSyntax left,
        ExpressionSyntax right) =>
        IsStableCondition(left) &&
        IsStableCondition(right) &&
        string.Equals(
            StripParentheses(left).WithoutTrivia().ToString(),
            StripParentheses(right).WithoutTrivia().ToString(),
            System.StringComparison.Ordinal);

    private static bool IsStableCondition(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);

        if (expression is LiteralExpressionSyntax ||
            expression is IdentifierNameSyntax)
        {
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax prefix &&
            prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return IsStableCondition(prefix.Operand);
        }

        if (expression is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.LogicalAndExpression) ||
             binary.IsKind(SyntaxKind.LogicalOrExpression) ||
             binary.IsKind(SyntaxKind.EqualsExpression) ||
             binary.IsKind(SyntaxKind.NotEqualsExpression) ||
             binary.IsKind(SyntaxKind.LessThanExpression) ||
             binary.IsKind(SyntaxKind.LessThanOrEqualExpression) ||
             binary.IsKind(SyntaxKind.GreaterThanExpression) ||
             binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression)))
        {
            return IsStableCondition(binary.Left) && IsStableCondition(binary.Right);
        }

        return false;
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasReachablePathWithoutGuardedRegistration(
        IfStatementSyntax ifStatement,
        SyntaxNode guardedBranch,
        List<OrderedRegistration> registrations,
        OrderedRegistration beforeRegistration)
    {
        var beforeNode = TryGetFallbackCompletionNode(beforeRegistration, ifStatement);
        foreach (var branch in EnumerateIfChainBranches(ifStatement))
        {
            if (IsSameBranch(branch, guardedBranch) ||
                !CanCompleteNormally(branch, beforeNode) ||
                HasPriorRegistrationInBranch(ifStatement, branch, registrations, beforeRegistration, beforeNode))
            {
                continue;
            }

            return true;
        }

        return GetFinalElseClause(ifStatement) is null &&
               !IsKnownTrueCondition(ifStatement.Condition, ifStatement) &&
               !HasKnownTrueElseIfCondition(ifStatement) &&
               !HasComplementaryElseIfCondition(ifStatement);
    }

    private static SyntaxNode? TryGetFallbackCompletionNode(
        OrderedRegistration beforeRegistration,
        IfStatementSyntax guardedIf)
    {
        var registrationNode = TryGetNode(beforeRegistration.Location);
        if (registrationNode is not null &&
            GetReturnScope(registrationNode) is { } registrationScope &&
            GetReturnScope(guardedIf) is { } guardedScope &&
            IsSameSyntaxNode(registrationScope, guardedScope))
        {
            return registrationNode;
        }

        return TryGetExecutionNode(beforeRegistration);
    }

    private static bool HasKnownTrueElseIfCondition(IfStatementSyntax ifStatement)
    {
        var current = ifStatement.Else?.Statement as IfStatementSyntax;
        while (current is not null)
        {
            if (IsKnownTrueCondition(current.Condition, current))
            {
                return true;
            }

            current = current.Else?.Statement as IfStatementSyntax;
        }

        return false;
    }

    private static bool HasComplementaryElseIfCondition(IfStatementSyntax ifStatement)
    {
        var priorConditions = new List<ExpressionSyntax> { ifStatement.Condition };
        var current = ifStatement.Else?.Statement as IfStatementSyntax;
        while (current is not null)
        {
            if (priorConditions.Any(condition =>
                    IsStableCondition(condition) &&
                    IsStableCondition(current.Condition) &&
                    IsNegatedCondition(current.Condition, condition)))
            {
                return true;
            }

            priorConditions.Add(current.Condition);
            current = current.Else?.Statement as IfStatementSyntax;
        }

        return false;
    }

    private static IEnumerable<SyntaxNode> EnumerateIfChainBranches(IfStatementSyntax ifStatement)
    {
        var current = ifStatement;
        while (true)
        {
            yield return current.Statement;

            if (current.Else is not { } elseClause)
            {
                yield break;
            }

            if (elseClause.Statement is not IfStatementSyntax nextIf)
            {
                yield return elseClause.Statement;
                yield break;
            }

            current = nextIf;
        }
    }

    private static bool HasPriorRegistrationInBranch(
        IfStatementSyntax ifStatement,
        SyntaxNode branch,
        List<OrderedRegistration> registrations,
        OrderedRegistration beforeRegistration,
        SyntaxNode? beforeNode)
    {
        var registrationNodes = new List<SyntaxNode>();
        foreach (var registration in registrations)
        {
            if (registration.Order >= beforeRegistration.Order)
            {
                continue;
            }

            if (!SameFlowKey(registration.FlowKey, beforeRegistration.FlowKey))
            {
                continue;
            }

            foreach (var location in EnumerateRegistrationAnalysisLocations(registration))
            {
                if (TryGetNode(location) is { } registrationNode &&
                    !registrationNodes.Any(existing => IsSameSyntaxNode(existing, registrationNode)))
                {
                    registrationNodes.Add(registrationNode);
                }
            }
        }

        if (registrationNodes.Any(registrationNode => IsPriorRegistrationInBranch(registrationNode, branch)))
        {
            return true;
        }

        return IsBranchCoveredByPriorRegistrations(branch, registrationNodes, beforeNode);
    }

    private static bool IsBranchCoveredByPriorRegistrations(
        SyntaxNode branch,
        IReadOnlyCollection<SyntaxNode> registrationNodes,
        SyntaxNode? beforeNode)
    {
        return branch switch
        {
            BlockSyntax block => IsStatementSequenceCoveredByPriorRegistrations(
                block.Statements,
                registrationNodes,
                beforeNode),
            StatementSyntax statement => IsStatementCoveredByPriorRegistrations(
                statement,
                registrationNodes,
                beforeNode),
            _ => false
        };
    }

    private static bool IsStatementSequenceCoveredByPriorRegistrations(
        SyntaxList<StatementSyntax> statements,
        IReadOnlyCollection<SyntaxNode> registrationNodes,
        SyntaxNode? beforeNode)
    {
        foreach (var statement in statements)
        {
            if (IsStatementCoveredByPriorRegistrations(statement, registrationNodes, beforeNode))
            {
                return true;
            }

            if (!CanCompleteNormally(statement, beforeNode))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsStatementCoveredByPriorRegistrations(
        StatementSyntax statement,
        IReadOnlyCollection<SyntaxNode> registrationNodes,
        SyntaxNode? beforeNode)
    {
        if (registrationNodes.Any(registrationNode => IsDirectRegistrationInStatement(registrationNode, statement)))
        {
            return true;
        }

        return statement switch
        {
            BlockSyntax block => IsStatementSequenceCoveredByPriorRegistrations(
                block.Statements,
                registrationNodes,
                beforeNode),
            IfStatementSyntax ifStatement => IsIfStatementCoveredByPriorRegistrations(
                ifStatement,
                registrationNodes,
                beforeNode),
            _ => false
        };
    }

    private static bool IsIfStatementCoveredByPriorRegistrations(
        IfStatementSyntax ifStatement,
        IReadOnlyCollection<SyntaxNode> registrationNodes,
        SyntaxNode? beforeNode)
    {
        foreach (var branch in EnumerateIfChainBranches(ifStatement))
        {
            if (!CanCompleteNormally(branch, beforeNode))
            {
                continue;
            }

            if (!IsBranchCoveredByPriorRegistrations(branch, registrationNodes, beforeNode))
            {
                return false;
            }
        }

        return GetFinalElseClause(ifStatement) is not null ||
               IsKnownTrueCondition(ifStatement.Condition, ifStatement) ||
               HasKnownTrueElseIfCondition(ifStatement) ||
               HasComplementaryElseIfCondition(ifStatement);
    }

    private static bool IsDirectRegistrationInStatement(
        SyntaxNode registrationNode,
        StatementSyntax statement)
    {
        if (registrationNode.SyntaxTree != statement.SyntaxTree ||
            !statement.Span.Contains(registrationNode.SpanStart))
        {
            return false;
        }

        for (var current = registrationNode; current is not null; current = current.Parent)
        {
            if (IsSameSyntaxNode(current, statement))
            {
                return true;
            }

            if (current.Parent is IfStatementSyntax ||
                current.Parent is ElseClauseSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsPriorRegistrationInBranch(SyntaxNode registrationNode, SyntaxNode branch)
    {
        if (registrationNode.SyntaxTree != branch.SyntaxTree ||
            !branch.Span.Contains(registrationNode.SpanStart))
        {
            return false;
        }

        for (var current = registrationNode; current is not null && !IsSameBranch(current, branch); current = current.Parent)
        {
            if (current.Parent is IfStatementSyntax parentIf)
            {
                if (!IsSameBranch(current, parentIf.Statement) ||
                    !IsKnownTrueCondition(parentIf.Condition, parentIf))
                {
                    return false;
                }
            }
            else if (current.Parent is ElseClauseSyntax)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSameBranch(SyntaxNode left, SyntaxNode right)
    {
        return left.SyntaxTree == right.SyntaxTree &&
               left.Span == right.Span;
    }

    private static bool IsSameLocation(Location left, Location right) =>
        left.SourceTree == right.SourceTree &&
        left.SourceSpan == right.SourceSpan;

    private static bool SameFlowKey(string? left, string? right) =>
        string.Equals(NormalizeFlowKey(left), NormalizeFlowKey(right), System.StringComparison.Ordinal);

    private static string? NormalizeFlowKey(string? flowKey) =>
        ServiceCollectionReachabilityAnalyzer.NormalizeLoopedWrapperFlowKey(flowKey);

    private static ElseClauseSyntax? GetFinalElseClause(IfStatementSyntax ifStatement)
    {
        var current = ifStatement;
        while (current.Else is { } elseClause)
        {
            if (elseClause.Statement is not IfStatementSyntax nextIf)
            {
                return elseClause;
            }

            current = nextIf;
        }

        return null;
    }

    private static bool CanCompleteNormally(SyntaxNode statement, SyntaxNode? completionTarget = null)
    {
        if (statement is BlockSyntax block)
        {
            return CanStatementSequenceCompleteNormally(block.Statements, 0, completionTarget, breakCompletesNormally: false);
        }

        if (statement is LabeledStatementSyntax labeledStatement)
        {
            return CanCompleteNormally(labeledStatement.Statement, completionTarget);
        }

        if (statement is IfStatementSyntax ifStatement)
        {
            if (ifStatement.Else is null)
            {
                return !IsKnownTrueCondition(ifStatement.Condition, ifStatement) ||
                       CanCompleteNormally(ifStatement.Statement, completionTarget);
            }

            return CanCompleteNormally(ifStatement.Statement, completionTarget) ||
                   CanCompleteNormally(ifStatement.Else.Statement, completionTarget);
        }

        if (statement is UsingStatementSyntax usingStatement)
        {
            return CanCompleteNormally(usingStatement.Statement, completionTarget);
        }

        if (statement is LockStatementSyntax lockStatement)
        {
            return CanCompleteNormally(lockStatement.Statement, completionTarget);
        }

        if (statement is FixedStatementSyntax fixedStatement)
        {
            return CanCompleteNormally(fixedStatement.Statement, completionTarget);
        }

        if (statement is CheckedStatementSyntax checkedStatement)
        {
            return CanCompleteNormally(checkedStatement.Block, completionTarget);
        }

        if (statement is UnsafeStatementSyntax unsafeStatement)
        {
            return CanCompleteNormally(unsafeStatement.Block, completionTarget);
        }

        if (statement is TryStatementSyntax tryStatement)
        {
            if (tryStatement.Finally is { } finallyClause &&
                !CanCompleteNormally(finallyClause.Block, completionTarget))
            {
                return false;
            }

            return CanCompleteNormally(tryStatement.Block, completionTarget) ||
                   tryStatement.Catches.Any(catchClause => CanCompleteNormally(catchClause.Block, completionTarget));
        }

        if (statement is SwitchStatementSyntax switchStatement)
        {
            return !switchStatement.Sections.Any(section =>
                       section.Labels.Any(label => label.IsKind(SyntaxKind.DefaultSwitchLabel))) ||
                   switchStatement.Sections.Any(section => CanSwitchSectionCompleteNormally(section, completionTarget));
        }

        if (statement is WhileStatementSyntax whileStatement &&
            IsTrueLiteral(whileStatement.Condition) &&
            !ContainsBreakForStatement(whileStatement))
        {
            return ContainsGotoForCompletionTarget(whileStatement.Statement, completionTarget);
        }

        if (statement is ForStatementSyntax forStatement &&
            (forStatement.Condition is null || IsTrueLiteral(forStatement.Condition)) &&
            !ContainsBreakForStatement(forStatement))
        {
            return ContainsGotoForCompletionTarget(forStatement.Statement, completionTarget);
        }

        if (statement is DoStatementSyntax doStatement &&
            IsTrueLiteral(doStatement.Condition) &&
            !ContainsBreakForStatement(doStatement))
        {
            return ContainsGotoForCompletionTarget(doStatement.Statement, completionTarget);
        }

        if (statement is GotoStatementSyntax gotoStatement)
        {
            return GotoReachesCompletionTarget(gotoStatement, completionTarget);
        }

        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            return CanExpressionCompleteNormally(expressionStatement.Expression);
        }

        if (statement is ReturnStatementSyntax returnStatement)
        {
            return ReturnsToDifferentCompletionScope(returnStatement, completionTarget);
        }

        return statement is not ReturnStatementSyntax &&
               statement is not ThrowStatementSyntax &&
               statement is not ContinueStatementSyntax &&
               statement is not BreakStatementSyntax;
    }

    private static bool ReturnsToDifferentCompletionScope(
        ReturnStatementSyntax returnStatement,
        SyntaxNode? completionTarget)
    {
        if (completionTarget is null)
        {
            return false;
        }

        var returnScope = GetReturnScope(returnStatement);
        var targetScope = GetReturnScope(completionTarget);
        return returnScope is not null &&
               targetScope is not null &&
               !IsSameSyntaxNode(returnScope, targetScope);
    }

    private static SyntaxNode? GetReturnScope(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate is BaseMethodDeclarationSyntax ||
            candidate is LocalFunctionStatementSyntax ||
            candidate is AccessorDeclarationSyntax ||
            candidate is AnonymousFunctionExpressionSyntax);

    private static bool IsSameSyntaxNode(SyntaxNode left, SyntaxNode right) =>
        left.SyntaxTree == right.SyntaxTree &&
        left.Span == right.Span;

    private static bool CanExpressionCompleteNormally(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);

        if (expression is ThrowExpressionSyntax)
        {
            return false;
        }

        if (expression is AssignmentExpressionSyntax assignment)
        {
            return CanExpressionCompleteNormally(assignment.Right);
        }

        if (expression is ConditionalExpressionSyntax conditional)
        {
            if (IsTrueLiteral(conditional.Condition))
            {
                return CanExpressionCompleteNormally(conditional.WhenTrue);
            }

            if (IsFalseLiteral(conditional.Condition))
            {
                return CanExpressionCompleteNormally(conditional.WhenFalse);
            }

            return CanExpressionCompleteNormally(conditional.WhenTrue) ||
                   CanExpressionCompleteNormally(conditional.WhenFalse);
        }

        if (expression is SwitchExpressionSyntax switchExpression)
        {
            return switchExpression.Arms.Count == 0 ||
                   switchExpression.Arms.Any(arm => CanExpressionCompleteNormally(arm.Expression));
        }

        return true;
    }

    private static bool CanSwitchSectionCompleteNormally(SwitchSectionSyntax section, SyntaxNode? completionTarget)
    {
        if (section.Statements.Count == 0)
        {
            return true;
        }

        return CanStatementSequenceCompleteNormally(section.Statements, 0, completionTarget, breakCompletesNormally: true);
    }

    private static bool GotoReachesCompletionTarget(GotoStatementSyntax gotoStatement, SyntaxNode? completionTarget)
    {
        if (completionTarget is null ||
            gotoStatement.Expression is not IdentifierNameSyntax labelName)
        {
            return false;
        }

        var labelScope = GetLabelScope(gotoStatement);
        var label = labelScope
            .DescendantNodes()
            .OfType<LabeledStatementSyntax>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Identifier.ValueText, labelName.Identifier.ValueText, System.StringComparison.Ordinal) &&
                ReferenceEquals(GetLabelScope(candidate), labelScope));

        return label is not null &&
               label.SpanStart <= completionTarget.SpanStart &&
               CanCompleteFromLabelToTarget(label, completionTarget);
    }

    private static bool ContainsGotoForCompletionTarget(SyntaxNode node, SyntaxNode? completionTarget) =>
        completionTarget is not null &&
        EnumerateImmediateExecutionNodes(node)
            .OfType<GotoStatementSyntax>()
            .Any(gotoStatement => GotoReachesCompletionTarget(gotoStatement, completionTarget));

    private static bool CanCompleteFromLabelToTarget(LabeledStatementSyntax label, SyntaxNode completionTarget)
    {
        if (label.Parent is BlockSyntax block)
        {
            var index = block.Statements.IndexOf(label);
            return index >= 0 &&
                   CanStatementSequenceCompleteNormally(block.Statements, index, completionTarget, breakCompletesNormally: false);
        }

        if (label.Parent is SwitchSectionSyntax section)
        {
            var index = section.Statements.IndexOf(label);
            return index >= 0 &&
                   CanStatementSequenceCompleteNormally(section.Statements, index, completionTarget, breakCompletesNormally: true);
        }

        return CanCompleteNormally(label.Statement, completionTarget);
    }

    private static bool CanStatementSequenceCompleteNormally(
        SyntaxList<StatementSyntax> statements,
        int startIndex,
        SyntaxNode? completionTarget,
        bool breakCompletesNormally)
    {
        for (var i = startIndex; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (completionTarget is not null &&
                statement.SpanStart >= completionTarget.SpanStart)
            {
                return true;
            }

            if (statement is LocalFunctionStatementSyntax)
            {
                continue;
            }

            if (breakCompletesNormally &&
                statement is BreakStatementSyntax)
            {
                return true;
            }

            if (!CanCompleteNormally(statement, completionTarget))
            {
                return false;
            }
        }

        return true;
    }

    private static SyntaxNode GetLabelScope(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(IsLabelScope) ?? node.SyntaxTree.GetRoot();

    private static bool IsLabelScope(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax ||
        node is LocalFunctionStatementSyntax ||
        node is AccessorDeclarationSyntax ||
        node is AnonymousFunctionExpressionSyntax;

    private static bool IsTrueLiteral(ExpressionSyntax expression) =>
        StripParentheses(expression).IsKind(SyntaxKind.TrueLiteralExpression);

    private static bool IsFalseLiteral(ExpressionSyntax expression) =>
        StripParentheses(expression).IsKind(SyntaxKind.FalseLiteralExpression);

    private static bool IsKnownTrueCondition(ExpressionSyntax expression, IfStatementSyntax ifStatement)
    {
        expression = StripParentheses(expression);
        if (IsTrueLiteral(expression))
        {
            return true;
        }

        return expression is IdentifierNameSyntax identifier &&
               TryFindPriorConstBoolValue(
                   identifier.Identifier.ValueText,
                   ifStatement,
                   out var value) &&
               value;
    }

    private static bool TryFindPriorConstBoolValue(
        string identifierName,
        IfStatementSyntax ifStatement,
        out bool value)
    {
        value = false;
        SyntaxNode currentNode = ifStatement;
        while (TryGetContainingBlockStatement(currentNode, out var block, out var statementIndex))
        {
            if (TryFindPriorConstBoolValueInBlock(identifierName, block, statementIndex, out value))
            {
                return true;
            }

            currentNode = block;
        }

        return false;
    }

    private static bool TryFindPriorConstBoolValueInBlock(
        string identifierName,
        BlockSyntax block,
        int statementIndex,
        out bool value)
    {
        value = false;
        for (var i = statementIndex - 1; i >= 0; i--)
        {
            if (block.Statements[i] is not LocalDeclarationStatementSyntax localDeclaration ||
                !localDeclaration.Modifiers.Any(token => token.IsKind(SyntaxKind.ConstKeyword)) ||
                !IsBoolType(localDeclaration.Declaration.Type))
            {
                continue;
            }

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (!string.Equals(variable.Identifier.ValueText, identifierName, System.StringComparison.Ordinal) ||
                    variable.Initializer is null)
                {
                    continue;
                }

                if (IsTrueLiteral(variable.Initializer.Value))
                {
                    value = true;
                    return true;
                }

                if (IsFalseLiteral(variable.Initializer.Value))
                {
                    value = false;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetContainingBlockStatement(
        SyntaxNode node,
        out BlockSyntax block,
        out int statementIndex)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is StatementSyntax statement &&
                statement.Parent is BlockSyntax parentBlock)
            {
                var index = parentBlock.Statements.IndexOf(statement);
                if (index >= 0)
                {
                    block = parentBlock;
                    statementIndex = index;
                    return true;
                }
            }
        }

        block = null!;
        statementIndex = -1;
        return false;
    }

    private static bool IsBoolType(TypeSyntax type) =>
        type is PredefinedTypeSyntax predefinedType &&
        predefinedType.Keyword.IsKind(SyntaxKind.BoolKeyword);

    private static bool ContainsBreakForStatement(StatementSyntax statement) =>
        statement
            .DescendantNodes()
            .OfType<BreakStatementSyntax>()
            .Any(breakStatement => TargetsStatement(breakStatement, statement));

    private static bool TargetsStatement(BreakStatementSyntax breakStatement, StatementSyntax statement)
    {
        for (var current = breakStatement.Parent; current is not null; current = current.Parent)
        {
            if (current == statement)
            {
                return true;
            }

            if (current is ForStatementSyntax ||
                current is ForEachStatementSyntax ||
                current is ForEachVariableStatementSyntax ||
                current is WhileStatementSyntax ||
                current is DoStatementSyntax ||
                current is SwitchStatementSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is ForStatementSyntax ||
                current is ForEachStatementSyntax ||
                current is ForEachVariableStatementSyntax ||
                current is WhileStatementSyntax ||
                current is DoStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? TryGetNode(Location location)
    {
        if (location.SourceTree is null)
        {
            return null;
        }

        var root = location.SourceTree.GetRoot();
        return root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
    }

    private static SyntaxNode? TryGetExecutionNode(OrderedRegistration registration) =>
        TryGetNode(registration.ExecutionLocation);

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
