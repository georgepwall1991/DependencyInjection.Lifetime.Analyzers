using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Shared helpers for analyzing DI factory registrations.
/// </summary>
internal static class FactoryAnalysis
{
    public static IEnumerable<InvocationExpressionSyntax> GetFactoryInvocations(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel)
    {
        foreach (var unwrappedFactoryExpression in ResolveFactoryExpressions(factoryExpression, semanticModel))
        {
            if (unwrappedFactoryExpression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                foreach (var invocation in unwrappedFactoryExpression.DescendantNodesAndSelf()
                             .OfType<InvocationExpressionSyntax>())
                {
                    yield return invocation;
                }

                continue;
            }

            if (!IsMethodGroupExpression(unwrappedFactoryExpression, semanticModel))
            {
                continue;
            }

            if (!TryGetFactoryMethodBodyNode(unwrappedFactoryExpression, semanticModel, out var bodyNode))
            {
                continue;
            }

            foreach (var invocation in bodyNode.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                yield return invocation;
            }
        }
    }

    /// <summary>
    /// Resolves the semantic model that owns <paramref name="node"/>. Method-group factories
    /// resolve to bodies declared in other files, so nodes produced by factory analysis must
    /// never be queried against the registration site's model.
    /// </summary>
    public static SemanticModel? GetSemanticModelForNode(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node.SyntaxTree == semanticModel.SyntaxTree)
        {
            return semanticModel;
        }

        var compilation = semanticModel.Compilation;
        if (!compilation.ContainsSyntaxTree(node.SyntaxTree))
        {
            return null;
        }

