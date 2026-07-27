using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Tracks registrations that are provably removed by a later, unconditional mutation in the same
/// straight-line service-collection flow.
/// </summary>
internal sealed class DefinitelyRemovedRegistrationSet
{
    private readonly Compilation _compilation;
    private readonly HashSet<ServiceRegistration> _registrations = new();
    private readonly HashSet<ServiceRegistration> _reactivatedRegistrations = new();

    private DefinitelyRemovedRegistrationSet(Compilation compilation)
    {
        _compilation = compilation;
    }

    public static DefinitelyRemovedRegistrationSet Create(
        Compilation compilation,
        IEnumerable<ServiceRegistration> registrations,
        IEnumerable<ServiceRegistration> registrationCandidates,
        IEnumerable<OrderedRegistrationMutation> mutations)
    {
        var removedRegistrations = new DefinitelyRemovedRegistrationSet(compilation);
        var originalRegistrations = new HashSet<ServiceRegistration>(registrations);
        var registrationsByTree = registrationCandidates
            .Where(static registration =>
                registration.FlowKey is not null &&
                registration.Location.SourceTree is not null)
            .GroupBy(static registration => registration.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static registration => registration.Location.SourceSpan.Start)
                    .ThenBy(static registration => registration.Location.SourceSpan.End)
                    .ThenBy(static registration => registration.Order)
                    .ToArray());
        var mutationsByTree = mutations
            .Where(static mutation =>
                mutation.FlowKey is not null &&
                mutation.Location.SourceTree is not null)
            .GroupBy(static mutation => mutation.Location.SourceTree!)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static mutation => mutation.Location.SourceSpan.Start)
                    .ThenBy(static mutation => mutation.Location.SourceSpan.End)
                    .ThenBy(static mutation => mutation.Order)
                    .ToArray());

        foreach (var tree in registrationsByTree.Keys.Union(mutationsByTree.Keys))
        {
            registrationsByTree.TryGetValue(tree, out var orderedRegistrations);
            mutationsByTree.TryGetValue(tree, out var orderedMutations);
            orderedRegistrations ??= [];
            orderedMutations ??= [];

            var activeRegistrations = new List<ServiceRegistration>();
            var possibleRemovals = new List<OrderedRegistrationMutation>();
            var registrationIndex = 0;
            var mutationIndex = 0;

            while (registrationIndex < orderedRegistrations.Length ||
                   mutationIndex < orderedMutations.Length)
            {
                if (registrationIndex < orderedRegistrations.Length &&
                    (mutationIndex >= orderedMutations.Length ||
                     Compare(
                         orderedRegistrations[registrationIndex],
                         orderedMutations[mutationIndex]) < 0))
                {
                    removedRegistrations.ApplyRegistration(
                        orderedRegistrations[registrationIndex],
                        activeRegistrations,
                        originalRegistrations,
                        possibleRemovals);
                    registrationIndex++;
                    continue;
                }

                var mutation = orderedMutations[mutationIndex];
                if (CanTreatMutationAsDefinite(mutation))
                {
                    removedRegistrations.ApplyMutation(mutation, activeRegistrations);
                }
                else if (mutation.Kind is
                         RegistrationMutationKind.Clear or
                         RegistrationMutationKind.RemoveAll)
                {
                    possibleRemovals.Add(mutation);
                }

                mutationIndex++;
            }
        }

        return removedRegistrations;
    }

    public bool Contains(ServiceRegistration registration) => _registrations.Contains(registration);

    public bool IsReactivated(ServiceRegistration registration) =>
        _reactivatedRegistrations.Contains(registration);

    public IReadOnlyList<ServiceRegistration> GetEffectiveRegistrations(
        IEnumerable<ServiceRegistration> registrations,
        IEnumerable<ServiceRegistration> registrationCandidates)
    {
        var originalRegistrations = new HashSet<ServiceRegistration>(registrations);
        return registrationCandidates
            .Where(registration =>
                (originalRegistrations.Contains(registration) ||
                 IsReactivated(registration)) &&
                !Contains(registration))
            .ToList();
    }

    private static int Compare(
        ServiceRegistration registration,
        OrderedRegistrationMutation mutation)
    {
        var byStart = registration.Location.SourceSpan.Start.CompareTo(
            mutation.Location.SourceSpan.Start);
        if (byStart != 0)
        {
            return byStart;
        }

        var byEnd = registration.Location.SourceSpan.End.CompareTo(
            mutation.Location.SourceSpan.End);
        return byEnd != 0 ? byEnd : registration.Order.CompareTo(mutation.Order);
    }

    private void ApplyRegistration(
        ServiceRegistration registration,
        List<ServiceRegistration> activeRegistrations,
        HashSet<ServiceRegistration> originalRegistrations,
        List<OrderedRegistrationMutation> possibleRemovals)
    {
        if (!registration.SkipIfSameImplementationAlreadyRegistered &&
            (registration.SkipIfAlreadyRegistered || registration.IsTryAdd) &&
            activeRegistrations.Any(active => HasSameServiceIdentifier(active, registration)) &&
            !possibleRemovals.Any(mutation =>
                CanPotentiallyReactivate(mutation, registration)))
        {
            return;
        }

        if (registration.SkipIfSameImplementationAlreadyRegistered &&
            activeRegistrations.Any(active =>
                HasSameServiceIdentifier(active, registration) &&
                SymbolEqualityComparer.Default.Equals(
                    active.ImplementationType,
                    registration.ImplementationType)) &&
            !possibleRemovals.Any(mutation =>
                CanPotentiallyReactivate(mutation, registration)))
        {
            return;
        }

        if (registration.PrependToCollection)
        {
            activeRegistrations.Insert(0, registration);
        }
        else
        {
            activeRegistrations.Add(registration);
        }

        if (!originalRegistrations.Contains(registration))
        {
            _reactivatedRegistrations.Add(registration);
        }

        possibleRemovals.RemoveAll(mutation =>
            CanDefinitelySupersede(mutation, registration));
    }

    private static bool HasSameServiceIdentifier(
        ServiceRegistration left,
        ServiceRegistration right) =>
        string.Equals(left.FlowKey, right.FlowKey, StringComparison.Ordinal) &&
        SymbolEqualityComparer.Default.Equals(left.ServiceType, right.ServiceType) &&
        left.IsKeyed == right.IsKeyed &&
        Equals(left.Key, right.Key) &&
        IsSameExecutionScope(left.Location, right.Location);

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
                case BinaryExpressionSyntax binaryExpression
                    when IsShortCircuiting(binaryExpression) &&
                         binaryExpression.Right.Span.Contains(node.Span):
                case AssignmentExpressionSyntax assignmentExpression
                    when assignmentExpression.IsKind(
                             SyntaxKind.CoalesceAssignmentExpression) &&
                         assignmentExpression.Right.Span.Contains(node.Span):
                    return false;
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case CompilationUnitSyntax:
                    return true;
            }
        }

        return false;
    }

    private void ApplyMutation(
        OrderedRegistrationMutation mutation,
        List<ServiceRegistration> activeRegistrations)
    {
        if (mutation.Kind is
            RegistrationMutationKind.Clear or
            RegistrationMutationKind.RemoveAll)
        {
            for (var i = activeRegistrations.Count - 1; i >= 0; i--)
            {
                if (!CanMutationRemoveRegistration(mutation, activeRegistrations[i]))
                {
                    continue;
                }

                Add(activeRegistrations[i]);
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

            Add(activeRegistrations[i]);
            activeRegistrations.RemoveAt(i);
            return;
        }
    }

    private static bool CanMutationRemoveRegistration(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration) =>
        CanMutationTargetRegistration(mutation, registration) &&
        IsSameExecutionScope(mutation.Location, registration.Location) &&
        IsSameStraightLineBlock(mutation.Location, registration.Location);

    private static bool CanMutationTargetRegistration(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration) =>
        string.Equals(mutation.FlowKey, registration.FlowKey, StringComparison.Ordinal) &&
        (mutation.Kind == RegistrationMutationKind.Clear ||
         (SymbolEqualityComparer.Default.Equals(mutation.ServiceType, registration.ServiceType) &&
          mutation.IsKeyed == registration.IsKeyed &&
          Equals(mutation.Key, registration.Key)));

    private bool CanPotentiallyReactivate(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration)
    {
        if (mutation.Kind is not (
                RegistrationMutationKind.Clear or
                RegistrationMutationKind.RemoveAll) ||
            !CanMutationTargetRegistration(mutation, registration) ||
            !IsSameExecutionScope(mutation.Location, registration.Location))
        {
            return false;
        }

        var mutationStatement = GetEnclosingStatement(mutation.Location);
        var registrationStatement = GetEnclosingStatement(registration.Location);
        if (mutationStatement is null ||
            registrationStatement is null)
        {
            return false;
        }

        if (CanReachLaterStatementInSameBlock(
                mutationStatement,
                registrationStatement))
        {
            return true;
        }

        if (CanFinallyRegistrationSupersede(
                mutationStatement,
                registrationStatement))
        {
            return true;
        }

        var conditionalContainers = mutationStatement
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .Where(static statement =>
                statement is IfStatementSyntax or
                    SwitchStatementSyntax or
                    TryStatementSyntax or
                    ForStatementSyntax or
                    ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or
                    WhileStatementSyntax or
                    DoStatementSyntax);
        foreach (var conditionalContainer in conditionalContainers)
        {
            if (conditionalContainer.Parent is not BlockSyntax containingBlock)
            {
                continue;
            }

            var followingRegistration = registrationStatement
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement =>
                    ReferenceEquals(statement.Parent, containingBlock));
            if (followingRegistration is null)
            {
                continue;
            }

            var conditionalIndex = containingBlock.Statements.IndexOf(conditionalContainer);
            var registrationIndex = containingBlock.Statements.IndexOf(followingRegistration);
            if (conditionalIndex >= 0 &&
                registrationIndex > conditionalIndex &&
                CanReachFollowingStatement(
                    mutationStatement,
                    conditionalContainer,
                    followingRegistration))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanFinallyRegistrationSupersede(
        StatementSyntax mutationStatement,
        StatementSyntax registrationStatement)
    {
        foreach (var tryStatement in mutationStatement
                     .Ancestors()
                     .OfType<TryStatementSyntax>())
        {
            if (tryStatement.Finally?.Block is not { } finallyBlock ||
                registrationStatement.Parent is not BlockSyntax registrationBlock ||
                registrationBlock.SyntaxTree != finallyBlock.SyntaxTree ||
                registrationBlock.Span != finallyBlock.Span)
            {
                continue;
            }

            var registrationIndex =
                registrationBlock.Statements.IndexOf(registrationStatement);
            return registrationIndex >= 0 &&
                   registrationBlock.Statements
                       .Take(registrationIndex)
                       .All(statement =>
                           CanCompleteNormally(
                               statement,
                               registrationStatement));
        }

        return false;
    }

    private bool CanDefinitelySupersede(
        OrderedRegistrationMutation mutation,
        ServiceRegistration registration)
    {
        if (mutation.Kind is not (
                RegistrationMutationKind.Clear or
                RegistrationMutationKind.RemoveAll) ||
            !CanMutationTargetRegistration(mutation, registration) ||
            !IsSameExecutionScope(mutation.Location, registration.Location))
        {
            return false;
        }

        var mutationStatement = GetEnclosingStatement(mutation.Location);
        var registrationStatement = GetEnclosingStatement(registration.Location);
        if (mutationStatement is null ||
            registrationStatement is null)
        {
            return false;
        }

        if (CanReachLaterStatementInSameBlock(
                mutationStatement,
                registrationStatement))
        {
            return true;
        }

        if (CanFinallyRegistrationSupersede(
                mutationStatement,
                registrationStatement))
        {
            return true;
        }

        foreach (var conditionalContainer in mutationStatement
                     .AncestorsAndSelf()
                     .OfType<StatementSyntax>()
                     .Where(static statement =>
                         statement is IfStatementSyntax or
                             SwitchStatementSyntax or
                             TryStatementSyntax or
                             ForStatementSyntax or
                             ForEachStatementSyntax or
                             ForEachVariableStatementSyntax or
                             WhileStatementSyntax or
                             DoStatementSyntax))
        {
            if (conditionalContainer.Parent is not BlockSyntax containingBlock ||
                !ReferenceEquals(registrationStatement.Parent, containingBlock))
            {
                continue;
            }

            var conditionalIndex = containingBlock.Statements.IndexOf(conditionalContainer);
            var registrationIndex = containingBlock.Statements.IndexOf(registrationStatement);
            if (conditionalIndex >= 0 &&
                registrationIndex > conditionalIndex &&
                CanReachFollowingStatement(
                    mutationStatement,
                    conditionalContainer,
                    registrationStatement))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanReachLaterStatementInSameBlock(
        StatementSyntax mutationStatement,
        StatementSyntax registrationStatement)
    {
        if (mutationStatement.Parent is not BlockSyntax block ||
            !ReferenceEquals(registrationStatement.Parent, block))
        {
            return false;
        }

        var mutationIndex = block.Statements.IndexOf(mutationStatement);
        var registrationIndex = block.Statements.IndexOf(registrationStatement);
        return mutationIndex >= 0 &&
               registrationIndex > mutationIndex &&
               block.Statements
                   .Skip(mutationIndex + 1)
                   .Take(registrationIndex - mutationIndex - 1)
                   .All(statement =>
                       CanCompleteNormally(
                           statement,
                           registrationStatement));
    }

    private bool CanReachFollowingStatement(
        StatementSyntax mutationStatement,
        StatementSyntax conditionalContainer,
        StatementSyntax registrationStatement) =>
        conditionalContainer switch
        {
            IfStatementSyntax conditional =>
                CanReachEndOfConditionalBranch(
                    mutationStatement,
                    conditional,
                    registrationStatement),
            SwitchStatementSyntax switchStatement =>
                CanReachEndOfSwitchSection(
                    mutationStatement,
                    switchStatement,
                    registrationStatement),
            TryStatementSyntax tryStatement =>
                CanReachEndOfTryStatement(
                    mutationStatement,
                    tryStatement,
                    registrationStatement),
            ForStatementSyntax or
            ForEachStatementSyntax or
            ForEachVariableStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax =>
                CanReachEndOfLoop(
                    mutationStatement,
                    conditionalContainer,
                    registrationStatement),
            _ => false,
        };

    private bool CanReachEndOfTryStatement(
        StatementSyntax mutationStatement,
        TryStatementSyntax tryStatement,
        StatementSyntax registrationStatement)
    {
        if (tryStatement.Finally is { } finallyClause &&
            !CanCompleteNormally(
                finallyClause.Block,
                registrationStatement))
        {
            return false;
        }

        var mutationBlock = mutationStatement.AncestorsAndSelf()
            .OfType<BlockSyntax>()
            .FirstOrDefault(block =>
                ReferenceEquals(block, tryStatement.Block) ||
                tryStatement.Catches.Any(catchClause =>
                    ReferenceEquals(block, catchClause.Block)) ||
                ReferenceEquals(block, tryStatement.Finally?.Block));
        if (mutationBlock is null)
        {
            return false;
        }

        if (CanReachEndOfStatementSequence(
                mutationStatement,
                mutationBlock.Statements,
                registrationStatement))
        {
            return true;
        }

        return CanReachCaughtThrow(
            mutationStatement,
            tryStatement,
            registrationStatement);
    }

    private bool CanReachCaughtThrow(
        StatementSyntax mutationStatement,
        TryStatementSyntax tryStatement,
        StatementSyntax registrationStatement)
    {
        if (!mutationStatement.Ancestors()
                .OfType<BlockSyntax>()
                .Any(block =>
                    ReferenceEquals(block, tryStatement.Block)) ||
            mutationStatement.Parent is not BlockSyntax mutationBlock)
        {
            return false;
        }

        var mutationIndex = mutationBlock.Statements.IndexOf(mutationStatement);
        var caughtThrow = mutationBlock.Statements
            .Skip(mutationIndex + 1)
            .OfType<ThrowStatementSyntax>()
            .FirstOrDefault(throwStatement =>
                ReferenceEquals(
                    throwStatement.Ancestors()
                        .OfType<TryStatementSyntax>()
                        .FirstOrDefault(),
                    tryStatement) &&
                CanReachLaterStatementInSameBlock(
                    mutationStatement,
                    throwStatement));
        if (caughtThrow is null)
        {
            return false;
        }

        return tryStatement.Catches.Any(catchClause =>
            CanCatchThrownExpression(catchClause, caughtThrow.Expression) &&
            CanCompleteNormally(
                catchClause.Block,
                registrationStatement));
    }

    private bool CanCatchThrownExpression(
        CatchClauseSyntax catchClause,
        ExpressionSyntax? thrownExpression)
    {
        if (catchClause.Filter is not null)
        {
            return false;
        }

        if (catchClause.Declaration is null)
        {
            return true;
        }

        if (thrownExpression is null)
        {
            return false;
        }

        var semanticModel = _compilation.GetSemanticModel(thrownExpression.SyntaxTree);
        var thrownType = semanticModel.GetTypeInfo(thrownExpression).Type;
        var caughtType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        return thrownType is not null &&
               caughtType is not null &&
               _compilation.ClassifyConversion(thrownType, caughtType).IsImplicit;
    }

    private bool CanReachEndOfConditionalBranch(
        StatementSyntax mutationStatement,
        IfStatementSyntax conditional,
        StatementSyntax registrationStatement)
    {
        var branch = mutationStatement.AncestorsAndSelf()
            .OfType<BlockSyntax>()
            .FirstOrDefault(block =>
                ReferenceEquals(block, conditional.Statement) ||
                ReferenceEquals(block, conditional.Else?.Statement));
        if (branch is null)
        {
            return ReferenceEquals(mutationStatement, conditional.Statement) ||
                   ReferenceEquals(mutationStatement, conditional.Else?.Statement);
        }

        return CanReachEndOfStatementSequence(
            mutationStatement,
            branch.Statements,
            registrationStatement);
    }

    private bool CanReachEndOfSwitchSection(
        StatementSyntax mutationStatement,
        SwitchStatementSyntax switchStatement,
        StatementSyntax registrationStatement)
    {
        var section = mutationStatement.AncestorsAndSelf()
            .OfType<SwitchSectionSyntax>()
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Parent, switchStatement));
        if (section is null)
        {
            return false;
        }

        return CanReachEndOfStatementSequence(
            mutationStatement,
            section.Statements,
            registrationStatement,
            switchStatement);
    }

    private bool CanReachEndOfLoop(
        StatementSyntax mutationStatement,
        StatementSyntax loop,
        StatementSyntax registrationStatement)
    {
        var body = loop switch
        {
            ForStatementSyntax forStatement => forStatement.Statement,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            ForEachVariableStatementSyntax forEachVariableStatement =>
                forEachVariableStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null,
        };
        if (body is null)
        {
            return false;
        }

        var isNonTerminating = loop switch
        {
            ForStatementSyntax forStatement =>
                forStatement.Condition is null ||
                IsConstantTrue(forStatement.Condition),
            WhileStatementSyntax whileStatement =>
                IsConstantTrue(whileStatement.Condition),
            DoStatementSyntax doStatement =>
                IsConstantTrue(doStatement.Condition),
            _ => false,
        };
        if (body is not BlockSyntax block)
        {
            return !isNonTerminating &&
                   ReferenceEquals(body, mutationStatement);
        }

        var canReachEndOfIteration = CanReachEndOfStatementSequence(
            mutationStatement,
            block.Statements,
            registrationStatement,
            loop);
        return canReachEndOfIteration &&
               (!isNonTerminating ||
                HasExitToCompletionTargetFromMutation(
                    loop,
                    registrationStatement,
                    mutationStatement));
    }

    private bool CanReachEndOfStatementSequence(
        StatementSyntax mutationStatement,
        SyntaxList<StatementSyntax> statements,
        StatementSyntax registrationStatement,
        StatementSyntax? structuredExitTarget = null)
    {
        var containingStatement = mutationStatement
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement =>
                statements.IndexOf(statement) >= 0);
        if (containingStatement is null)
        {
            return false;
        }

        if (!ReferenceEquals(containingStatement, mutationStatement) &&
            !CanReachFollowingStatement(
                mutationStatement,
                containingStatement,
                registrationStatement))
        {
            return false;
        }

        var mutationIndex = statements.IndexOf(containingStatement);
        return statements
            .Skip(mutationIndex + 1)
            .All(statement =>
                CanCompleteNormally(
                    statement,
                    registrationStatement,
                    structuredExitTarget));
    }

    private bool CanCompleteNormally(
        StatementSyntax statement,
        StatementSyntax completionTarget,
        StatementSyntax? structuredExitTarget = null)
    {
        switch (statement)
        {
            case BlockSyntax block:
                return block.Statements.All(nestedStatement =>
                    CanCompleteNormally(
                        nestedStatement,
                        completionTarget,
                        structuredExitTarget));
            case LabeledStatementSyntax labeled:
                return CanCompleteNormally(
                    labeled.Statement,
                    completionTarget,
                    structuredExitTarget);
            case IfStatementSyntax { Else: null } ifWithoutElse:
                return !IsConstantTrue(ifWithoutElse.Condition) ||
                       CanCompleteNormally(
                           ifWithoutElse.Statement,
                           completionTarget,
                           structuredExitTarget);
            case IfStatementSyntax ifStatement:
                return CanCompleteNormally(
                           ifStatement.Statement,
                           completionTarget,
                           structuredExitTarget) ||
                       CanCompleteNormally(
                           ifStatement.Else!.Statement,
                           completionTarget,
                           structuredExitTarget);
            case UsingStatementSyntax usingStatement:
                return CanCompleteNormally(
                    usingStatement.Statement,
                    completionTarget,
                    structuredExitTarget);
            case LockStatementSyntax lockStatement:
                return CanCompleteNormally(
                    lockStatement.Statement,
                    completionTarget,
                    structuredExitTarget);
            case FixedStatementSyntax fixedStatement:
                return CanCompleteNormally(
                    fixedStatement.Statement,
                    completionTarget,
                    structuredExitTarget);
            case CheckedStatementSyntax checkedStatement:
                return CanCompleteNormally(
                    checkedStatement.Block,
                    completionTarget,
                    structuredExitTarget);
            case UnsafeStatementSyntax unsafeStatement:
                return CanCompleteNormally(
                    unsafeStatement.Block,
                    completionTarget,
                    structuredExitTarget);
            case TryStatementSyntax tryStatement:
                if (tryStatement.Finally is { } finallyClause &&
                    !CanCompleteNormally(
                        finallyClause.Block,
                        completionTarget,
                        structuredExitTarget))
                {
                    return false;
                }

                return CanCompleteNormally(
                           tryStatement.Block,
                           completionTarget,
                           structuredExitTarget) ||
                       tryStatement.Catches.Any(catchClause =>
                           CanCompleteNormally(
                               catchClause.Block,
                               completionTarget,
                               structuredExitTarget));
            case SwitchStatementSyntax switchStatement:
                return !switchStatement.Sections.Any(section =>
                           section.Labels.Any(label =>
                               label is DefaultSwitchLabelSyntax)) ||
                       switchStatement.Sections.Any(section =>
                           section.Statements.All(nestedStatement =>
                               CanCompleteNormally(
                                   nestedStatement,
                                   completionTarget,
                                   switchStatement)));
            case WhileStatementSyntax whileStatement
                when IsConstantTrue(whileStatement.Condition):
                return HasExitToCompletionTarget(whileStatement, completionTarget);
            case DoStatementSyntax doStatement
                when IsConstantTrue(doStatement.Condition):
                return HasExitToCompletionTarget(doStatement, completionTarget);
            case ForStatementSyntax forStatement
                when forStatement.Condition is null ||
                     IsConstantTrue(forStatement.Condition):
                return HasExitToCompletionTarget(forStatement, completionTarget);
            case ExpressionStatementSyntax expressionStatement:
                return CanExpressionCompleteNormally(
                    expressionStatement.Expression);
            case LocalDeclarationStatementSyntax localDeclaration:
                return localDeclaration.Declaration.Variables.All(variable =>
                    variable.Initializer is null ||
                    CanExpressionCompleteNormally(variable.Initializer.Value));
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return false;
            case BreakStatementSyntax breakStatement:
                return structuredExitTarget is not null &&
                       ReferenceEquals(
                           GetBreakTarget(breakStatement),
                           structuredExitTarget);
            case ContinueStatementSyntax continueStatement:
                return structuredExitTarget is not null &&
                       ReferenceEquals(
                           GetContinueTarget(continueStatement),
                           structuredExitTarget);
            case GotoStatementSyntax gotoStatement:
                return CanGotoReachStatement(gotoStatement, completionTarget);
            default:
                return true;
        }
    }

    private bool CanExpressionCompleteNormally(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        if (expression is ThrowExpressionSyntax)
        {
            return false;
        }

        if (expression is CastExpressionSyntax castExpression)
        {
            return CanExpressionCompleteNormally(castExpression.Expression);
        }

        if (expression is AssignmentExpressionSyntax assignmentExpression)
        {
            return CanExpressionCompleteNormally(assignmentExpression.Right);
        }

        if (expression is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.IsKind(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression) &&
            IsDefinitelyNull(binaryExpression.Left))
        {
            return CanExpressionCompleteNormally(binaryExpression.Right);
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            return CanExpressionCompleteNormally(conditionalExpression.WhenTrue) ||
                   CanExpressionCompleteNormally(conditionalExpression.WhenFalse);
        }

        return true;
    }

    private static bool IsDefinitelyNull(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        return expression is LiteralExpressionSyntax literalExpression &&
               literalExpression.IsKind(
                   Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
               expression is CastExpressionSyntax castExpression &&
               IsDefinitelyNull(castExpression.Expression);
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            expression = parenthesizedExpression.Expression;
        }

        return expression;
    }

    private static bool HasExitToCompletionTarget(
        StatementSyntax loop,
        StatementSyntax completionTarget) =>
        loop.DescendantNodes()
            .OfType<BreakStatementSyntax>()
            .Any(breakStatement =>
                ReferenceEquals(
                    GetBreakTarget(breakStatement),
                    loop)) ||
        loop.DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Any(gotoStatement =>
                CanGotoReachStatement(gotoStatement, completionTarget));

    private static bool HasExitToCompletionTargetFromMutation(
        StatementSyntax loop,
        StatementSyntax completionTarget,
        StatementSyntax mutationStatement) =>
        loop.DescendantNodes()
            .OfType<BreakStatementSyntax>()
            .Any(breakStatement =>
                breakStatement.SpanStart > mutationStatement.SpanStart &&
                ReferenceEquals(
                    GetBreakTarget(breakStatement),
                    loop) &&
                SharesConditionalPath(mutationStatement, breakStatement)) ||
        loop.DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Any(gotoStatement =>
                gotoStatement.SpanStart > mutationStatement.SpanStart &&
                CanGotoReachStatement(gotoStatement, completionTarget) &&
                SharesConditionalPath(mutationStatement, gotoStatement));

    private static bool SharesConditionalPath(
        StatementSyntax mutationStatement,
        StatementSyntax exitStatement)
    {
        foreach (var conditional in mutationStatement.Ancestors()
                     .OfType<IfStatementSyntax>()
                     .Where(exitStatement.Ancestors().Contains))
        {
            var mutationBranch = GetContainingBranch(conditional, mutationStatement);
            var exitBranch = GetContainingBranch(conditional, exitStatement);
            if (mutationBranch is not null &&
                exitBranch is not null &&
                !ReferenceEquals(mutationBranch, exitBranch))
            {
                return false;
            }
        }

        return true;
    }

    private static StatementSyntax? GetContainingBranch(
        IfStatementSyntax conditional,
        StatementSyntax statement)
    {
        if (statement.AncestorsAndSelf().Contains(conditional.Statement))
        {
            return conditional.Statement;
        }

        return conditional.Else is { } elseClause &&
               statement.AncestorsAndSelf().Contains(elseClause.Statement)
            ? conditional.Else.Statement
            : null;
    }

    private static StatementSyntax? GetBreakTarget(
        BreakStatementSyntax breakStatement) =>
        breakStatement.Ancestors().OfType<StatementSyntax>()
            .FirstOrDefault(static ancestor =>
                ancestor is ForStatementSyntax or
                    ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or
                    WhileStatementSyntax or
                    DoStatementSyntax or
                    SwitchStatementSyntax);

    private static StatementSyntax? GetContinueTarget(
        ContinueStatementSyntax continueStatement) =>
        continueStatement.Ancestors().OfType<StatementSyntax>()
            .FirstOrDefault(static ancestor =>
                ancestor is ForStatementSyntax or
                    ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or
                    WhileStatementSyntax or
                    DoStatementSyntax);

    private static bool CanGotoReachStatement(
        GotoStatementSyntax gotoStatement,
        StatementSyntax completionTarget) =>
        gotoStatement.Expression is IdentifierNameSyntax identifier &&
        completionTarget.AncestorsAndSelf()
            .OfType<LabeledStatementSyntax>()
            .Any(label =>
                string.Equals(
                    label.Identifier.ValueText,
                    identifier.Identifier.ValueText,
                    StringComparison.Ordinal));

    private static bool IsConstantTrue(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal =>
                literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.TrueLiteralExpression),
            ParenthesizedExpressionSyntax parenthesized =>
                IsConstantTrue(parenthesized.Expression),
            _ => false,
        };

    private static bool IsShortCircuiting(BinaryExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.CoalesceExpression) ||
        expression.IsKind(SyntaxKind.LogicalAndExpression) ||
        expression.IsKind(SyntaxKind.LogicalOrExpression);

    private static bool IsSameExecutionScope(
        Location mutationLocation,
        Location registrationLocation)
    {
        var mutationScope = GetExecutionScopeKey(mutationLocation);
        var registrationScope = GetExecutionScopeKey(registrationLocation);
        return mutationScope.HasValue &&
               registrationScope.HasValue &&
               mutationScope.Value.Equals(registrationScope.Value);
    }

    private static ServiceCollectionReachabilityAnalyzer.LocationKey? GetExecutionScopeKey(
        Location location)
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
                    return ServiceCollectionReachabilityAnalyzer.LocationKey.Create(
                        ancestor.GetLocation());
            }
        }

        return null;
    }

    private static bool IsSameStraightLineBlock(
        Location mutationLocation,
        Location registrationLocation)
    {
        var mutationStatement = GetEnclosingStatement(mutationLocation);
        var registrationStatement = GetEnclosingStatement(registrationLocation);
        if (mutationStatement is null ||
            registrationStatement is null)
        {
            return false;
        }

        if (ReferenceEquals(mutationStatement, registrationStatement))
        {
            return registrationLocation.SourceSpan.Start <= mutationLocation.SourceSpan.Start &&
                   registrationLocation.SourceSpan.End < mutationLocation.SourceSpan.End;
        }

        if (mutationStatement.Parent is not BlockSyntax mutationBlock ||
            !ReferenceEquals(mutationBlock, registrationStatement.Parent))
        {
            return IsEarlierTopLevelStatement(
                registrationStatement,
                mutationStatement);
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

    private static bool IsEarlierTopLevelStatement(
        StatementSyntax registrationStatement,
        StatementSyntax mutationStatement)
    {
        if (registrationStatement.Parent is not GlobalStatementSyntax registrationGlobal ||
            mutationStatement.Parent is not GlobalStatementSyntax mutationGlobal ||
            registrationGlobal.Parent is not CompilationUnitSyntax compilationUnit ||
            !ReferenceEquals(compilationUnit, mutationGlobal.Parent))
        {
            return false;
        }

        var registrationIndex = compilationUnit.Members.IndexOf(registrationGlobal);
        var mutationIndex = compilationUnit.Members.IndexOf(mutationGlobal);
        if (registrationIndex < 0 || mutationIndex <= registrationIndex)
        {
            return false;
        }

        for (var i = registrationIndex + 1; i < mutationIndex; i++)
        {
            if (compilationUnit.Members[i] is GlobalStatementSyntax globalStatement &&
                CanBypassLaterMutation(globalStatement.Statement))
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

    private void Add(ServiceRegistration registration) => _registrations.Add(registration);
}
