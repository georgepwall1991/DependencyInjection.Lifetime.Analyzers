using System.Collections.Generic;
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
/// Analyzer that detects IServiceScope instances that are not properly disposed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI001_ScopeDisposalAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ScopeMustBeDisposed);

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

            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, wellKnownTypes),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Check if this is CreateScope or CreateAsyncScope
        var methodName = invocation.TargetMethod.Name;
        if (methodName != "CreateScope" && methodName != "CreateAsyncScope")
        {
            return;
        }

        // Verify it's on IServiceScopeFactory
        var containingType = invocation.TargetMethod.ContainingType;
        if (!wellKnownTypes.IsServiceScopeFactory(containingType) &&
            !IsServiceProviderServiceExtensions(containingType))
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
            DiagnosticDescriptors.ScopeMustBeDisposed,
            invocation.Syntax.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsServiceProviderServiceExtensions(INamedTypeSymbol? type)
    {
        return type?.Name == "ServiceProviderServiceExtensions" &&
               type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
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
        // This is complex to track, so we simplify by checking if Dispose is called
        // on the same variable in the same method
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
            // using var scope = CreateScope();
            // await using var scope = CreateAsyncScope();
            if (parent is VariableDeclarationSyntax varDecl &&
                varDecl.Parent is LocalDeclarationStatementSyntax localDecl &&
                (localDecl.UsingKeyword != default || localDecl.AwaitKeyword != default))
            {
                return true;
            }

            // using (var scope = CreateScope()) { } OR using (CreateScope()) { }
            if (parent is UsingStatementSyntax usingStatement &&
                IsUsingStatementResource(syntax, usingStatement))
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
    /// Returns the expression whose parent decides how the created scope is consumed. A
    /// conditional-access creation (`provider?.CreateScope()`) wraps the invocation in a
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
        // Check if the invocation is directly returned
        var parent = invocation.Parent;

        // return CreateScope();
        if (parent is IReturnOperation)
        {
            return true;
        }

        // return _factory.CreateScope(); / return _factory?.CreateScope();
        var creationExpression = GetCreationExpression(invocation.Syntax);
        if (creationExpression.Parent is ReturnStatementSyntax)
        {
            return true;
        }

        // Arrow expression: => CreateScope() / => _factory?.CreateScope()
        if (creationExpression.Parent is ArrowExpressionClauseSyntax)
        {
            return true;
        }

        return false;
    }

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation, SemanticModel semanticModel)
    {
        var syntax = GetCreationExpression(invocation.Syntax);
        ILocalSymbol? variableSymbol = null;

        // Pattern 1: var scope = CreateScope();
        if (syntax.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator &&
            semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol localSymbol1)
        {
            variableSymbol = localSymbol1;
        }
        // Pattern 2: scope = CreateScope(); (reassignment to existing local)
        else if (syntax.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol2)
        {
            variableSymbol = localSymbol2;
        }

        if (variableSymbol is null)
        {
            return false;
        }

        var searchRoot = GetExecutableBoundary(syntax) ?? GetContainingBlock(syntax);
        if (searchRoot is null)
        {
            return false;
        }

        return HasDisposeCall(searchRoot, syntax, variableSymbol, semanticModel);
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

    private static bool HasDisposeCall(
        SyntaxNode block,
        SyntaxNode creationSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in block.DescendantNodes())
        {
            if (descendant is not InvocationExpressionSyntax invocationSyntax ||
                invocationSyntax.SpanStart <= creationSyntax.SpanStart)
            {
                continue;
            }

            if (!IsDisposeInvocation(invocationSyntax))
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

            if (CreationMayRunRepeatedlyBeforeDispose(creationSyntax, invocationSyntax, semanticModel))
            {
                continue;
            }

            if (!IsReliableDisposeProof(invocationSyntax, creationSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            var targetSymbol = GetDisposeTargetSymbol(invocationSyntax, semanticModel);
            if (targetSymbol is null)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(targetSymbol, variableSymbol))
            {
                continue;
            }

            // Ensure no intervening reassignment between creation and dispose
            if (HasInterveningReassignment(creationSyntax, invocationSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasInterveningReassignment(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in creationSyntax.SyntaxTree.GetRoot().DescendantNodes(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(creationSyntax.Span.End, disposeSyntax.SpanStart)))
        {
            if (descendant is not AssignmentExpressionSyntax assignment ||
                assignment.Left is not IdentifierNameSyntax identifierName)
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(identifierName).Symbol is not ILocalSymbol assignedLocal ||
                !SymbolEqualityComparer.Default.Equals(assignedLocal, variableSymbol))
            {
                continue;
            }

            // Only count reassignments in the same executable boundary.
            // A reassignment inside a lambda may never execute.
            if (!SharesExecutableBoundary(assignment, creationSyntax))
            {
                continue;
            }

            if (AreInMutuallyExclusiveBranches(creationSyntax, assignment, semanticModel))
            {
                continue;
            }

            // Any reassignment to the same local between creation and dispose
            // means the original scope is lost — the dispose call proves the
            // reassigned value was disposed, not the original.
            return true;
        }

        return false;
    }

    private static bool CreationCanReachDispose(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (DisposeRunsFromFinallyForCreation(creationSyntax, disposeSyntax, variableSymbol, semanticModel))
        {
            return true;
        }

        return !HasBlockingExitBetween(creationSyntax, disposeSyntax, variableSymbol, semanticModel);
    }

    private static bool HasBlockingExitBetween(
        SyntaxNode startSyntax,
        SyntaxNode endSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in startSyntax.SyntaxTree.GetRoot().DescendantNodes(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startSyntax.Span.End, endSyntax.SpanStart)))
        {
            if (descendant is not ReturnStatementSyntax and not ThrowStatementSyntax and not ThrowExpressionSyntax and not GotoStatementSyntax)
            {
                continue;
            }

            if (!SharesExecutableBoundary(descendant, startSyntax))
            {
                continue;
            }

            if (descendant is ThrowStatementSyntax throwStatement &&
                IsHandledThrowBeforeDispose(throwStatement, startSyntax, endSyntax, semanticModel))
            {
                continue;
            }

            if (descendant is ThrowExpressionSyntax throwExpression &&
                IsHandledThrowBeforeDispose(throwExpression, startSyntax, endSyntax, semanticModel))
            {
                continue;
            }

            if (descendant is GotoStatementSyntax gotoStatement &&
                !GotoMayBypassDispose(gotoStatement, startSyntax, endSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            if (AreInMutuallyExclusiveBranches(startSyntax, descendant, semanticModel))
            {
                continue;
            }

            if (IsNullGuardExit(descendant, variableSymbol, semanticModel))
            {
                continue;
            }

            if (HasDisposeBeforeExit(startSyntax, descendant, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasDisposeBeforeExit(
        SyntaxNode startSyntax,
        SyntaxNode exitSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        foreach (var descendant in startSyntax.SyntaxTree.GetRoot().DescendantNodes(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startSyntax.Span.End, exitSyntax.SpanStart)))
        {
            if (descendant is not InvocationExpressionSyntax invocationSyntax ||
                !IsDisposeInvocation(invocationSyntax) ||
                !SharesExecutableBoundary(invocationSyntax, startSyntax) ||
                AreInMutuallyExclusiveBranches(invocationSyntax, exitSyntax, semanticModel) ||
                !DisposeRunsOnSamePathBeforeExit(invocationSyntax, exitSyntax, semanticModel))
            {
                continue;
            }

            var targetSymbol = GetDisposeTargetSymbol(invocationSyntax, semanticModel);
            if (!SymbolEqualityComparer.Default.Equals(targetSymbol, variableSymbol))
            {
                continue;
            }

            if (HasInterveningReassignment(startSyntax, invocationSyntax, variableSymbol, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool DisposeRunsOnSamePathBeforeExit(
        InvocationExpressionSyntax disposeSyntax,
        SyntaxNode exitSyntax,
        SemanticModel semanticModel)
    {
        var boundary = GetExecutableBoundary(disposeSyntax);
        var current = disposeSyntax.Parent;
        while (current is not null && current != boundary)
        {
            if (current is IfStatementSyntax ifStatement &&
                !AreInSameIfBranch(disposeSyntax, exitSyntax, ifStatement))
            {
                return false;
            }

            if (current is ElseClauseSyntax &&
                current.Parent is IfStatementSyntax parentIf &&
                !AreInSameIfBranch(disposeSyntax, exitSyntax, parentIf))
            {
                return false;
            }

            if (current is TryStatementSyntax tryStatement)
            {
                return tryStatement.Block.Span.Contains(disposeSyntax.Span) &&
                       tryStatement.Block.Span.Contains(exitSyntax.Span) ||
                       tryStatement.Catches.Any(catchClause =>
                           catchClause.Block.Span.Contains(disposeSyntax.Span) &&
                           catchClause.Block.Span.Contains(exitSyntax.Span)) ||
                       tryStatement.Finally?.Block.Span.Contains(disposeSyntax.Span) == true &&
                       tryStatement.Finally.Block.Span.Contains(exitSyntax.Span);
            }

            if (current is SwitchStatementSyntax or
                SwitchSectionSyntax or
                ConditionalExpressionSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax or
                CatchClauseSyntax)
            {
                if (current.Span.Contains(exitSyntax.Span))
                {
                    return true;
                }

                if (current is SwitchSectionSyntax sourceSection &&
                    exitSyntax.AncestorsAndSelf().OfType<SwitchSectionSyntax>().FirstOrDefault() is { } targetSection &&
                    sourceSection.Parent == targetSection.Parent &&
                    sourceSection.Parent is SwitchStatementSyntax switchStatement &&
                    SwitchSectionCanReachSection(sourceSection, targetSection, switchStatement, disposeSyntax.Span.End, semanticModel))
                {
                    return true;
                }

                return false;
            }

            current = current.Parent;
        }

        return true;
    }

    private static bool IsHandledThrowBeforeDispose(
        ThrowStatementSyntax throwStatement,
        SyntaxNode startSyntax,
        SyntaxNode disposeSyntax,
        SemanticModel semanticModel)
    {
        return IsHandledThrowBeforeDispose(
            throwStatement,
            throwStatement.Expression,
            startSyntax,
            disposeSyntax,
            semanticModel);
    }

    private static bool IsHandledThrowBeforeDispose(
        ThrowExpressionSyntax throwExpression,
        SyntaxNode startSyntax,
        SyntaxNode disposeSyntax,
        SemanticModel semanticModel)
    {
        return IsHandledThrowBeforeDispose(
            throwExpression,
            throwExpression.Expression,
            startSyntax,
            disposeSyntax,
            semanticModel);
    }

    private static bool IsHandledThrowBeforeDispose(
        SyntaxNode throwSyntax,
        ExpressionSyntax? thrownExpression,
        SyntaxNode startSyntax,
        SyntaxNode disposeSyntax,
        SemanticModel semanticModel)
    {
        var tryStatement = throwSyntax.Ancestors()
            .OfType<TryStatementSyntax>()
            .FirstOrDefault(candidate => candidate.Block.Span.Contains(throwSyntax.Span));

        return tryStatement is not null &&
               (tryStatement.SpanStart > startSyntax.SpanStart ||
                tryStatement.Block.Span.Contains(startSyntax.Span)) &&
               !tryStatement.Span.Contains(disposeSyntax.Span) &&
               SharesExecutableBoundary(tryStatement, startSyntax) &&
               tryStatement.Catches.Any(catchClause => CatchCanHandleThrow(catchClause, thrownExpression, semanticModel));
    }

    private static bool CatchCanHandleThrow(
        CatchClauseSyntax catchClause,
        ExpressionSyntax? thrownExpression,
        SemanticModel semanticModel)
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

        var thrownType = semanticModel.GetTypeInfo(thrownExpression).ConvertedType ??
                         semanticModel.GetTypeInfo(thrownExpression).Type;
        var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;

        if (thrownType is not null &&
            catchType is not null &&
            TypeDerivesFromOrEquals(thrownType, catchType))
        {
            return true;
        }

        return thrownExpression is ObjectCreationExpressionSyntax objectCreation &&
               catchClause.Declaration.Type.ToString() == objectCreation.Type.ToString();
    }

    private static bool TypeDerivesFromOrEquals(ITypeSymbol type, ITypeSymbol candidateBaseType)
    {
        var current = type;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidateBaseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool DisposeRunsFromFinallyForCreation(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var finallyClause = disposeSyntax.Ancestors().OfType<FinallyClauseSyntax>().FirstOrDefault();
        if (finallyClause?.Parent is not TryStatementSyntax tryStatement)
        {
            return false;
        }

        if (tryStatement.Block.Span.Contains(creationSyntax.Span) ||
            tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(creationSyntax.Span)))
        {
            return true;
        }

        return creationSyntax.SpanStart < tryStatement.SpanStart &&
               SharesExecutableBoundary(tryStatement, creationSyntax) &&
               !HasBlockingExitBetween(creationSyntax, tryStatement, variableSymbol, semanticModel);
    }

    private static bool IsNullGuardExit(
        SyntaxNode exitSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (exitSyntax.Parent is IfStatementSyntax directIfStatement &&
            directIfStatement.Statement == exitSyntax)
        {
            return IsNullGuardForTarget(directIfStatement.Condition, variableSymbol, semanticModel);
        }

        if (exitSyntax.Parent is not BlockSyntax block ||
            block.Parent is not IfStatementSyntax ifStatement ||
            ifStatement.Statement != block)
        {
            return false;
        }

        return IsNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel);
    }

    private static bool IsReliableDisposeProof(
        InvocationExpressionSyntax disposeSyntax,
        SyntaxNode creationSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var boundary = GetExecutableBoundary(creationSyntax);
        var current = disposeSyntax.Parent;
        while (current is not null && current != boundary)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (AreInSameIfBranch(creationSyntax, disposeSyntax, ifStatement))
                {
                    current = current.Parent;
                    continue;
                }

                if (!IsNonNullGuardForTarget(ifStatement.Condition, variableSymbol, semanticModel))
                {
                    return false;
                }
            }
            else if (current is ElseClauseSyntax elseClause)
            {
                if (elseClause.Parent is IfStatementSyntax parentIf &&
                    AreInSameIfBranch(creationSyntax, disposeSyntax, parentIf))
                {
                    current = current.Parent;
                    continue;
                }

                return false;
            }
            else if (current is SwitchStatementSyntax or
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

    private static bool AreInSameIfBranch(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        IfStatementSyntax ifStatement)
    {
        var creationBranch = GetIfBranchContainingSyntax(ifStatement, creationSyntax);
        return creationBranch is not null &&
               creationBranch == GetIfBranchContainingSyntax(ifStatement, disposeSyntax);
    }

    private static SyntaxNode? GetIfBranchContainingSyntax(IfStatementSyntax ifStatement, SyntaxNode syntax)
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

    private static bool CreationMayRunRepeatedlyBeforeDispose(
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        SemanticModel semanticModel)
    {
        var boundary = GetExecutableBoundary(creationSyntax);
        var current = creationSyntax.Parent;
        while (current is not null && current != boundary)
        {
            if (current is ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax)
            {
                return !current.Span.Contains(disposeSyntax.Span);
            }

            current = current.Parent;
        }

        foreach (var gotoStatement in creationSyntax.SyntaxTree.GetRoot().DescendantNodes(
            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(creationSyntax.Span.End, disposeSyntax.SpanStart))
            .OfType<GotoStatementSyntax>())
        {
            if (!SharesExecutableBoundary(gotoStatement, creationSyntax))
            {
                continue;
            }

            if (gotoStatement.Expression is IdentifierNameSyntax labelName)
            {
                var targetLabel = gotoStatement.SyntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<LabeledStatementSyntax>()
                    .FirstOrDefault(label =>
                        label.Identifier.ValueText == labelName.Identifier.ValueText &&
                        SharesExecutableBoundary(label, gotoStatement));

                if (targetLabel is not null && targetLabel.SpanStart <= creationSyntax.SpanStart)
                {
                    return true;
                }
            }

            var creationSection = creationSyntax.AncestorsAndSelf().OfType<SwitchSectionSyntax>().FirstOrDefault();
            if (creationSection?.Parent is SwitchStatementSyntax switchStatement &&
                gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault() == switchStatement &&
                GotoTargetsSwitchSection(gotoStatement, creationSection, semanticModel))
            {
                return true;
            }
        }

        return false;
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

        var firstSwitchSection = firstSyntax.AncestorsAndSelf().OfType<SwitchSectionSyntax>().FirstOrDefault();
        var secondSwitchSection = secondSyntax.AncestorsAndSelf().OfType<SwitchSectionSyntax>().FirstOrDefault();
        var switchStatement = firstSwitchSection?.Parent;
        if (firstSwitchSection is not null &&
            secondSwitchSection is not null &&
            firstSwitchSection != secondSwitchSection &&
            switchStatement == secondSwitchSection.Parent &&
            switchStatement is SwitchStatementSyntax owningSwitchStatement &&
            !SwitchSectionCanReachSection(firstSwitchSection, secondSwitchSection, owningSwitchStatement, firstSyntax.Span.End, semanticModel))
        {
            return true;
        }

        return false;
    }

    private static bool GotoMayBypassDispose(
        GotoStatementSyntax gotoStatement,
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        if (gotoStatement.Expression is IdentifierNameSyntax labelName)
        {
            var targetLabel = gotoStatement.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<LabeledStatementSyntax>()
                .FirstOrDefault(label =>
                    label.Identifier.ValueText == labelName.Identifier.ValueText &&
                    SharesExecutableBoundary(label, gotoStatement));

            return targetLabel is null ||
                   targetLabel.SpanStart <= creationSyntax.SpanStart ||
                   targetLabel.SpanStart >= disposeSyntax.SpanStart;
        }

        if (gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) ||
            gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            return SwitchGotoMayBypassDispose(
                gotoStatement,
                creationSyntax,
                disposeSyntax,
                variableSymbol,
                semanticModel);
        }

        return true;
    }

    private static bool SwitchGotoMayBypassDispose(
        GotoStatementSyntax gotoStatement,
        SyntaxNode creationSyntax,
        SyntaxNode disposeSyntax,
        ILocalSymbol variableSymbol,
        SemanticModel semanticModel)
    {
        var switchStatement = gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault();
        if (switchStatement is null)
        {
            return true;
        }

        var pending = new Queue<SwitchSectionSyntax>();
        foreach (var targetSection in switchStatement.Sections)
        {
            if (GotoTargetsSwitchSection(gotoStatement, targetSection, semanticModel))
            {
                pending.Enqueue(targetSection);
            }
        }

        if (pending.Count == 0)
        {
            return true;
        }

        var visited = new HashSet<SwitchSectionSyntax>();
        while (pending.Count > 0)
        {
            var currentSection = pending.Dequeue();
            if (!visited.Add(currentSection))
            {
                continue;
            }

            foreach (var descendant in currentSection.DescendantNodes())
            {
                if (descendant.SpanStart >= disposeSyntax.SpanStart ||
                    !SharesExecutableBoundary(descendant, creationSyntax))
                {
                    continue;
                }

                if (descendant is ThrowStatementSyntax throwStatement &&
                    IsHandledThrowBeforeDispose(throwStatement, creationSyntax, disposeSyntax, semanticModel))
                {
                    continue;
                }

                if (descendant is ThrowExpressionSyntax throwExpression &&
                    IsHandledThrowBeforeDispose(throwExpression, creationSyntax, disposeSyntax, semanticModel))
                {
                    continue;
                }

                if (descendant is ReturnStatementSyntax or ThrowStatementSyntax or ThrowExpressionSyntax)
                {
                    if (IsNullGuardExit(descendant, variableSymbol, semanticModel) ||
                        HasDisposeBeforeExit(creationSyntax, descendant, variableSymbol, semanticModel))
                    {
                        continue;
                    }

                    return true;
                }

                if (descendant is not GotoStatementSyntax nestedGoto)
                {
                    continue;
                }

                if (nestedGoto.IsKind(SyntaxKind.GotoCaseStatement) ||
                    nestedGoto.IsKind(SyntaxKind.GotoDefaultStatement))
                {
                    foreach (var targetSection in switchStatement.Sections)
                    {
                        if (GotoTargetsSwitchSection(nestedGoto, targetSection, semanticModel))
                        {
                            pending.Enqueue(targetSection);
                        }
                    }

                    continue;
                }

                if (GotoMayBypassDispose(nestedGoto, creationSyntax, disposeSyntax, variableSymbol, semanticModel))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SwitchSectionCanReachSection(
        SwitchSectionSyntax sourceSection,
        SwitchSectionSyntax targetSection,
        SwitchStatementSyntax switchStatement,
        int sourceStart,
        SemanticModel semanticModel)
    {
        var visited = new HashSet<SwitchSectionSyntax>();
        var pending = new Queue<SwitchSectionSyntax>();
        pending.Enqueue(sourceSection);

        while (pending.Count > 0)
        {
            var currentSection = pending.Dequeue();
            if (!visited.Add(currentSection))
            {
                continue;
            }

            var minimumGotoStart = currentSection == sourceSection ? sourceStart : currentSection.SpanStart;
            foreach (var nextSection in GetGotoTargetSwitchSections(currentSection, switchStatement, minimumGotoStart, semanticModel))
            {
                if (nextSection == targetSection)
                {
                    return true;
                }

                pending.Enqueue(nextSection);
            }
        }

        return false;
    }

    private static IEnumerable<SwitchSectionSyntax> GetGotoTargetSwitchSections(
        SwitchSectionSyntax sourceSection,
        SwitchStatementSyntax switchStatement,
        int minimumGotoStart,
        SemanticModel semanticModel)
    {
        foreach (var gotoStatement in sourceSection.DescendantNodes().OfType<GotoStatementSyntax>())
        {
            if (gotoStatement.SpanStart < minimumGotoStart ||
                !SharesExecutableBoundary(gotoStatement, sourceSection) ||
                gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault() != switchStatement)
            {
                continue;
            }

            foreach (var targetSection in switchStatement.Sections)
            {
                if (GotoTargetsSwitchSection(gotoStatement, targetSection, semanticModel))
                {
                    yield return targetSection;
                }
            }
        }
    }

    private static bool GotoTargetsSwitchSection(
        GotoStatementSyntax gotoStatement,
        SwitchSectionSyntax targetSection,
        SemanticModel semanticModel)
    {
        if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            return targetSection.Labels.OfType<DefaultSwitchLabelSyntax>().Any();
        }

        if (gotoStatement.IsKind(SyntaxKind.GotoCaseStatement))
        {
            if (gotoStatement.Expression is null)
            {
                return false;
            }

            var targetCase = semanticModel.GetConstantValue(gotoStatement.Expression);
            return targetSection.Labels
                .OfType<CaseSwitchLabelSyntax>()
                .Any(label =>
                {
                    var labelValue = semanticModel.GetConstantValue(label.Value);
                    return targetCase.HasValue &&
                           labelValue.HasValue &&
                           Equals(targetCase.Value, labelValue.Value) ||
                           label.Value.ToString() == gotoStatement.Expression.ToString();
                });
        }

        if (gotoStatement.Expression is IdentifierNameSyntax labelName)
        {
            return targetSection.DescendantNodes()
                .OfType<LabeledStatementSyntax>()
                .Any(label => label.Identifier.ValueText == labelName.Identifier.ValueText);
        }

        return false;
    }

    private static bool IsNullGuardForTarget(
        ExpressionSyntax condition,
        ILocalSymbol variableSymbol,
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

    private static bool IsNonNullGuardForTarget(
        ExpressionSyntax condition,
        ILocalSymbol variableSymbol,
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

    private static bool ExpressionTargetsSymbol(
        ExpressionSyntax expression,
        ILocalSymbol variableSymbol,
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

    private static bool IsDisposeInvocation(InvocationExpressionSyntax invocationSyntax)
    {
        // scope.Dispose() or scope.DisposeAsync()
        if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text is ("Dispose" or "DisposeAsync"))
        {
            return true;
        }

        // scope?.Dispose() or scope?.DisposeAsync()
        // Parsed as: InvocationExpression { Expression: MemberBindingExpressionSyntax }
        //   parent: ConditionalAccessExpressionSyntax { Expression: scope }
        if (invocationSyntax.Expression is MemberBindingExpressionSyntax memberBinding &&
            memberBinding.Name.Identifier.Text is ("Dispose" or "DisposeAsync") &&
            invocationSyntax.Parent is ConditionalAccessExpressionSyntax)
        {
            return true;
        }

        return false;
    }

    private static ISymbol? GetDisposeTargetSymbol(InvocationExpressionSyntax invocationSyntax, SemanticModel semanticModel)
    {
        // scope.Dispose() — target is the expression before the dot
        if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
        }

        // scope?.Dispose() — target is the expression before the ?
        if (invocationSyntax.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return semanticModel.GetSymbolInfo(conditionalAccess.Expression).Symbol;
        }

        return null;
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
