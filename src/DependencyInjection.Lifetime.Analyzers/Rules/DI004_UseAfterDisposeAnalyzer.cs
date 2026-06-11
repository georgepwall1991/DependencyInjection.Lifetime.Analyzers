using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects when services are used after their scope has been disposed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI004_UseAfterDisposeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UseAfterScopeDisposed);

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

            var executableRoots = new ConcurrentBag<SyntaxNode>();
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

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    executableRoots.Add(syntaxContext.Node);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                ExecutableSyntaxHelper.ExecutableRootKinds);

            compilationContext.RegisterCompilationEndAction(
                endContext =>
                {
                    foreach (var executableRoot in executableRoots)
                    {
                        if (!semanticModelsByTree.TryGetValue(executableRoot.SyntaxTree, out var semanticModel))
                        {
                            continue;
                        }

                        AnalyzeExecutableRoot(
                            endContext,
                            executableRoot,
                            semanticModel,
                            wellKnownTypes,
                            registrationCollector);
                    }
                });
        });
    }

    private static void AnalyzeExecutableRoot(
        CompilationAnalysisContext context,
        SyntaxNode executableRoot,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        RegistrationCollector registrationCollector)
    {
        if (!ExecutableSyntaxHelper.TryGetExecutableBody(executableRoot, out var executableBody))
        {
            return;
        }

        var reportedSpans = new HashSet<TextSpan>();

        foreach (var usingStmt in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody).OfType<UsingStatementSyntax>())
        {
            AnalyzeUsingStatement(
                context,
                executableBody,
                usingStmt,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }

        foreach (var localDecl in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody).OfType<LocalDeclarationStatementSyntax>())
        {
            if (localDecl.UsingKeyword == default)
            {
                continue;
            }

            AnalyzeUsingDeclaration(
                context,
                executableBody,
                localDecl,
                semanticModel,
                registrationCollector,
                wellKnownTypes,
                reportedSpans);
        }

        AnalyzeExplicitScopeDisposals(
            context,
            executableBody,
            semanticModel,
            registrationCollector,
            wellKnownTypes,
            reportedSpans);
    }

    private static void AnalyzeUsingStatement(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        if (!TryGetScopeSymbolFromUsingStatement(usingStmt, semanticModel, wellKnownTypes, out var scopeSymbol))
        {
            return;
        }

        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
        if (usingStmt.Statement is not null)
        {
            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(usingStmt.Statement))
            {
                TrackProviderAlias(node, semanticModel, scopeSymbol, providerAliases);
                TrackServiceAlias(node, semanticModel, serviceVariables);
                TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

                if (node is not InvocationExpressionSyntax invocation ||
                    !TryGetResolutionLifetime(
                        invocation,
                        semanticModel,
                        scopeSymbol,
                        providerAliases,
                        registrationCollector,
                        wellKnownTypes,
                        out var lifetime) ||
                    !ShouldReportUseAfterDispose(lifetime))
                {
                    continue;
                }

                var assignedVariable = GetAssignedVariableSymbol(invocation, semanticModel);
                if (assignedVariable is not null)
                {
                    serviceVariables[assignedVariable] = invocation;
                }
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        var usingEndPosition = usingStmt.Span.End;
        // The using statement is the dispose site: a use in a mutually exclusive branch
        // never sees the disposed instance.
        ReportUsageAfterPosition(
            context,
            executableBody,
            semanticModel,
            serviceVariables,
            capturedDelegateVariables,
            usingEndPosition,
            reportedSpans,
            new List<SyntaxNode> { usingStmt });
    }

    private static void AnalyzeUsingDeclaration(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        LocalDeclarationStatementSyntax localDecl,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        if (!TryGetScopeSymbolFromUsingDeclaration(localDecl, semanticModel, wellKnownTypes, out var scopeSymbol))
        {
            return;
        }

        if (localDecl.Parent is not BlockSyntax containingBlock)
        {
            return;
        }

        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(containingBlock))
        {
            if (node.SpanStart < localDecl.SpanStart)
            {
                continue;
            }

            TrackProviderAlias(node, semanticModel, scopeSymbol, providerAliases);
            TrackServiceAlias(node, semanticModel, serviceVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeSymbol,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    out var lifetime) ||
                !ShouldReportUseAfterDispose(lifetime))
            {
                continue;
            }

            var assignedVariable = GetAssignedVariableSymbol(invocation, semanticModel);
            if (assignedVariable is not null)
            {
                serviceVariables[assignedVariable] = invocation;
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        var blockEndPosition = containingBlock.Span.End;
        // The declaration's containing block is the dispose site: a use in a mutually
        // exclusive branch never sees the disposed instance.
        ReportUsageAfterPosition(
            context,
            executableBody,
            semanticModel,
            serviceVariables,
            capturedDelegateVariables,
            blockEndPosition,
            reportedSpans,
            new List<SyntaxNode> { localDecl });
    }

    private static void AnalyzeExplicitScopeDisposals(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        HashSet<TextSpan> reportedSpans)
    {
        foreach (var scope in CollectExplicitScopeVariables(executableBody, semanticModel, wellKnownTypes))
        {
            var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
            var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
            var capturedDelegateVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
            int? disposePosition = null;
            List<SyntaxNode>? disposeNodes = null;

            foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
            {
                if (node.SpanStart < scope.CreationPosition)
                {
                    continue;
                }

                if (node is InvocationExpressionSyntax disposeInvocation &&
                    IsDisposeInvocationForScope(disposeInvocation, semanticModel, scope.Symbol))
                {
                    // Every dispose site matters: a branch mutually exclusive with the first
                    // dispose can contain its own dispose-then-use. Variable tracking still
                    // stops at the first site.
                    disposePosition = disposeInvocation.Span.End;
                    disposeNodes = new List<SyntaxNode> { disposeInvocation };
                    foreach (var later in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
                    {
                        if (later.SpanStart > disposeInvocation.SpanStart &&
                            later is InvocationExpressionSyntax laterDispose &&
                            IsDisposeInvocationForScope(laterDispose, semanticModel, scope.Symbol))
                        {
                            disposeNodes.Add(laterDispose);
                        }
                    }

                    break;
                }

                TrackProviderAlias(node, semanticModel, scope.Symbol, providerAliases);
                TrackServiceAlias(node, semanticModel, serviceVariables);
                TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

                if (node is not InvocationExpressionSyntax invocation ||
                    !TryGetResolutionLifetime(
                        invocation,
                        semanticModel,
                        scope.Symbol,
                        providerAliases,
                        registrationCollector,
                        wellKnownTypes,
                        out var lifetime) ||
                    !ShouldReportUseAfterDispose(lifetime))
                {
                    continue;
                }

                var assignedVariable = GetAssignedVariableSymbol(invocation, semanticModel);
                if (assignedVariable is not null)
                {
                    serviceVariables[assignedVariable] = invocation;
                }
            }

            if (disposePosition is null || serviceVariables.Count == 0 && capturedDelegateVariables.Count == 0)
            {
                continue;
            }

            ReportUsageAfterPosition(
                context,
                executableBody,
                semanticModel,
                serviceVariables,
                capturedDelegateVariables,
                disposePosition.Value,
                reportedSpans,
                disposeNodes);
        }
    }

    /// <summary>
    /// True when <paramref name="use"/> sits in a branch that is mutually exclusive with the
    /// branch containing <paramref name="dispose"/>: the opposite arm of the same if/else, or a
    /// different section of the same switch. Such a pair cannot both execute on one path.
    /// </summary>
    private static bool OutWriteDominatesLaterUses(SyntaxNode outInvocation, SyntaxNode executableBody)
    {
        // A conditional out-write only refreshes the local on its own branch; the
        // fall-through path still holds the disposed instance, so tracking is kept
        // (the out argument itself is still never a use).
        if (outInvocation.FirstAncestorOrSelf<StatementSyntax>() is { } containingStatement &&
            IsShortCircuited(outInvocation, containingStatement))
        {
            return false;
        }

        for (SyntaxNode? current = outInvocation.Parent; current is not null && current != executableBody; current = current.Parent)
        {
            // An out-call inside the construct's condition runs before any branching, so it
            // rewrites the local on every path that reaches code after the construct.
            var conditionSpan = current switch
            {
                IfStatementSyntax ifStatement => ifStatement.Condition.Span,
                WhileStatementSyntax whileStatement => whileStatement.Condition.Span,
                SwitchStatementSyntax switchStatement => switchStatement.Expression.Span,
                ForStatementSyntax { Condition: { } forCondition } => forCondition.Span,
                _ => default(Microsoft.CodeAnalysis.Text.TextSpan?)
            };
            if (conditionSpan is { } span && span.Contains(outInvocation.Span) &&
                !IsShortCircuited(outInvocation, current))
            {
                current = current.Parent is ElseClauseSyntax elseClause ? elseClause : current;
                continue;
            }

            if (current is IfStatementSyntax or
                ElseClauseSyntax or
                SwitchStatementSyntax or
                SwitchSectionSyntax or
                ConditionalExpressionSyntax or
                ConditionalAccessExpressionSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax or
                CatchClauseSyntax)
            {
                return false;
            }

            // Inside a try with handlers the out-call may throw before assigning while the
            // catch swallows the exception and execution continues to the later use.
            if (current is TryStatementSyntax tryStatement &&
                tryStatement.Catches.Count > 0 &&
                tryStatement.Block.Span.Contains(outInvocation.Span))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsShortCircuited(SyntaxNode outInvocation, SyntaxNode conditionOwner)
    {
        // The right operand of && / || (and the arms of ?:) may never evaluate, so an
        // out-call there does not rewrite the local on every path.
        for (SyntaxNode? current = outInvocation.Parent; current is not null && current != conditionOwner; current = current.Parent)
        {
            if (current is BinaryExpressionSyntax binary &&
                (binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                 binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                 binary.IsKind(SyntaxKind.CoalesceExpression)) &&
                binary.Right.Span.Contains(outInvocation.Span))
            {
                return true;
            }

            if (current is ConditionalExpressionSyntax conditional &&
                !conditional.Condition.Span.Contains(outInvocation.Span))
            {
                return true;
            }

            // `maybe ??= TryReplace(out service)` skips the RHS when maybe is non-null.
            if (current is AssignmentExpressionSyntax coalesceAssignment &&
                coalesceAssignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                coalesceAssignment.Right.Span.Contains(outInvocation.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInMutuallyExclusiveBranch(SyntaxNode use, SyntaxNode dispose)
    {
        for (SyntaxNode? current = dispose.Parent; current is not null; current = current.Parent)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                var disposeInThen = ifStatement.Statement.Span.Contains(dispose.Span);
                if (disposeInThen &&
                    ifStatement.Else is { } elseClause &&
                    elseClause.Statement.Span.Contains(use.Span))
                {
                    return true;
                }

                if (!disposeInThen && ifStatement.Statement.Span.Contains(use.Span))
                {
                    return true;
                }
            }

            if (current is SwitchSectionSyntax disposeSection &&
                disposeSection.Parent is SwitchStatementSyntax switchStatement)
            {
                foreach (var section in switchStatement.Sections)
                {
                    // goto case / goto default chains sections onto one execution path — but
                    // only the chain actually reachable from the dispose's section matters.
                    if (section != disposeSection &&
                        section.Span.Contains(use.Span) &&
                        !CanReachSectionViaGoto(switchStatement, disposeSection, section))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether control can flow from <paramref name="from"/> to <paramref name="to"/> through
    /// goto case / goto default edges of the same switch. Plain label gotos and unresolvable
    /// targets are treated as reaching everything (conservative).
    /// </summary>
    private static bool CanReachSectionViaGoto(
        SwitchStatementSyntax switchStatement,
        SwitchSectionSyntax from,
        SwitchSectionSyntax to)
    {
        var visited = new HashSet<SwitchSectionSyntax> { from };
        var queue = new Queue<SwitchSectionSyntax>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var section = queue.Dequeue();
            foreach (var gotoStatement in section.DescendantNodes().OfType<GotoStatementSyntax>())
            {
                // A goto inside a nested switch targets that switch, not this one.
                if (gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault() !=
                    switchStatement)
                {
                    continue;
                }

                var target = ResolveGotoTargetSection(switchStatement, gotoStatement);
                if (target is null)
                {
                    // Plain `goto label` or an unresolvable case value: assume it can reach.
                    return true;
                }

                if (target == to)
                {
                    return true;
                }

                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return false;
    }

    private static SwitchSectionSyntax? ResolveGotoTargetSection(
        SwitchStatementSyntax switchStatement,
        GotoStatementSyntax gotoStatement)
    {
        if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            return switchStatement.Sections.FirstOrDefault(
                section => section.Labels.Any(label => label is DefaultSwitchLabelSyntax));
        }

        if (gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) && gotoStatement.Expression is { } caseValue)
        {
            var normalized = caseValue.ToString();
            return switchStatement.Sections.FirstOrDefault(section => section.Labels.Any(
                label => label is CaseSwitchLabelSyntax caseLabel &&
                         caseLabel.Value.ToString() == normalized));
        }

        return null;
    }

    private static void ReportUsageAfterPosition(
        CompilationAnalysisContext context,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, string> capturedDelegateVariables,
        int position,
        HashSet<TextSpan> reportedSpans,
        List<SyntaxNode>? disposeNodes = null)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node.SpanStart < position)
            {
                continue;
            }

            // Alias/reassignment tracking must observe every node — a reassignment in a branch
            // exclusive with the first dispose still changes what the local refers to before
            // that branch's own dispose.
            TrackServiceAlias(node, semanticModel, serviceVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            // A use reports only when SOME dispose site precedes it on a path it shares: an
            // explicit Dispose() in one if/else arm or switch section cannot have run when
            // execution is in a mutually exclusive sibling branch, but that sibling's own
            // dispose still counts.
            if (disposeNodes is not null &&
                !disposeNodes.Any(dispose =>
                    dispose.Span.End <= node.SpanStart && !IsInMutuallyExclusiveBranch(node, dispose)))
            {
                continue;
            }

            if (node is InvocationExpressionSyntax delegateInvocation &&
                delegateInvocation.Expression is IdentifierNameSyntax delegateIdentifier &&
                semanticModel.GetSymbolInfo(delegateIdentifier).Symbol is ILocalSymbol delegateSymbol &&
                capturedDelegateVariables.TryGetValue(delegateSymbol, out var capturedServiceName))
            {
                ReportDiagnostic(context, delegateInvocation, capturedServiceName, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax assignment &&
                TryGetTrackedLocalReference(assignment.Right, semanticModel, serviceVariables, out var assignedServiceName) &&
                (assignment.Left is TupleExpressionSyntax ||
                 semanticModel.GetSymbolInfo(assignment.Left).Symbol is IFieldSymbol or IPropertySymbol or IParameterSymbol))
            {
                ReportDiagnostic(context, assignment.Right, assignedServiceName, reportedSpans);
            }

            if (node is InvocationExpressionSyntax invocationAfter &&
                invocationAfter.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol symbol &&
                serviceVariables.ContainsKey(symbol))
            {
                ReportDiagnostic(context, invocationAfter, identifier.Identifier.Text, reportedSpans);
            }

            if (node is InvocationExpressionSyntax invocationWithTrackedArgument)
            {
                // The out-write only lands after the entire argument list is evaluated and
                // the call returns, so removal is deferred: `TryReplace(out service, service)`
                // still reads the disposed instance through its second argument.
                List<ILocalSymbol>? rewrittenByOut = null;
                foreach (var argument in invocationWithTrackedArgument.ArgumentList.Arguments)
                {
                    if (argument.Expression is not IdentifierNameSyntax argumentIdentifier ||
                        semanticModel.GetSymbolInfo(argumentIdentifier).Symbol is not ILocalSymbol argumentSymbol ||
                        !serviceVariables.ContainsKey(argumentSymbol))
                    {
                        continue;
                    }

                    // An out argument writes the local without reading the disposed
                    // instance, and the rewritten local refers to a fresh value afterwards.
                    if (argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                    {
                        (rewrittenByOut ??= new List<ILocalSymbol>()).Add(argumentSymbol);
                        continue;
                    }

                    ReportDiagnostic(context, argumentIdentifier, argumentIdentifier.Identifier.Text, reportedSpans);
                }

                if (rewrittenByOut is not null && OutWriteDominatesLaterUses(invocationWithTrackedArgument, executableBody))
                {
                    foreach (var rewritten in rewrittenByOut)
                    {
                        serviceVariables.Remove(rewritten);

                        // A closure observes the reassigned local, so delegates that
                        // captured it now see the fresh instance.
                        foreach (var staleDelegate in capturedDelegateVariables
                                     .Where(pair => pair.Value == rewritten.Name)
                                     .Select(pair => pair.Key)
                                     .ToList())
                        {
                            capturedDelegateVariables.Remove(staleDelegate);
                        }
                    }
                }
            }

            if (node is ForEachStatementSyntax foreachStatement &&
                foreachStatement.Expression is IdentifierNameSyntax foreachIdentifier &&
                semanticModel.GetSymbolInfo(foreachIdentifier).Symbol is ILocalSymbol foreachSymbol &&
                serviceVariables.ContainsKey(foreachSymbol))
            {
                ReportDiagnostic(context, foreachIdentifier, foreachIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.Expression is IdentifierNameSyntax conditionalIdentifier &&
                semanticModel.GetSymbolInfo(conditionalIdentifier).Symbol is ILocalSymbol conditionalSymbol &&
                serviceVariables.ContainsKey(conditionalSymbol))
            {
                ReportDiagnostic(context, conditionalAccess, conditionalIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is ElementAccessExpressionSyntax elementAccess &&
                elementAccess.Expression is IdentifierNameSyntax elementIdentifier &&
                semanticModel.GetSymbolInfo(elementIdentifier).Symbol is ILocalSymbol elementSymbol &&
                serviceVariables.ContainsKey(elementSymbol))
            {
                ReportDiagnostic(context, elementAccess, elementIdentifier.Identifier.Text, reportedSpans);
            }

            if (node is MemberAccessExpressionSyntax memberAccessAfter &&
                memberAccessAfter.Parent is not InvocationExpressionSyntax &&
                memberAccessAfter.Expression is IdentifierNameSyntax identifierAccess &&
                semanticModel.GetSymbolInfo(identifierAccess).Symbol is ILocalSymbol symbolAccess &&
                serviceVariables.ContainsKey(symbolAccess))
            {
                ReportDiagnostic(context, memberAccessAfter, identifierAccess.Identifier.Text, reportedSpans);
            }

            if (node is ReturnStatementSyntax { Expression: IdentifierNameSyntax returnIdentifier } &&
                semanticModel.GetSymbolInfo(returnIdentifier).Symbol is ILocalSymbol returnSymbol &&
                serviceVariables.ContainsKey(returnSymbol))
            {
                ReportDiagnostic(context, returnIdentifier, returnIdentifier.Identifier.Text, reportedSpans);
            }
        }
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        SyntaxNode locationNode,
        string serviceVariableName,
        HashSet<TextSpan> reportedSpans)
    {
        if (!reportedSpans.Add(locationNode.Span))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UseAfterScopeDisposed,
            locationNode.GetLocation(),
            serviceVariableName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryGetScopeSymbolFromUsingStatement(
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (usingStmt.Declaration is not null)
        {
            foreach (var variable in usingStmt.Declaration.Variables)
            {
                if (UnwrapToInvocation(variable.Initializer?.Value) is not { } invocation ||
                    !IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                scopeSymbol = localSymbol;
                return true;
            }
        }

        return TryGetExistingScopeSymbol(usingStmt.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
    }

    private static bool TryGetScopeSymbolFromUsingDeclaration(
        LocalDeclarationStatementSyntax localDecl,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        foreach (var variable in localDecl.Declaration.Variables)
        {
            if (UnwrapToInvocation(variable.Initializer?.Value) is not { } invocation ||
                !IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) ||
                semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
            {
                continue;
            }

            scopeSymbol = localSymbol;
            return true;
        }

        return false;
    }

    private static bool TryGetExistingScopeSymbol(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetExistingScopeSymbol(parenthesized.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
        }

        if (expression is not IdentifierNameSyntax identifierName ||
            semanticModel.GetSymbolInfo(identifierName).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } initializer } &&
                UnwrapToInvocation(initializer) is { } invocation &&
                IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes))
            {
                scopeSymbol = localSymbol;
                return true;
            }
        }

        return false;
    }

    private static bool IsCreateScopeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (methodSymbol.Name is not ("CreateScope" or "CreateAsyncScope"))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var containingType = sourceMethod.ContainingType;

        if (containingType is null)
        {
            return false;
        }

        if (wellKnownTypes.IsServiceScopeFactory(containingType))
        {
            return true;
        }

        return containingType.Name == "ServiceProviderServiceExtensions" &&
               containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static ILocalSymbol? GetAssignedVariableSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // A conditional-access resolution (`scope?.ServiceProvider.GetRequiredService<T>()`)
        // hangs the assignment/initializer off the enclosing ConditionalAccessExpressionSyntax
        // rather than the invocation itself.
        SyntaxNode consumption = invocation;
        while (consumption.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
               conditionalAccess.WhenNotNull == consumption)
        {
            consumption = conditionalAccess;
        }

        if (consumption.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
            semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignmentSymbol)
        {
            return assignmentSymbol;
        }

        if (consumption.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator &&
            semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaratorSymbol)
        {
            return declaratorSymbol;
        }

        return null;
    }

    /// <summary>
    /// Unwraps a conditional-access creation expression (`factory?.CreateScope()`) to the
    /// invocation it evaluates, or returns the expression itself when it already is one.
    /// </summary>
    private static InvocationExpressionSyntax? UnwrapToInvocation(ExpressionSyntax? expression) =>
        expression switch
        {
            InvocationExpressionSyntax invocation => invocation,
            ConditionalAccessExpressionSyntax conditionalAccess => UnwrapToInvocation(conditionalAccess.WhenNotNull),
            _ => null,
        };

    /// <summary>
    /// Resolves the receiver that a conditional-access member binding is evaluated against: for
    /// `scope?.ServiceProvider` the `.ServiceProvider` binding's receiver is `scope`. The owning
    /// conditional access is the nearest ancestor whose <c>WhenNotNull</c> contains the binding --
    /// the binding can also be the <c>Expression</c> of an inner chained conditional access, which
    /// is not its owner.
    /// </summary>
    private static ExpressionSyntax? TryGetConditionalAccessReceiver(MemberBindingExpressionSyntax memberBinding)
    {
        for (SyntaxNode? node = memberBinding.Parent; node is not null; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(memberBinding.Span))
            {
                return conditionalAccess.Expression;
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    private readonly struct ScopeVariable
    {
        public ScopeVariable(ILocalSymbol symbol, int creationPosition)
        {
            Symbol = symbol;
            CreationPosition = creationPosition;
        }

        public ILocalSymbol Symbol { get; }

        public int CreationPosition { get; }
    }

    private static IEnumerable<ScopeVariable> CollectExplicitScopeVariables(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0 } localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (UnwrapToInvocation(variable.Initializer?.Value) is { } invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                    {
                        yield return new ScopeVariable(localSymbol, localDeclaration.SpanStart);
                    }
                }
            }

            if (node is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax leftIdentifier &&
                UnwrapToInvocation(assignment.Right) is { } assignedInvocation &&
                IsCreateScopeInvocation(assignedInvocation, semanticModel, wellKnownTypes) &&
                semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol assignedScope)
            {
                yield return new ScopeVariable(assignedScope, assignment.SpanStart);
            }
        }
    }

    private static bool IsDisposeInvocationForScope(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text is not ("Dispose" or "DisposeAsync") ||
            memberAccess.Expression is not IdentifierNameSyntax scopeIdentifier ||
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(localSymbol, scopeSymbol);
    }

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(
                invocation,
                semanticModel,
                scopeSymbol,
                providerAliases,
                wellKnownTypes,
                out var serviceType,
                out var key,
                out var isKeyed,
                out var isCollectionResolution))
        {
            return false;
        }

        if (serviceType is null)
        {
            return true;
        }

        lifetime = isCollectionResolution
            ? GetCollectionResolutionLifetime(registrationCollector, serviceType, key, isKeyed)
            : registrationCollector.GetLifetime(serviceType, key, isKeyed);

        return true;
    }

    private static bool TryGetResolvedServiceInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol? serviceType,
        out object? key,
        out bool isKeyed,
        out bool isCollectionResolution)
    {
        serviceType = null;
        key = null;
        isKeyed = false;
        isCollectionResolution = false;

        // The provider is the receiver of the resolution member itself: the member-access
        // expression, or -- for `provider?.GetRequiredService<T>()` member bindings -- the
        // enclosing conditional access's expression.
        ExpressionSyntax? providerExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax outerMember => outerMember.Expression,
            MemberBindingExpressionSyntax when invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                                               conditionalAccess.WhenNotNull == invocation => conditionalAccess.Expression,
            _ => null,
        };

        if (providerExpression is null ||
            !TryResolveProviderScope(
                providerExpression,
                semanticModel,
                scopeSymbol,
                providerAliases,
                out _))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!IsServiceResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService";
        isCollectionResolution = sourceMethod.Name == "GetServices";

        serviceType = GetResolvedServiceType(invocation, methodSymbol, semanticModel);
        if (isKeyed)
        {
            if (!TryExtractKey(invocation, methodSymbol, semanticModel, out key))
            {
                return false;
            }
        }

        return true;
    }

    private static ServiceLifetime? GetCollectionResolutionLifetime(
        RegistrationCollector registrationCollector,
        ITypeSymbol serviceType,
        object? key,
        bool isKeyed)
    {
        ServiceLifetime? singletonLifetime = null;

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key) ||
                !IsMatchingRegistration(registration.ServiceType, serviceType))
            {
                continue;
            }

            if (registration.Lifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient)
            {
                return registration.Lifetime;
            }

            singletonLifetime = registration.Lifetime;
        }

        return singletonLifetime;
    }

    private static bool IsMatchingRegistration(INamedTypeSymbol registeredType, ITypeSymbol requestedType)
    {
        if (SymbolEqualityComparer.Default.Equals(registeredType, requestedType))
        {
            return true;
        }

        if (requestedType is INamedTypeSymbol requestedNamedType &&
            requestedNamedType.IsGenericType &&
            !requestedNamedType.IsUnboundGenericType)
        {
            var openRequestedType = requestedNamedType.ConstructUnboundGenericType();
            return SymbolEqualityComparer.Default.Equals(registeredType, openRequestedType);
        }

        return false;
    }

    private static void TrackProviderAlias(
        SyntaxNode node,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                if (variable.Initializer?.Value is null ||
                    !TryResolveProviderScope(
                        variable.Initializer.Value,
                        semanticModel,
                        scopeSymbol,
                        providerAliases,
                        out var resolvedScope))
                {
                    providerAliases.Remove(localSymbol);
                    continue;
                }

                providerAliases[localSymbol] = resolvedScope;
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (!TryResolveProviderScope(
                    assignment.Right,
                    semanticModel,
                    scopeSymbol,
                    providerAliases,
                    out var assignmentScope))
            {
                providerAliases.Remove(leftLocal);
                return;
            }

            providerAliases[leftLocal] = assignmentScope;
        }
    }

    private static bool TryResolveProviderScope(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ILocalSymbol scopeSymbol,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        out ILocalSymbol resolvedScope)
    {
        resolvedScope = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryResolveProviderScope(
                parenthesized.Expression,
                semanticModel,
                scopeSymbol,
                providerAliases,
                out resolvedScope);
        }

        // `scope?.ServiceProvider` evaluates to the scope's provider when not null.
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return TryResolveProviderScope(
                conditionalAccess.WhenNotNull,
                semanticModel,
                scopeSymbol,
                providerAliases,
                out resolvedScope);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ServiceProvider" &&
            memberAccess.Expression is IdentifierNameSyntax scopeIdentifier &&
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is ILocalSymbol directScope &&
            SymbolEqualityComparer.Default.Equals(directScope, scopeSymbol))
        {
            resolvedScope = directScope;
            return true;
        }

        // Conditional-access form: in `scope?.ServiceProvider.GetRequiredService<T>()` the
        // provider surfaces as a `.ServiceProvider` member binding whose receiver is the
        // enclosing conditional access's expression.
        if (expression is MemberBindingExpressionSyntax memberBinding &&
            memberBinding.Name.Identifier.Text == "ServiceProvider" &&
            TryGetConditionalAccessReceiver(memberBinding) is IdentifierNameSyntax bindingScopeIdentifier &&
            semanticModel.GetSymbolInfo(bindingScopeIdentifier).Symbol is ILocalSymbol bindingScope &&
            SymbolEqualityComparer.Default.Equals(bindingScope, scopeSymbol))
        {
            resolvedScope = bindingScope;
            return true;
        }

        if (expression is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol providerLocal &&
            providerAliases.TryGetValue(providerLocal, out var aliasScope) &&
            SymbolEqualityComparer.Default.Equals(aliasScope, scopeSymbol))
        {
            resolvedScope = aliasScope;
            return true;
        }

        return false;
    }

    private static void TrackServiceAlias(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not IdentifierNameSyntax identifier ||
                    semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol sourceLocal ||
                    !serviceVariables.TryGetValue(sourceLocal, out var sourceInvocation) ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol aliasLocal)
                {
                    continue;
                }

                serviceVariables[aliasLocal] = sourceInvocation;
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (assignment.Right is InvocationExpressionSyntax &&
                serviceVariables.TryGetValue(leftLocal, out var existingInvocation) &&
                existingInvocation == assignment.Right)
            {
                return;
            }

            if (assignment.Right is not IdentifierNameSyntax rightIdentifier ||
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is not ILocalSymbol rightLocal ||
                !serviceVariables.TryGetValue(rightLocal, out var invocation))
            {
                serviceVariables.Remove(leftLocal);
                return;
            }

            serviceVariables[leftLocal] = invocation;
        }
    }

    private static void TrackDelegateCapture(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, string> capturedDelegateVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol delegateLocal)
                {
                    continue;
                }

                if (variable.Initializer?.Value is AnonymousFunctionExpressionSyntax anonymousFunction)
                {
                    if (TryFindCapturedServiceName(anonymousFunction, semanticModel, serviceVariables, out var capturedServiceName))
                    {
                        capturedDelegateVariables[delegateLocal] = capturedServiceName;
                    }

                    continue;
                }

                if (variable.Initializer?.Value is IdentifierNameSyntax identifier &&
                    semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol sourceDelegate &&
                    capturedDelegateVariables.TryGetValue(sourceDelegate, out var aliasedCapturedServiceName))
                {
                    capturedDelegateVariables[delegateLocal] = aliasedCapturedServiceName;
                }
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol assignedDelegate)
        {
            if (assignment.Right is AnonymousFunctionExpressionSyntax anonymousFunction &&
                TryFindCapturedServiceName(anonymousFunction, semanticModel, serviceVariables, out var capturedServiceName))
            {
                capturedDelegateVariables[assignedDelegate] = capturedServiceName;
                return;
            }

            if (assignment.Right is IdentifierNameSyntax rightIdentifier &&
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is ILocalSymbol sourceDelegate &&
                capturedDelegateVariables.TryGetValue(sourceDelegate, out var aliasedCapturedServiceName))
            {
                capturedDelegateVariables[assignedDelegate] = aliasedCapturedServiceName;
                return;
            }

            capturedDelegateVariables.Remove(assignedDelegate);
        }
    }

    private static bool TryFindCapturedServiceName(
        AnonymousFunctionExpressionSyntax anonymousFunction,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out string serviceName)
    {
        foreach (var identifier in anonymousFunction.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
                serviceVariables.ContainsKey(localSymbol))
            {
                serviceName = identifier.Identifier.Text;
                return true;
            }
        }

        serviceName = string.Empty;
        return false;
    }

    private static bool TryGetTrackedLocalReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out string serviceName)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetTrackedLocalReference(parenthesized.Expression, semanticModel, serviceVariables, out serviceName);
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
            serviceVariables.ContainsKey(localSymbol))
        {
            serviceName = identifier.Identifier.Text;
            return true;
        }

        serviceName = string.Empty;
        return false;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol, WellKnownTypes wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetServices" or "GetKeyedService" or "GetRequiredKeyedService"))
        {
            return false;
        }

        var containingType = sourceMethod.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        if (containingType.Name == "ServiceProviderServiceExtensions" &&
            containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            return true;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            if (IsSystemIServiceProvider(receiverType) ||
                wellKnownTypes.IsKeyedServiceProvider(receiverType))
            {
                return true;
            }
        }

        return IsSystemIServiceProvider(containingType) ||
               wellKnownTypes.IsKeyedServiceProvider(containingType);
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
               type.ContainingNamespace.ToDisplayString() == "System";
    }

    private static ITypeSymbol? GetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            return methodSymbol.TypeArguments[0];
        }

        var serviceTypeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");
        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        return null;
    }

    private static bool TryExtractKey(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out object? key)
    {
        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            key = null;
            return false;
        }

        var constant = semanticModel.GetConstantValue(keyExpression);
        key = constant.HasValue ? constant.Value : null;
        return constant.HasValue;
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

    private static bool ShouldReportUseAfterDispose(ServiceLifetime? lifetime)
    {
        // Unknown lifetime should not produce a warning to avoid false positives
        // when registration metadata cannot be resolved.
        return lifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;
    }
}
