using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects IServiceProvider instances created by BuildServiceProvider that are not properly disposed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI014_RootProviderNotDisposedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RootProviderNotDisposed);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Check if this is BuildServiceProvider
        var methodName = invocation.TargetMethod.Name;
        if (methodName != "BuildServiceProvider")
        {
            return;
        }

        // Verify it's on ServiceCollectionContainerBuilderExtensions
        var containingType = invocation.TargetMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionContainerBuilderExtensions" ||
            containingType.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return;
        }

        #pragma warning disable RS1030
        var semanticModel = context.Compilation.GetSemanticModel(invocation.Syntax.SyntaxTree);
        #pragma warning restore RS1030

        // Check if the result is properly handled
        if (IsProperlyDisposed(invocation, semanticModel))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RootProviderNotDisposed,
            invocation.Syntax.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsProperlyDisposed(IInvocationOperation invocation, SemanticModel semanticModel)
    {
        // Case 1: Result is used in a using statement or declaration
        if (IsInUsingContext(invocation))
        {
            return true;
        }

        // Case 2: Result is returned from the method (caller responsibility)
        if (IsReturned(invocation))
        {
            return true;
        }

        // Case 3: Result is assigned to a variable that is later disposed
        if (IsExplicitlyDisposed(invocation, semanticModel))
        {
            return true;
        }

        return false;
    }

    private static bool IsInUsingContext(IInvocationOperation invocation)
    {
        // Walk up the syntax tree to find if we're in a using statement/declaration
        var syntax = invocation.Syntax;
        var parent = syntax.Parent;

        while (parent is not null)
        {
            // using var provider = ...;
            if (parent is VariableDeclarationSyntax varDecl &&
                varDecl.Parent is LocalDeclarationStatementSyntax localDecl &&
                localDecl.UsingKeyword != default)
            {
                return true;
            }

            // using (var provider = ...) { } OR using (services.BuildServiceProvider()) { }
            if (parent is UsingStatementSyntax usingStatement &&
                IsUsingStatementResource(syntax, usingStatement))
            {
                return true;
            }

            // await using var provider = ...;
            if (parent is VariableDeclarationSyntax varDecl2 &&
                varDecl2.Parent is LocalDeclarationStatementSyntax localDecl2 &&
                localDecl2.AwaitKeyword != default)
            {
                return true;
            }

            // Stop at method/lambda boundaries
            if (parent is MethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                LambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax)
            {
                break;
            }

            parent = parent.Parent;
        }

        return false;
    }

    private static bool IsUsingStatementResource(SyntaxNode invocationSyntax, UsingStatementSyntax usingStatement)
    {
        if (usingStatement.Declaration is { } declaration &&
            declaration.Span.Contains(invocationSyntax.Span))
        {
            return true;
        }

        if (usingStatement.Expression is { } expression &&
            expression.Span.Contains(invocationSyntax.Span))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the expression whose parent decides how the created provider is consumed. A
    /// conditional-access creation (`services?.BuildServiceProvider()`) wraps the invocation in a
    /// <see cref="ConditionalAccessExpressionSyntax"/>, so the consumption shape (initializer,
    /// assignment, return statement, arrow body) hangs off the conditional access rather than
    /// the invocation itself.
    /// </summary>
    private static SyntaxNode GetCreationExpression(SyntaxNode syntax)
    {
        var current = syntax;
        while (current.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
               conditionalAccess.WhenNotNull == current)
        {
            current = conditionalAccess;
        }

        return current;
    }

    private static bool IsReturned(IInvocationOperation invocation)
    {
        var parent = invocation.Parent;

        if (parent is IReturnOperation)
        {
            return true;
        }

        var creationExpression = GetCreationExpression(invocation.Syntax);
        if (creationExpression.Parent is ReturnStatementSyntax)
        {
            return true;
        }

        if (creationExpression.Parent is ArrowExpressionClauseSyntax)
        {
            return true;
        }

        return false;
    }

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation, SemanticModel semanticModel)
    {
        var creationExpression = GetCreationExpression(invocation.Syntax);
        var assignmentTarget = GetAssignmentTarget(creationExpression, semanticModel);
        if (assignmentTarget is null)
        {
            return false;
        }

        var (targetSymbol, containingType) = assignmentTarget.Value;
        if (targetSymbol is ILocalSymbol localSymbol)
        {
            var searchRoot = GetExecutableBoundary(creationExpression) ?? GetContainingBlock(creationExpression);
            if (searchRoot is null)
            {
                return false;
            }

            return HasDisposeCallInBlock(searchRoot, creationExpression, localSymbol, semanticModel);
        }

        if (targetSymbol is IFieldSymbol or IPropertySymbol)
        {
            return containingType is not null &&
                   HasDisposeCallInOwnerType(containingType, targetSymbol, semanticModel);
        }

        return false;
    }

    private static SyntaxNode? GetContainingBlock(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is BlockSyntax or MethodDeclarationSyntax)
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    private static bool HasDisposeCallInBlock(
        SyntaxNode block,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in block.DescendantNodes())
        {
            if (descendant is not InvocationExpressionSyntax invocationSyntax ||
                invocationSyntax.SpanStart <= creationSyntax.SpanStart)
            {
                continue;
            }

            if (!TryGetDisposedTargetSymbol(invocationSyntax, semanticModel, out var targetSymbol))
            {
                continue;
            }

            if (!SharesExecutableBoundary(invocationSyntax, creationSyntax))
            {
                continue;
            }

            if (!CreationCanReachDispose(creationSyntax, invocationSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            if (CreationMayRunRepeatedlyBeforeDispose(creationSyntax, invocationSyntax))
            {
                continue;
            }

            if (!IsReliableDisposeProof(invocationSyntax, creationSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(targetSymbol, variableSymbol))
            {
                if (HasInterveningReassignment(creationSyntax, invocationSyntax, variableSymbol, semanticModel))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool HasInterveningReassignment(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in creationSyntax.SyntaxTree.GetRoot().DescendantNodes(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(creationSyntax.Span.End, disposeSyntax.SpanStart)))
        {
            if (descendant is not AssignmentExpressionSyntax assignment)
            {
                continue;
            }

            if (!SharesExecutableBoundary(assignment, creationSyntax))
            {
                continue;
            }

            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            if (SymbolEqualityComparer.Default.Equals(assignedSymbol, variableSymbol))
            {
                if (AreInMutuallyExclusiveBranches(creationSyntax, assignment, semanticModel))
                {
                    continue;
                }

                return true;
            }
        }

        return HasSwitchGotoReassignment(creationSyntax, disposeSyntax, variableSymbol, semanticModel);
    }

    private static bool HasSwitchGotoReassignment(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var creationSection = creationSyntax.Ancestors().OfType<SwitchSectionSyntax>().FirstOrDefault();
        if (creationSection?.Parent is not SwitchStatementSyntax switchStatement)
        {
            return false;
        }

        foreach (var gotoStatement in creationSection.DescendantNodes().OfType<GotoStatementSyntax>())
        {
            if (gotoStatement.SpanStart <= creationSyntax.SpanStart ||
                !SharesExecutableBoundary(gotoStatement, creationSyntax) ||
                gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault() != switchStatement)
            {
                continue;
            }

            foreach (var targetSection in switchStatement.Sections)
            {
                if (!SwitchSectionMatchesGoto(targetSection, gotoStatement, semanticModel) ||
                    !SwitchSectionAssignsVariable(targetSection, disposeSyntax, variableSymbol, semanticModel))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool SwitchSectionMatchesGoto(
        SwitchSectionSyntax switchSection,
        GotoStatementSyntax gotoStatement,
        SemanticModel semanticModel)
    {
        if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            return switchSection.Labels.OfType<DefaultSwitchLabelSyntax>().Any();
        }

        if (!gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) ||
            gotoStatement.Expression is null)
        {
            return false;
        }

        var gotoConstant = semanticModel.GetConstantValue(gotoStatement.Expression);
        return switchSection.Labels
            .OfType<CaseSwitchLabelSyntax>()
            .Any(label =>
            {
                var labelConstant = semanticModel.GetConstantValue(label.Value);
                return gotoConstant.HasValue && labelConstant.HasValue
                    ? Equals(gotoConstant.Value, labelConstant.Value)
                    : label.Value.ToString() == gotoStatement.Expression.ToString();
            });
    }

    private static bool SwitchSectionAssignsVariable(
        SwitchSectionSyntax switchSection,
        SyntaxNode disposeSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var assignment in switchSection.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart >= disposeSyntax.SpanStart ||
                !SharesExecutableBoundary(assignment, disposeSyntax) ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol,
                    variableSymbol))
            {
                continue;
            }

            if (SwitchSectionDisposesVariableBeforeAssignment(switchSection, assignment, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool SwitchSectionDisposesVariableBeforeAssignment(
        SwitchSectionSyntax switchSection,
        AssignmentExpressionSyntax assignment,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var statement in switchSection.Statements)
        {
            if (statement.SpanStart >= assignment.SpanStart)
            {
                return false;
            }

            if (StatementReliablyDisposesTarget(statement, assignment, variableSymbol, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReliableDisposeProof(
        InvocationExpressionSyntax disposeSyntax,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var boundary = GetExecutableBoundary(creationSyntax);
        var current = disposeSyntax.Parent;
        while (current is not null && current != boundary)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (!IsNonNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel))
                {
                    return false;
                }
            }
            else if (current is ElseClauseSyntax or
                     SwitchStatementSyntax or
                     SwitchSectionSyntax or
                     ConditionalExpressionSyntax or
                     ForStatementSyntax or
                     ForEachStatementSyntax or
                     ForEachVariableStatementSyntax or
                     WhileStatementSyntax or
                     DoStatementSyntax or
                     CatchClauseSyntax)
            {
                return false;
            }

            current = current.Parent;
        }

        return true;
    }

    private static bool CreationCanReachDispose(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (DisposeRunsFromFinallyForCreation(creationSyntax, disposeSyntax))
        {
            return true;
        }

        foreach (var ifStatement in creationSyntax.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Span.Contains(disposeSyntax.Span))
            {
                continue;
            }

            var branch = GetIfBranchContainingSyntax(ifStatement, creationSyntax);
            if (branch is not null &&
                BranchExitsAfterCreation(branch, creationSyntax, variableSymbol, semanticModel))
            {
                return false;
            }
        }

        if (BlockExitsBeforeDispose(creationSyntax, disposeSyntax, variableSymbol, semanticModel))
        {
            return false;
        }

        return true;
    }

    private static bool DisposeRunsFromFinallyForCreation(SyntaxNode creationSyntax, SyntaxNode disposeSyntax)
    {
        var finallyClause = disposeSyntax.Ancestors().OfType<FinallyClauseSyntax>().FirstOrDefault();
        return finallyClause?.Parent is TryStatementSyntax tryStatement &&
               (tryStatement.Block.Span.Contains(creationSyntax.Span) ||
                tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(creationSyntax.Span)));
    }

    private static StatementSyntax? GetIfBranchContainingSyntax(
        IfStatementSyntax ifStatement,
        SyntaxNode syntax)
    {
        if (ifStatement.Statement.Span.Contains(syntax.Span))
        {
            return ifStatement.Statement;
        }

        if (ifStatement.Else?.Statement.Span.Contains(syntax.Span) == true)
        {
            return ifStatement.Else.Statement;
        }

        return null;
    }

    private static bool BlockExitsBeforeDispose(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var creationStatement = creationSyntax.FirstAncestorOrSelf<StatementSyntax>();
        if (creationStatement is null)
        {
            return false;
        }

        foreach (var block in creationStatement.Ancestors().OfType<BlockSyntax>())
        {
            if (!block.Span.Contains(disposeSyntax.Span))
            {
                continue;
            }

            if (block.DescendantNodes()
                .OfType<StatementSyntax>()
                .Any(statement =>
                    statement.SpanStart > creationSyntax.SpanStart &&
                    statement.SpanStart < disposeSyntax.SpanStart &&
                    !IsInsideCreationAncestorBranch(statement, creationSyntax) &&
                    !AreInMutuallyExclusiveBranches(creationSyntax, statement, semanticModel) &&
                    !IsCatchExitForCreationTryFailure(statement, creationSyntax) &&
                    IsUnconditionalExit(statement, block, creationSyntax, variableSymbol, semanticModel) &&
                    SharesExecutableBoundary(statement, creationSyntax) &&
                    !HasDisposeBeforeExit(block, statement, creationSyntax, variableSymbol, semanticModel)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideCreationAncestorBranch(StatementSyntax statement, SyntaxNode creationSyntax)
    {
        return creationSyntax.Ancestors().OfType<IfStatementSyntax>()
            .Any(ifStatement => ifStatement.Span.Contains(statement.Span));
    }

    private static bool BranchExitsAfterCreation(
        StatementSyntax branch,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        return branch.DescendantNodesAndSelf()
            .OfType<StatementSyntax>()
            .Any(statement =>
                statement.SpanStart > creationSyntax.SpanStart &&
                !AreInMutuallyExclusiveBranches(creationSyntax, statement, semanticModel) &&
                !IsCatchExitForCreationTryFailure(statement, creationSyntax) &&
                IsUnconditionalExit(statement, branch, creationSyntax, variableSymbol, semanticModel) &&
                SharesExecutableBoundary(statement, creationSyntax) &&
                !HasDisposeBeforeExit(branch, statement, creationSyntax, variableSymbol, semanticModel));
    }

    private static bool IsCatchExitForCreationTryFailure(StatementSyntax statement, SyntaxNode creationSyntax)
    {
        var catchClause = statement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (catchClause?.Parent is not TryStatementSyntax tryStatement ||
            !tryStatement.Block.Span.Contains(creationSyntax.Span))
        {
            return false;
        }

        var creationStatement = creationSyntax.FirstAncestorOrSelf<StatementSyntax>();
        if (creationStatement is null)
        {
            return false;
        }

        var directTryStatement = tryStatement.Block.Statements
            .FirstOrDefault(candidate => candidate.Span.Contains(creationStatement.Span));
        return directTryStatement == creationStatement &&
               tryStatement.Block.Statements[tryStatement.Block.Statements.Count - 1] == creationStatement;
    }

    private static bool IsUnconditionalExit(
        StatementSyntax statement,
        StatementSyntax branch,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        return statement switch
        {
            ReturnStatementSyntax => !IsNullGuardExit(statement, creationSyntax, variableSymbol, semanticModel),
            ThrowStatementSyntax throwStatement => !IsCaughtInsideBranch(throwStatement, branch, variableSymbol, semanticModel),
            _ => false,
        };
    }

    private static bool IsNullGuardExit(
        StatementSyntax statement,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var ifStatement in statement.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Statement.Span.Contains(statement.Span))
            {
                continue;
            }

            if (!ifStatement.Span.Contains(creationSyntax.Span) &&
                IsNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCaughtInsideBranch(
        ThrowStatementSyntax throwStatement,
        StatementSyntax branch,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var tryStatement in throwStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(throwStatement.Span))
            {
                continue;
            }

            var handled = CatchSequenceHandlesThrow(
                tryStatement.Catches,
                startIndex: 0,
                throwStatement,
                variableSymbol,
                semanticModel);
            if (handled.HasValue)
            {
                return handled.Value;
            }
        }

        return false;
    }

    private static bool? CatchSequenceHandlesThrow(
        SyntaxList<CatchClauseSyntax> catchClauses,
        int startIndex,
        ThrowStatementSyntax throwStatement,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        for (var index = startIndex; index < catchClauses.Count; index++)
        {
            var catchClause = catchClauses[index];
            if (!CatchCanHandleThrow(catchClause, throwStatement, semanticModel))
            {
                continue;
            }

            var catchHandlesThrow =
                CatchCanResume(catchClause) ||
                CatchReliablyDisposesBeforeExit(catchClause, throwStatement, variableSymbol, semanticModel) ||
                CatchRethrowsToCaughtOuterCatch(catchClause, variableSymbol, semanticModel);
            if (!catchHandlesThrow)
            {
                return false;
            }

            if (!CatchFilterCanBypass(catchClause, semanticModel))
            {
                return true;
            }

            return CatchSequenceHandlesThrow(
                catchClauses,
                index + 1,
                throwStatement,
                variableSymbol,
                semanticModel) == true;
        }

        return null;
    }

    private static bool CatchReliablyDisposesBeforeExit(
        CatchClauseSyntax catchClause,
        ThrowStatementSyntax throwStatement,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var exits = catchClause.Block.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement =>
                statement is (ReturnStatementSyntax or ThrowStatementSyntax) &&
                SharesExecutableBoundary(statement, catchClause))
            .ToArray();

        return exits.Length > 0 &&
               exits.All(exit =>
                   HasDisposeBeforeExit(catchClause.Block, exit, throwStatement, variableSymbol, semanticModel));
    }

    private static bool CatchRethrowsToCaughtOuterCatch(
        CatchClauseSyntax catchClause,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var rethrows = catchClause.Block.DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .Where(throwStatement =>
                throwStatement.Expression is null &&
                SharesExecutableBoundary(throwStatement, catchClause))
            .ToArray();

        return rethrows.Length > 0 &&
               rethrows.All(rethrow => IsCaughtInsideBranch(rethrow, catchClause.Block, variableSymbol, semanticModel));
    }

    private static bool CatchCanResume(CatchClauseSyntax catchClause)
    {
        return !catchClause.Block.DescendantNodes()
            .OfType<StatementSyntax>()
            .Any(statement =>
                statement is (ReturnStatementSyntax or ThrowStatementSyntax) &&
                SharesExecutableBoundary(statement, catchClause));
    }

    private static bool CatchFilterCanBypass(CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        if (catchClause.Filter is null)
        {
            return false;
        }

        var constant = semanticModel.GetConstantValue(catchClause.Filter.FilterExpression);
        return !constant.HasValue || constant.Value is not true;
    }

    private static bool CatchCanHandleThrow(
        CatchClauseSyntax catchClause,
        ThrowStatementSyntax throwStatement,
        SemanticModel semanticModel)
    {
        if (catchClause.Declaration is null)
        {
            return true;
        }

        if (throwStatement.Expression is null)
        {
            return CatchCanHandleRethrow(catchClause, throwStatement, semanticModel);
        }

        var caughtType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        var thrownType = semanticModel.GetTypeInfo(throwStatement.Expression).Type;
        return caughtType is not null &&
               thrownType is not null &&
               TypeDerivesFromOrEquals(thrownType, caughtType);
    }

    private static bool CatchCanHandleRethrow(
        CatchClauseSyntax catchClause,
        ThrowStatementSyntax throwStatement,
        SemanticModel semanticModel)
    {
        var caughtType = semanticModel.GetTypeInfo(catchClause.Declaration!.Type).Type;
        if (caughtType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Exception")
        {
            return true;
        }

        var sourceCatch = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (sourceCatch?.Declaration is null)
        {
            return false;
        }

        var rethrownType = semanticModel.GetTypeInfo(sourceCatch.Declaration.Type).Type;
        return caughtType is not null &&
               rethrownType is not null &&
               TypeDerivesFromOrEquals(rethrownType, caughtType);
    }

    private static bool TypeDerivesFromOrEquals(ITypeSymbol candidateType, ITypeSymbol targetType)
    {
        var currentType = candidateType;
        while (currentType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(currentType, targetType))
            {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }

    private static bool HasDisposeBeforeExit(
        StatementSyntax branch,
        StatementSyntax exitStatement,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (FinallyDisposesOnExit(exitStatement, creationSyntax, variableSymbol, semanticModel))
        {
            return true;
        }

        if (exitStatement.Parent is BlockSyntax exitBlock &&
            HasDirectDisposeBeforeExit(exitBlock, exitStatement, creationSyntax, variableSymbol, semanticModel))
        {
            return true;
        }

        return branch is BlockSyntax branchBlock &&
               HasDirectDisposeBeforeExit(branchBlock, exitStatement, creationSyntax, variableSymbol, semanticModel);
    }

    private static bool FinallyDisposesOnExit(
        StatementSyntax exitStatement,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var tryStatement in exitStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Finally is null ||
                !tryStatement.Block.Span.Contains(exitStatement.Span))
            {
                continue;
            }

            if (!tryStatement.Block.Span.Contains(creationSyntax.Span) &&
                tryStatement.SpanStart <= creationSyntax.SpanStart)
            {
                continue;
            }

            if (FinallyReliablyDisposesTarget(tryStatement.Finally, creationSyntax, variableSymbol, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FinallyReliablyDisposesTarget(
        FinallyClauseSyntax finallyClause,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var invocationSyntax in finallyClause.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!SharesExecutableBoundary(invocationSyntax, creationSyntax) ||
                !TryGetDisposedTargetSymbol(invocationSyntax, semanticModel, out var targetSymbol) ||
                !SymbolEqualityComparer.Default.Equals(targetSymbol, variableSymbol) ||
                !IsReliableFinallyDisposeProof(invocationSyntax, finallyClause, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsReliableFinallyDisposeProof(
        InvocationExpressionSyntax disposeSyntax,
        FinallyClauseSyntax finallyClause,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var current = disposeSyntax.Parent;
        while (current is not null && current != finallyClause)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (!IsNonNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel))
                {
                    return false;
                }
            }
            else if (current is ElseClauseSyntax or
                     SwitchStatementSyntax or
                     SwitchSectionSyntax or
                     ConditionalExpressionSyntax or
                     ForStatementSyntax or
                     ForEachStatementSyntax or
                     ForEachVariableStatementSyntax or
                     WhileStatementSyntax or
                     DoStatementSyntax or
                     CatchClauseSyntax)
            {
                return false;
            }

            current = current.Parent;
        }

        return true;
    }

    private static bool HasDirectDisposeBeforeExit(
        BlockSyntax block,
        StatementSyntax exitStatement,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (!block.Span.Contains(exitStatement.Span))
        {
            return false;
        }

        foreach (var statement in block.Statements)
        {
            if (statement.SpanStart <= creationSyntax.SpanStart)
            {
                continue;
            }

            if (statement.SpanStart >= exitStatement.SpanStart)
            {
                break;
            }

            if (StatementReliablyDisposesTarget(statement, creationSyntax, variableSymbol, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementReliablyDisposesTarget(
        StatementSyntax statement,
        SyntaxNode creationSyntax,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var invocationSyntax in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (!SharesExecutableBoundary(invocationSyntax, creationSyntax) ||
                !TryGetDisposedTargetSymbol(invocationSyntax, semanticModel, out var targetSymbol) ||
                !SymbolEqualityComparer.Default.Equals(targetSymbol, variableSymbol) ||
                !IsReliableDisposeProofWithinStatement(invocationSyntax, statement, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsReliableDisposeProofWithinStatement(
        InvocationExpressionSyntax disposeSyntax,
        StatementSyntax enclosingStatement,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var current = disposeSyntax.Parent;
        while (current is not null)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (!IsNonNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel))
                {
                    return false;
                }
            }
            else if (current is ElseClauseSyntax or
                     SwitchStatementSyntax or
                     SwitchSectionSyntax or
                     ConditionalExpressionSyntax or
                     ForStatementSyntax or
                     ForEachStatementSyntax or
                     ForEachVariableStatementSyntax or
                     WhileStatementSyntax or
                     DoStatementSyntax or
                     CatchClauseSyntax)
            {
                return false;
            }

            if (current == enclosingStatement)
            {
                return true;
            }

            current = current.Parent;
        }

        return true;
    }

    private static bool TryGetDisposeInvocation(
        ExpressionSyntax expression,
        out InvocationExpressionSyntax invocationSyntax)
    {
        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            expression = awaitExpression.Expression;
        }

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull is InvocationExpressionSyntax conditionalInvocation)
        {
            invocationSyntax = conditionalInvocation;
            return true;
        }

        if (expression is InvocationExpressionSyntax candidate)
        {
            invocationSyntax = candidate;
            return true;
        }

        invocationSyntax = null!;
        return false;
    }

    private static bool CreationMayRunRepeatedlyBeforeDispose(SyntaxNode creationSyntax, SyntaxNode disposeSyntax)
    {
        var boundary = GetExecutableBoundary(creationSyntax);
        var current = creationSyntax.Parent;
        while (current is not null && current != boundary)
        {
            if (current is ForStatementSyntax forStatement)
            {
                if (!IsSingleRunForInitializer(creationSyntax, forStatement) &&
                    !current.Span.Contains(disposeSyntax.Span) &&
                    !LoopBreaksAfterCreationBeforeNextIteration(creationSyntax, forStatement))
                {
                    return true;
                }
            }
            else if (current is ForEachStatementSyntax or
                     ForEachVariableStatementSyntax or
                     WhileStatementSyntax or
                     DoStatementSyntax)
            {
                if (!current.Span.Contains(disposeSyntax.Span) &&
                    current is StatementSyntax loopStatement &&
                    !LoopBreaksAfterCreationBeforeNextIteration(creationSyntax, loopStatement))
                {
                    return true;
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsSingleRunForInitializer(SyntaxNode creationSyntax, ForStatementSyntax forStatement)
    {
        return forStatement.Declaration?.Span.Contains(creationSyntax.Span) == true ||
               forStatement.Initializers.Any(initializer => initializer.Span.Contains(creationSyntax.Span));
    }

    private static bool LoopBreaksAfterCreationBeforeNextIteration(
        SyntaxNode creationSyntax,
        StatementSyntax loopStatement)
    {
        var creationStatement = creationSyntax.FirstAncestorOrSelf<StatementSyntax>();
        var loopBody = loopStatement switch
        {
            ForStatementSyntax forStatement => forStatement.Statement,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            ForEachVariableStatementSyntax forEachVariableStatement => forEachVariableStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null,
        };

        if (creationStatement is null ||
            loopBody is not BlockSyntax block ||
            !block.Span.Contains(creationStatement.Span))
        {
            return false;
        }

        foreach (var statement in block.Statements)
        {
            if (statement.SpanStart <= creationSyntax.SpanStart)
            {
                continue;
            }

            if (StatementCanContinueLoop(statement, loopStatement, creationSyntax))
            {
                return false;
            }

            if (statement is BreakStatementSyntax &&
                SharesExecutableBoundary(statement, creationSyntax))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementCanContinueLoop(
        StatementSyntax statement,
        StatementSyntax loopStatement,
        SyntaxNode creationSyntax)
    {
        return statement.DescendantNodesAndSelf()
            .OfType<ContinueStatementSyntax>()
            .Any(continueStatement =>
                SharesExecutableBoundary(continueStatement, creationSyntax) &&
                continueStatement.Ancestors()
                    .FirstOrDefault(ancestor =>
                        ancestor is ForStatementSyntax or
                        ForEachStatementSyntax or
                        ForEachVariableStatementSyntax or
                        WhileStatementSyntax or
                        DoStatementSyntax) == loopStatement);
    }

    private static bool AreInMutuallyExclusiveBranches(
        SyntaxNode firstSyntax,
        SyntaxNode secondSyntax,
        SemanticModel semanticModel)
    {
        foreach (var ifStatement in firstSyntax.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Else is null)
            {
                continue;
            }

            var firstInThen = ifStatement.Statement.Span.Contains(firstSyntax.Span);
            var firstInElse = ifStatement.Else.Span.Contains(firstSyntax.Span);
            var secondInThen = ifStatement.Statement.Span.Contains(secondSyntax.Span);
            var secondInElse = ifStatement.Else.Span.Contains(secondSyntax.Span);

            if ((firstInThen && secondInElse) || (firstInElse && secondInThen))
            {
                return true;
            }
        }

        if (AreInMutuallyExclusiveSwitchSections(firstSyntax, secondSyntax, semanticModel))
        {
            return true;
        }

        return false;
    }

    private static bool AreInMutuallyExclusiveSwitchSections(
        SyntaxNode firstSyntax,
        SyntaxNode secondSyntax,
        SemanticModel semanticModel)
    {
        var firstSection = firstSyntax.Ancestors().OfType<SwitchSectionSyntax>().FirstOrDefault();
        var secondSection = secondSyntax.Ancestors().OfType<SwitchSectionSyntax>().FirstOrDefault();
        if (firstSection is null ||
            secondSection is null ||
            firstSection == secondSection ||
            firstSection.Parent is not SwitchStatementSyntax switchStatement ||
            secondSection.Parent != switchStatement)
        {
            return false;
        }

        return !firstSection.DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Any(gotoStatement =>
                gotoStatement.SpanStart > firstSyntax.SpanStart &&
                gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault() == switchStatement &&
                SwitchSectionMatchesGoto(secondSection, gotoStatement, semanticModel) &&
                SharesExecutableBoundary(gotoStatement, firstSyntax));
    }

    private static bool IsNonNullGuardForTarget(
        ExpressionSyntax condition,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        condition = UnwrapParentheses(condition);

        if (condition is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return IsNullLiteral(binary.Left) && ExpressionTargetsSymbol(binary.Right, variableSymbol, semanticModel) ||
                   IsNullLiteral(binary.Right) && ExpressionTargetsSymbol(binary.Left, variableSymbol, semanticModel);
        }

        if (condition is IsPatternExpressionSyntax isPattern &&
            ExpressionTargetsSymbol(isPattern.Expression, variableSymbol, semanticModel) &&
            isPattern.Pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.Pattern is ConstantPatternSyntax constantPattern &&
            IsNullLiteral(constantPattern.Expression))
        {
            return true;
        }

        return false;
    }

    private static bool IsNullGuardForTarget(
        ExpressionSyntax condition,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        condition = UnwrapParentheses(condition);

        if (condition is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.EqualsExpression))
        {
            return IsNullLiteral(binary.Left) && ExpressionTargetsSymbol(binary.Right, variableSymbol, semanticModel) ||
                   IsNullLiteral(binary.Right) && ExpressionTargetsSymbol(binary.Left, variableSymbol, semanticModel);
        }

        if (condition is IsPatternExpressionSyntax isPattern &&
            ExpressionTargetsSymbol(isPattern.Expression, variableSymbol, semanticModel) &&
            isPattern.Pattern is ConstantPatternSyntax constantPattern &&
            IsNullLiteral(constantPattern.Expression))
        {
            return true;
        }

        return false;
    }

    private static bool ExpressionTargetsSymbol(
        ExpressionSyntax expression,
        ISymbol variableSymbol,
        SemanticModel semanticModel)
    {
        expression = UnwrapParentheses(expression);
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return SymbolEqualityComparer.Default.Equals(symbol, variableSymbol);
    }

    private static bool IsNullLiteral(ExpressionSyntax expression)
    {
        return UnwrapParentheses(expression).IsKind(SyntaxKind.NullLiteralExpression);
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasDisposeCallInOwnerType(
        TypeDeclarationSyntax containingType,
        ISymbol targetSymbol,
        SemanticModel semanticModel)
    {
        foreach (var member in containingType.Members)
        {
            if (member is not BaseMethodDeclarationSyntax method)
            {
                continue;
            }

            if (!IsDisposeMethod(method, semanticModel))
            {
                continue;
            }

            if (HasDisposeCallForTarget(method, targetSymbol, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDisposeCallForTarget(
        SyntaxNode node,
        ISymbol targetSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is not InvocationExpressionSyntax invocationSyntax ||
                !TryGetDisposedTargetSymbol(invocationSyntax, semanticModel, out var disposedSymbol))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(disposedSymbol, targetSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisposeMethod(BaseMethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (method is DestructorDeclarationSyntax)
        {
            return false;
        }

        if (method.ParameterList.Parameters.Count != 0)
        {
            return false;
        }

        if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        return methodSymbol.Name is "Dispose" or "DisposeAsync";
    }

    private static (ISymbol targetSymbol, TypeDeclarationSyntax? containingType)? GetAssignmentTarget(
        SyntaxNode creationSyntax,
        SemanticModel semanticModel)
    {
        if (creationSyntax.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(declarator);
            return declaredSymbol is null
                ? null
                : (declaredSymbol, declarator.FirstAncestorOrSelf<TypeDeclarationSyntax>());
        }

        if (creationSyntax.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Right == creationSyntax)
        {
            var targetSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            return targetSymbol is null
                ? null
                : (targetSymbol, assignment.FirstAncestorOrSelf<TypeDeclarationSyntax>());
        }

        return null;
    }

    private static bool TryGetDisposedTargetSymbol(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        out ISymbol? targetSymbol)
    {
        targetSymbol = null;

        if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text is not ("Dispose" or "DisposeAsync"))
            {
                return false;
            }

            targetSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            return targetSymbol is not null;
        }

        if (invocationSyntax.Expression is MemberBindingExpressionSyntax memberBinding &&
            memberBinding.Name.Identifier.Text is "Dispose" or "DisposeAsync" &&
            invocationSyntax.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            targetSymbol = semanticModel.GetSymbolInfo(conditionalAccess.Expression).Symbol;
            return targetSymbol is not null;
        }

        return false;
    }

    private static bool SharesExecutableBoundary(SyntaxNode candidateSyntax, SyntaxNode creationSyntax)
    {
        var candidateBoundary = GetExecutableBoundary(candidateSyntax);
        var creationBoundary = GetExecutableBoundary(creationSyntax);
        if (candidateBoundary is null || creationBoundary is null)
        {
            return true;
        }

        return candidateBoundary == creationBoundary;
    }

    private static SyntaxNode? GetExecutableBoundary(SyntaxNode syntax)
    {
        return syntax.AncestorsAndSelf().FirstOrDefault(node =>
            node is AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax or
            MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            AccessorDeclarationSyntax);
    }
}