        #pragma warning disable RS1030
        return compilation.GetSemanticModel(node.SyntaxTree);
        #pragma warning restore RS1030
    }

    public static bool TryGetActivatorUtilitiesImplementationType(
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
        if (sourceMethod.Name is not ("CreateInstance" or "GetServiceOrCreateInstance") ||
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

    public static bool TryGetFactoryReturnExpression(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out ExpressionSyntax returnExpression)
    {
        returnExpression = null!;

        var factoryExpressions = ResolveFactoryExpressions(factoryExpression, semanticModel).ToArray();
        if (factoryExpressions.Length != 1)
        {
            return false;
        }

        var unwrappedFactoryExpression = factoryExpressions[0];
        switch (unwrappedFactoryExpression)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                return TryGetReturnedExpression(simpleLambda.Body, out returnExpression);
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                return TryGetReturnedExpression(parenthesizedLambda.Body, out returnExpression);
            case AnonymousMethodExpressionSyntax anonymousMethod:
                return anonymousMethod.Body is not null &&
                       TryGetReturnedExpression(anonymousMethod.Body, out returnExpression);
        }

        if (!IsMethodGroupExpression(unwrappedFactoryExpression, semanticModel) ||
            !TryGetFactoryMethodBodyNode(unwrappedFactoryExpression, semanticModel, out var bodyNode))
        {
            return false;
        }

        return TryGetReturnedExpression(bodyNode, out returnExpression);
    }

    public static ExpressionSyntax ResolveFactoryExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var resolvedExpressions = ResolveFactoryExpressions(expression, semanticModel).Take(2).ToArray();
        return resolvedExpressions.Length == 1
            ? resolvedExpressions[0]
            : UnwrapFactoryExpression(expression);
    }

    public static IEnumerable<ExpressionSyntax> ResolveFactoryExpressions(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        return ResolveFactoryExpressions(
            expression,
            semanticModel,
            System.Array.Empty<ILocalSymbol>());
    }

    private static IEnumerable<ExpressionSyntax> ResolveFactoryExpressions(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IReadOnlyList<ILocalSymbol> visitedLocals)
    {
        var unwrappedExpression = UnwrapFactoryExpression(expression);
        if (semanticModel.GetSymbolInfo(unwrappedExpression).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local &&
            !visitedLocals.Any(visitedLocal => SymbolEqualityComparer.Default.Equals(visitedLocal, local)) &&
            TryGetStableLocalDelegateValues(unwrappedExpression, semanticModel, out var values))
        {
            var nextVisitedLocals = visitedLocals.Concat(new[] { local }).ToArray();
            return values
                .SelectMany(value => ResolveFactoryExpressions(value, semanticModel, nextVisitedLocals))
                .ToArray();
        }

        return new[] { unwrappedExpression };
    }

    private static bool TryGetStableLocalDelegateValues(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax> values)
    {
        values = System.Array.Empty<ExpressionSyntax>();

        if (semanticModel.GetSymbolInfo(expression).Symbol is not ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local ||
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator ||
            declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { } declarationStatement ||
            expression.FirstAncestorOrSelf<StatementSyntax>() is not { } usageStatement ||
            !TryGetSharedStatementScope(declarationStatement, usageStatement, out var statementScope) ||
            declarationStatement.SpanStart >= expression.SpanStart)
        {
            return false;
        }

        return TryResolveStableLocalDelegateValues(
            declarator.Initializer?.Value,
            statementScope,
            declarator.Span.End,
            expression.SpanStart,
            local,
            semanticModel,
            out values);
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

    private static bool TryGetSharedStatementScope(
        LocalDeclarationStatementSyntax declarationStatement,
        StatementSyntax usageStatement,
        out SyntaxNode statementScope)
    {
        if (declarationStatement.Parent is BlockSyntax block &&
            usageStatement.Parent == block)
        {
            statementScope = block;
            return true;
        }

        if (declarationStatement.Parent is GlobalStatementSyntax declarationGlobal &&
            usageStatement.Parent is GlobalStatementSyntax usageGlobal &&
            declarationGlobal.Parent is CompilationUnitSyntax compilationUnit &&
            usageGlobal.Parent == compilationUnit)
        {
            statementScope = compilationUnit;
            return true;
        }

        statementScope = null!;
        return false;
    }

    private static bool TryResolveStableLocalDelegateValues(
        ExpressionSyntax? initializerValue,
        SyntaxNode statementScope,
        int start,
        int end,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax> resolvedValues)
    {
        var values = initializerValue is null
            ? new List<ExpressionSyntax>()
            : new List<ExpressionSyntax> { initializerValue };
        var handledBranchStarts = new HashSet<int>();
        var handledBranchAssignmentStarts = new HashSet<int>();
        var handledBranchInvocationStarts = new HashSet<int>();
        var nodesBetween = statementScope
            .DescendantNodes()
            .Where(node => node.SpanStart > start && node.SpanStart < end)
            .Where(node => !IsInsideUninvokedNestedCallable(node))
            .OrderBy(node => node.SpanStart)
            .ToArray();

        foreach (var node in nodesBetween)
        {
            switch (node)
            {
                case IfStatementSyntax ifStatement
                    when !handledBranchStarts.Contains(ifStatement.SpanStart) &&
                         !IsNodeBlockedByDefiniteExitBeforeEnd(ifStatement, end) &&
                         TryGetExhaustiveBranchDelegateAssignmentValues(
                        ifStatement,
                        local,
                        semanticModel,
                        end,
                        treatReturnAsExit: true,
                        out var branchValues,
                        out var branchDefinitelyAssigns,
                        out var branchHandledInvocations,
                        out var branchAssignments):
                    if (branchDefinitelyAssigns &&
                        IsDefinitelyExecutedInScope(ifStatement, statementScope))
                    {
                        values.Clear();
                    }

                    values.AddRange(branchValues);
                    foreach (var branchAssignment in branchAssignments)
                    {
                        handledBranchAssignmentStarts.Add(branchAssignment.SpanStart);
                    }

                    foreach (var branchInvocation in branchHandledInvocations)
                    {
                        handledBranchInvocationStarts.Add(branchInvocation.SpanStart);
                    }

                    foreach (var nestedIfStatement in ifStatement.DescendantNodes().OfType<IfStatementSyntax>())
                    {
                        handledBranchStarts.Add(nestedIfStatement.SpanStart);
                    }

                    break;

                case AssignmentExpressionSyntax assignment
                    when !handledBranchAssignmentStarts.Contains(assignment.SpanStart) &&
                         !IsNodeBlockedByDefiniteExitBeforeEnd(assignment, end) &&
                         IsSameLocal(assignment.Left, local, semanticModel):
                    if (!TryGetStableDelegateAssignmentValue(assignment, local, semanticModel, out var assignedValue))
                    {
                        resolvedValues = System.Array.Empty<ExpressionSyntax>();
                        return false;
                    }

                    if (TryGetAliasCycleAssignmentValues(
                            assignedValue,
                            local,
                            semanticModel,
                            out var aliasAssignmentValues))
                    {
                        if (IsDefinitelyExecutedInScope(assignment, statementScope))
                        {
                            values.Clear();
                        }

                        values.AddRange(aliasAssignmentValues);
                        break;
                    }

                    if (IsDefinitelyExecutedInScope(assignment, statementScope))
                    {
                        values.Clear();
                    }

                    values.Add(assignedValue);
                    break;

                case AssignmentExpressionSyntax assignment
                    when !handledBranchAssignmentStarts.Contains(assignment.SpanStart) &&
                         !IsNodeBlockedByDefiniteExitBeforeEnd(assignment, end) &&
                         IsPotentialLocalWriteTarget(assignment.Left, local, semanticModel):
                    resolvedValues = System.Array.Empty<ExpressionSyntax>();
                    return false;

                case ArgumentSyntax argument
                    when IsRefOrOutArgument(argument) &&
                         !IsNodeBlockedByDefiniteExitBeforeEnd(argument, end) &&
                         ContainsSameLocal(argument.Expression, local, semanticModel):
                    resolvedValues = System.Array.Empty<ExpressionSyntax>();
                    return false;

                case InvocationExpressionSyntax invocation
                    when handledBranchInvocationStarts.Contains(invocation.SpanStart):
                    break;

                case InvocationExpressionSyntax invocation
                    when InvokedDelegateWritesLocal(
                        invocation,
                        local,
                        semanticModel,
                        out var delegateAssignedValues,
                        out var isDefiniteDelegateWrite):
                    if (delegateAssignedValues is null)
                    {
                        resolvedValues = System.Array.Empty<ExpressionSyntax>();
                        return false;
                    }

                    if (isDefiniteDelegateWrite &&
                        IsDefinitelyExecutedInScope(invocation, statementScope))
                    {
                        values.Clear();
                    }

                    if (!IsNodeBlockedByDefiniteExitBeforeEnd(invocation, end))
                    {
                        values.AddRange(delegateAssignedValues);
                    }

                    break;

                case InvocationExpressionSyntax invocation
                    when InvokedLocalFunctionWritesLocal(
                        invocation,
                        local,
                        semanticModel,
                        out var localFunctionAssignedValues,
                        out var isDefiniteLocalFunctionWrite):
                    if (localFunctionAssignedValues is null)
                    {
                        resolvedValues = System.Array.Empty<ExpressionSyntax>();
                        return false;
                    }

                    if (isDefiniteLocalFunctionWrite &&
                        IsDefinitelyExecutedInScope(invocation, statementScope))
                    {
                        values.Clear();
                    }

                    if (!IsNodeBlockedByDefiniteExitBeforeEnd(invocation, end))
                    {
                        values.AddRange(localFunctionAssignedValues);
                    }

                    break;
            }
        }

        resolvedValues = values;
        return true;
    }

    private static bool TryGetAliasCycleAssignmentValues(
        ExpressionSyntax assignedValue,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax> assignmentValues)
    {
        assignmentValues = System.Array.Empty<ExpressionSyntax>();
        if (!IsLocalDelegateReference(assignedValue, semanticModel) ||
            !TryGetStableLocalDelegateValues(assignedValue, semanticModel, out var aliasValues))
        {
            return false;
        }

        var sawCycle = false;
        var expandedValues = new List<ExpressionSyntax>();
        foreach (var aliasValue in aliasValues)
        {
            if (IsSameLocal(UnwrapFactoryExpression(aliasValue), local, semanticModel))
            {
                sawCycle = true;
                if (TryGetStableLocalDelegateValues(aliasValue, semanticModel, out var snapshotValues))
                {
                    expandedValues.AddRange(snapshotValues
                        .Where(snapshotValue => !IsSameLocal(
                            UnwrapFactoryExpression(snapshotValue),
                            local,
                            semanticModel)));
                }

                continue;
            }

            expandedValues.Add(aliasValue);
        }

        if (!sawCycle)
        {
            return false;
        }

        assignmentValues = expandedValues;
        return true;
    }

    private static bool TryGetExhaustiveBranchDelegateAssignmentValues(
        IfStatementSyntax ifStatement,
        ILocalSymbol local,
        SemanticModel semanticModel,
        int end,
        bool treatReturnAsExit,
        out IReadOnlyList<ExpressionSyntax> assignedValues,
        out bool definitelyAssigns,
        out IReadOnlyList<InvocationExpressionSyntax> handledInvocations,
        out IReadOnlyList<AssignmentExpressionSyntax> assignments)
    {
        assignedValues = System.Array.Empty<ExpressionSyntax>();
        definitelyAssigns = false;
        handledInvocations = System.Array.Empty<InvocationExpressionSyntax>();
        assignments = System.Array.Empty<AssignmentExpressionSyntax>();
        if (ifStatement.Else?.Statement is not { } elseStatement)
        {
            return false;
        }

        if (!TryGetBranchDelegateAssignmentValues(ifStatement.Statement, local, semanticModel, end, treatReturnAsExit, out var whenTrueValues, out var whenTrueDefinitelyAssigns, out var whenTrueHandledInvocations, out var whenTrueAssignments) ||
            !TryGetBranchDelegateAssignmentValues(elseStatement, local, semanticModel, end, treatReturnAsExit, out var whenFalseValues, out var whenFalseDefinitelyAssigns, out var whenFalseHandledInvocations, out var whenFalseAssignments))
        {
            return false;
        }

        assignedValues = whenTrueValues.Concat(whenFalseValues).ToArray();
        definitelyAssigns = whenTrueDefinitelyAssigns && whenFalseDefinitelyAssigns;
        handledInvocations = whenTrueHandledInvocations.Concat(whenFalseHandledInvocations).ToArray();
        assignments = whenTrueAssignments.Concat(whenFalseAssignments).ToArray();
        return true;
    }

    private static bool TryGetBranchDelegateAssignmentValues(
        StatementSyntax branch,
        ILocalSymbol local,
        SemanticModel semanticModel,
        int end,
        bool treatReturnAsExit,
        out IReadOnlyList<ExpressionSyntax> assignedValues,
        out bool definitelyAssigns,
        out IReadOnlyList<InvocationExpressionSyntax> handledInvocations,
        out IReadOnlyList<AssignmentExpressionSyntax> assignments)
    {
        assignedValues = System.Array.Empty<ExpressionSyntax>();
        definitelyAssigns = false;
        handledInvocations = System.Array.Empty<InvocationExpressionSyntax>();
        assignments = System.Array.Empty<AssignmentExpressionSyntax>();
        if (branch
            .DescendantNodesAndSelf()
            .Where(node => !IsInsideNestedCallable(node, branch))
            .OfType<ArgumentSyntax>()
            .Any(argument => IsRefOrOutArgument(argument) &&
                             !IsNodeBlockedByDefiniteExitBeforeEnd(argument, end, treatReturnAsExit) &&
                             ContainsSameLocal(argument.Expression, local, semanticModel)))
        {
            return false;
        }

        if (branch is IfStatementSyntax ifStatement)
        {
            return TryGetExhaustiveBranchDelegateAssignmentValues(
                ifStatement,
                local,
                semanticModel,
                end,
                treatReturnAsExit,
                out assignedValues,
                out definitelyAssigns,
                out handledInvocations,
                out assignments);
        }

        var branchAssignments = branch
            .DescendantNodesAndSelf()
            .Where(node => !IsInsideNestedCallable(node, branch))
            .OfType<AssignmentExpressionSyntax>()
            .Where(candidate => IsSameLocal(candidate.Left, local, semanticModel) ||
                                IsPotentialLocalWriteTarget(candidate.Left, local, semanticModel))
            .ToArray();

        if (branchAssignments.Length == 0)
        {
            var branchInvocationWrites = branch
                .DescendantNodesAndSelf()
                .Where(node => !IsInsideNestedCallable(node, branch))
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => TryGetInvocationAssignedValues(
                    invocation,
                    local,
                    semanticModel,
                    out var invocationAssignedValues,
                    out var invocationDefinitelyAssigns)
                    ? new
                    {
                        Invocation = invocation,
                        AssignedValues = invocationAssignedValues,
                        DefinitelyAssigns = invocationDefinitelyAssigns
                    }
                    : null)
                .Where(candidate => candidate is not null)
                .ToArray();

            if (branchInvocationWrites.Length == 1)
            {
                var branchInvocationWrite = branchInvocationWrites[0]!;
                if (branchInvocationWrite.AssignedValues is null)
                {
                    return false;
                }

                var invocationDefinitelyExecutes = IsDefinitelyExecutedInBranch(
                    branchInvocationWrite.Invocation,
                    branch,
                    treatReturnAsExit);
                if (!invocationDefinitelyExecutes && treatReturnAsExit)
                {
                    return false;
                }

                if (!IsNodeBlockedByDefiniteExitBeforeEnd(
                        branchInvocationWrite.Invocation,
                        end,
                        treatReturnAsExit))
                {
                    assignedValues = branchInvocationWrite.AssignedValues;
                }

                definitelyAssigns = invocationDefinitelyExecutes &&
                                   branchInvocationWrite.DefinitelyAssigns;
                handledInvocations = new[] { branchInvocationWrite.Invocation };
                return true;
            }

            if (branchInvocationWrites.Length > 1)
            {
                return false;
            }

            definitelyAssigns = StatementDefinitelyExits(branch, treatReturnAsExit);
            return definitelyAssigns;
        }

        if (branchAssignments.Length != 1 ||
            !TryGetStableDelegateAssignmentValue(branchAssignments[0], local, semanticModel, out var assignedValue))
        {
            return false;
        }

        var assignmentDefinitelyExecutes = IsDefinitelyExecutedInBranch(branchAssignments[0], branch, treatReturnAsExit);
        if (!assignmentDefinitelyExecutes && treatReturnAsExit)
        {
            return false;
        }

        if (!IsNodeBlockedByDefiniteExitBeforeEnd(branchAssignments[0], end, treatReturnAsExit))
        {
            assignedValues = new[] { assignedValue };
        }

        definitelyAssigns = assignmentDefinitelyExecutes;
        if (assignmentDefinitelyExecutes)
        {
            handledInvocations = branch
                .DescendantNodesAndSelf()
                .Where(node => !IsInsideNestedCallable(node, branch))
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation.SpanStart < branchAssignments[0].SpanStart)
                .ToArray();
        }

        assignments = new[] { branchAssignments[0] };
        return true;
    }

    private static bool TryGetInvocationAssignedValues(
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax>? assignedValues,
        out bool definitelyAssigns)
    {
        return InvokedDelegateWritesLocal(invocation, local, semanticModel, out assignedValues, out definitelyAssigns) ||
               InvokedLocalFunctionWritesLocal(invocation, local, semanticModel, out assignedValues, out definitelyAssigns);
    }

    private static bool IsDefinitelyExecutedInBranch(
        SyntaxNode node,
        StatementSyntax branch,
        bool treatReturnAsExit)
    {
        if (branch is BlockSyntax block)
        {
            var statement = node.FirstAncestorOrSelf<StatementSyntax>();
            if (statement?.Parent != block)
            {
                return false;
            }

            if (!treatReturnAsExit &&
                block.Statements
                    .TakeWhile(candidate => candidate != statement)
                    .Any(candidate => ContainsExitReturn(candidate, block)))
            {
                return false;
            }

            return true;
        }

        return node.FirstAncestorOrSelf<StatementSyntax>() == branch;
    }

    private static bool IsDefinitelyExecutedInScope(
        SyntaxNode node,
        SyntaxNode statementScope)
    {
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent == statementScope)
        {
            return IsDefinitelyEvaluatedInStatement(node, statement);
        }

        return statement?.Parent is GlobalStatementSyntax globalStatement &&
               globalStatement.Parent == statementScope &&
               IsDefinitelyEvaluatedInStatement(node, statement);
    }

    private static bool IsDefinitelyEvaluatedInStatement(
        SyntaxNode node,
        StatementSyntax statement)
    {
        if (node == statement)
        {
            return true;
        }

        for (var current = node.Parent; current is not null && current != statement; current = current.Parent)
        {
            switch (current)
            {
                case BinaryExpressionSyntax binaryExpression
                    when (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
                          binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) ||
                          binaryExpression.IsKind(SyntaxKind.CoalesceExpression)) &&
                         ContainsNode(binaryExpression.Right, node):
                    return false;

                case ConditionalExpressionSyntax conditionalExpression
                    when ContainsNode(conditionalExpression.WhenTrue, node) ||
                         ContainsNode(conditionalExpression.WhenFalse, node):
                    return false;

                case ConditionalAccessExpressionSyntax conditionalAccessExpression
                    when ContainsNode(conditionalAccessExpression.WhenNotNull, node):
                    return false;

                case SwitchExpressionArmSyntax:
                    return false;
            }
        }

        return statement is not ForStatementSyntax;
    }

    private static bool ContainsNode(SyntaxNode container, SyntaxNode node) =>
        container.SpanStart <= node.SpanStart &&
        node.Span.End <= container.Span.End;

    private static bool IsInsideUninvokedNestedCallable(SyntaxNode node) =>
        node
            .Ancestors()
            .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        StatementSyntax statement)
    {
        for (var current = node.Parent; current is not null && current != statement; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetStableDelegateAssignmentValue(
        AssignmentExpressionSyntax assignment,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out ExpressionSyntax assignedValue)
    {
        assignedValue = null!;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsSameLocal(assignment.Left, local, semanticModel) ||
            !IsResolvableFactoryValue(assignment.Right, semanticModel))
        {
            return false;
        }

        assignedValue = assignment.Right;
        return true;
    }

    private static bool InvokedLocalFunctionWritesLocal(
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax>? assignedValues,
        out bool isDefiniteWrite)
    {
        assignedValues = null;
        isDefiniteWrite = false;
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not LocalFunctionStatementSyntax localFunction)
        {
            return false;
        }

        var analyzeSynchronousPrefixOnly =
            localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
            !IsAwaitedInvocation(invocation);

        return TryGetLocalFunctionAssignedValue(
            localFunction,
            local,
            semanticModel,
            analyzeSynchronousPrefixOnly,
            out assignedValues,
            out isDefiniteWrite);
    }

    private static bool InvokedDelegateWritesLocal(
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax>? assignedValues,
        out bool isDefiniteWrite)
    {
        assignedValues = null;
        isDefiniteWrite = false;
        if (GetInvokedDelegateExpression(invocation, semanticModel) is not { } invokedExpression ||
            semanticModel.GetSymbolInfo(invokedExpression).Symbol is not ILocalSymbol { Type.TypeKind: TypeKind.Delegate } ||
            !TryGetStableLocalDelegateValues(invokedExpression, semanticModel, out var delegateValues))
        {
            return false;
        }

        var sawPossibleWrite = false;
        var everyPossibleDelegateWrites = true;
        var everyWriteIsDefinite = true;
        var collectedValues = new List<ExpressionSyntax>();
        foreach (var delegateValue in delegateValues)
        {
            if (TryGetLocalFunctionMethodGroup(delegateValue, semanticModel, out var localFunction))
            {
                var analyzeSynchronousPrefixOnly =
                    localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
                    !IsAwaitedInvocation(invocation);
                if (!TryGetLocalFunctionAssignedValue(
                        localFunction,
                        local,
                        semanticModel,
                        analyzeSynchronousPrefixOnly,
                        out var localFunctionAssignedValues,
                        out var localFunctionDefiniteWrite))
                {
                    everyPossibleDelegateWrites = false;
                    continue;
                }

                if (localFunctionAssignedValues is null)
                {
                    assignedValues = null;
                    return true;
                }

                sawPossibleWrite = true;
                everyWriteIsDefinite &= localFunctionDefiniteWrite;
                collectedValues.AddRange(localFunctionAssignedValues);
                continue;
            }

            if (delegateValue is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                var nodes = anonymousFunction
                    .DescendantNodes()
                    .Where(node => !IsInsideNestedCallable(node, anonymousFunction))
                    .ToArray();
                if (nodes
                        .OfType<AssignmentExpressionSyntax>()
                        .Any(assignment => IsSameLocal(assignment.Left, local, semanticModel) ||
                                           IsPotentialLocalWriteTarget(assignment.Left, local, semanticModel)) ||
                    nodes
                        .OfType<ArgumentSyntax>()
                        .Any(argument => IsRefOrOutArgument(argument) &&
                                         ContainsSameLocal(argument.Expression, local, semanticModel)))
                {
                    assignedValues = null;
                    return true;
                }
            }

            everyPossibleDelegateWrites = false;
        }

        if (!sawPossibleWrite)
        {
            return false;
        }

        assignedValues = collectedValues;
        isDefiniteWrite = everyPossibleDelegateWrites &&
                          everyWriteIsDefinite &&
                          collectedValues.Count > 0;
        return true;
    }

    private static ExpressionSyntax? GetInvokedDelegateExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation.Expression).Symbol is ILocalSymbol)
        {
            return invocation.Expression;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Invoke",
                Expression: var receiver
            })
        {
            return receiver;
        }

        return null;
    }

    private static bool TryGetLocalFunctionMethodGroup(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out LocalFunctionStatementSyntax localFunction)
    {
        localFunction = null!;
        var unwrappedExpression = UnwrapFactoryExpression(expression);
        if (unwrappedExpression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(unwrappedExpression);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not LocalFunctionStatementSyntax candidate)
        {
            return false;
        }

        localFunction = candidate;
        return true;
    }

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        AnonymousFunctionExpressionSyntax anonymousFunction)
    {
        for (var current = node.Parent; current is not null && current != anonymousFunction; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLocalFunctionAssignedValue(
        LocalFunctionStatementSyntax localFunction,
        ILocalSymbol local,
        SemanticModel semanticModel,
        bool analyzeSynchronousPrefixOnly,
        out IReadOnlyList<ExpressionSyntax>? assignedValues,
        out bool isDefiniteWrite)
    {
        assignedValues = null;
        isDefiniteWrite = false;
        if (IsIteratorLocalFunction(localFunction))
        {
            return false;
        }

        var localFunctionNodes = GetLocalFunctionAnalysisNodes(localFunction, analyzeSynchronousPrefixOnly);
        var hasRefOrOutWrite = localFunctionNodes
            .OfType<ArgumentSyntax>()
            .Any(argument => IsRefOrOutArgument(argument) &&
                             ContainsSameLocal(argument.Expression, local, semanticModel));
        var localAssignments = localFunctionNodes
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => IsSameLocal(assignment.Left, local, semanticModel) ||
                                 IsPotentialLocalWriteTarget(assignment.Left, local, semanticModel))
            .ToArray();

        if (!hasRefOrOutWrite &&
            TryGetLocalFunctionExhaustiveBranchAssignedValues(
                localFunction,
                localFunctionNodes,
                localAssignments,
                local,
                semanticModel,
                out var branchAssignedValues,
                out var isDefiniteBranchWrite))
        {
            assignedValues = branchAssignedValues;
            isDefiniteWrite = isDefiniteBranchWrite;
            return true;
        }

        if (!hasRefOrOutWrite &&
            localAssignments.Length == 1 &&
            TryGetStableDelegateAssignmentValue(localAssignments[0], local, semanticModel, out var stableAssignedValue))
        {
            assignedValues = new[] { stableAssignedValue };
            isDefiniteWrite = IsDefinitelyExecutedInLocalFunction(localAssignments[0], localFunction);
            return true;
        }

        if (localAssignments.Length > 0 || hasRefOrOutWrite)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetLocalFunctionExhaustiveBranchAssignedValues(
        LocalFunctionStatementSyntax localFunction,
        SyntaxNode[] localFunctionNodes,
        AssignmentExpressionSyntax[] localAssignments,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out IReadOnlyList<ExpressionSyntax> assignedValues,
        out bool isDefiniteWrite)
    {
        assignedValues = System.Array.Empty<ExpressionSyntax>();
        isDefiniteWrite = false;

        foreach (var ifStatement in localFunctionNodes.OfType<IfStatementSyntax>())
        {
            if (IsNodeBlockedByDefiniteExitBeforeEnd(
                    ifStatement,
                    localFunction.Span.End,
                    treatReturnAsExit: false))
            {
                continue;
            }

            if (!TryGetExhaustiveBranchDelegateAssignmentValues(
                    ifStatement,
                    local,
                    semanticModel,
                    localFunction.Span.End,
                    treatReturnAsExit: false,
                    out var branchAssignedValues,
                    out var branchDefinitelyAssigns,
                    out _,
                    out var branchAssignments))
            {
                continue;
            }

            var branchAssignmentStarts = new HashSet<int>(
                branchAssignments.Select(assignment => assignment.SpanStart));
            if (localAssignments.Any(assignment => !branchAssignmentStarts.Contains(assignment.SpanStart)))
            {
                continue;
            }

            assignedValues = branchAssignedValues;
            isDefiniteWrite = branchDefinitelyAssigns &&
                              IsDefinitelyExecutedInLocalFunction(ifStatement, localFunction);
            return true;
        }

        return false;
    }

    private static bool IsIteratorLocalFunction(LocalFunctionStatementSyntax localFunction)
    {
        return localFunction
            .DescendantNodes()
            .Where(node => !IsInsideNestedCallable(node, localFunction))
            .Any(node => node is YieldStatementSyntax);
    }

    private static SyntaxNode[] GetLocalFunctionAnalysisNodes(
        LocalFunctionStatementSyntax localFunction,
        bool analyzeSynchronousPrefixOnly)
    {
        var nodes = localFunction
            .DescendantNodes()
            .Where(node => !IsInsideNestedCallable(node, localFunction))
            .ToArray();
        if (!analyzeSynchronousPrefixOnly)
        {
            return nodes;
        }

        var firstAwait = nodes
            .OfType<AwaitExpressionSyntax>()
            .OrderBy(awaitExpression => awaitExpression.SpanStart)
            .FirstOrDefault();
        return firstAwait is null
            ? nodes
            : nodes.Where(node => node.SpanStart < firstAwait.SpanStart).ToArray();
    }

    private static bool IsAwaitedInvocation(InvocationExpressionSyntax invocation)
    {
        for (var current = invocation.Parent; current is not null && current is not StatementSyntax; current = current.Parent)
        {
            if (current is AwaitExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDefinitelyExecutedInLocalFunction(
        SyntaxNode node,
        LocalFunctionStatementSyntax localFunction)
    {
        if (localFunction.Body is not null)
        {
            return IsDefinitelyExecutedInBlock(node, localFunction.Body);
        }

        if (localFunction.ExpressionBody?.Expression is { } expressionBody)
        {
            return node == expressionBody ||
                   node.Ancestors().Any(ancestor => ancestor == expressionBody);
        }

        return false;
    }

    private static bool IsNodeFollowedByDefiniteExitInContainingBlock(
        SyntaxNode node,
        int end,
        bool treatReturnAsExit = true)
    {
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not BlockSyntax block)
        {
            return false;
        }

        return block.Statements
            .SkipWhile(candidate => candidate != statement)
            .Skip(1)
            .TakeWhile(candidate => candidate.Span.End <= end)
            .Any(candidate => StatementDefinitelyExits(candidate, treatReturnAsExit));
    }

    private static bool IsNodeBlockedByDefiniteExitBeforeEnd(
        SyntaxNode node,
        int end,
        bool treatReturnAsExit = true)
    {
        for (var statement = node.FirstAncestorOrSelf<StatementSyntax>();
             statement is not null;
             statement = statement.Parent?.FirstAncestorOrSelf<StatementSyntax>())
        {
            if (IsNodeFollowedByDefiniteExitInContainingBlock(statement, end, treatReturnAsExit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementDefinitelyExits(
        StatementSyntax statement,
        bool treatReturnAsExit)
    {
        return statement switch
        {
            ReturnStatementSyntax => treatReturnAsExit,
            ThrowStatementSyntax => true,
            BlockSyntax { Statements.Count: > 0 } block =>
                StatementDefinitelyExits(block.Statements[block.Statements.Count - 1], treatReturnAsExit),
            IfStatementSyntax ifStatement when ifStatement.Else?.Statement is { } elseStatement =>
                StatementDefinitelyExits(ifStatement.Statement, treatReturnAsExit) &&
                StatementDefinitelyExits(elseStatement, treatReturnAsExit),
            _ => false
        };
    }

    private static bool IsDefinitelyExecutedInBlock(
        SyntaxNode node,
        BlockSyntax block)
    {
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent != block)
        {
            return false;
        }

        foreach (var priorStatement in block.Statements.TakeWhile(candidate => candidate != statement))
        {
            if (ContainsExitReturn(priorStatement, block))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsExitReturn(
        StatementSyntax statement,
        BlockSyntax block) =>
        statement
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Any(returnStatement => !IsInsideNestedCallable(returnStatement, block));

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        LocalFunctionStatementSyntax localFunction)
    {
        for (var current = node.Parent; current is not null && current != localFunction; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsResolvableFactoryValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var unwrappedExpression = UnwrapFactoryExpression(expression);
        return unwrappedExpression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax ||
               IsMethodGroupExpression(unwrappedExpression, semanticModel) ||
               IsLocalDelegateReference(unwrappedExpression, semanticModel);
    }

    private static bool IsLocalDelegateReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel) =>
        semanticModel.GetSymbolInfo(expression).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate };

    private static bool IsRefOrOutArgument(ArgumentSyntax argument) =>
        argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
        argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

    private static bool ContainsSameLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel semanticModel) =>
        expression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => IsSameLocal(identifier, local, semanticModel));

    private static bool IsSameLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel semanticModel) =>
        SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(expression).Symbol, local);

    private static bool IsPotentialLocalWriteTarget(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel semanticModel)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            expression = parenthesizedExpression.Expression;
        }

        return expression is TupleExpressionSyntax &&
               ContainsSameLocal(expression, local, semanticModel);
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

    private static bool TryGetReturnedExpression(
        SyntaxNode bodyNode,
        out ExpressionSyntax returnExpression)
    {
        switch (bodyNode)
        {
            case ExpressionSyntax expression:
                returnExpression = expression;
                return true;
            case BlockSyntax { Statements.Count: 1 } blockSyntax
                when blockSyntax.Statements[0] is ReturnStatementSyntax { Expression: not null } returnStatement:
                returnExpression = returnStatement.Expression;
                return true;
            case BlockSyntax { Statements.Count: > 1 } blockSyntax
                when TryGetFinalReturnExpression(blockSyntax, out returnExpression):
                return true;
            default:
                returnExpression = null!;
                return false;
        }
    }

    private static bool TryGetFinalReturnExpression(
        BlockSyntax blockSyntax,
        out ExpressionSyntax returnExpression)
    {
        returnExpression = null!;

        if (blockSyntax.Statements[blockSyntax.Statements.Count - 1] is not ReturnStatementSyntax { Expression: not null } returnStatement)
        {
            return false;
        }

        if (blockSyntax.Statements
            .Take(blockSyntax.Statements.Count - 1)
            .Any(statement => ContainsFactoryExitReturn(statement, blockSyntax)))
        {
            return false;
        }

        returnExpression = returnStatement.Expression;
        return true;
    }

    private static bool ContainsFactoryExitReturn(
        StatementSyntax statement,
        BlockSyntax factoryBlock)
    {
        return statement
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Any(returnStatement => !IsInsideNestedCallable(returnStatement, factoryBlock));
    }

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        BlockSyntax factoryBlock)
    {
        for (var current = node.Parent; current is not null && current != factoryBlock; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
