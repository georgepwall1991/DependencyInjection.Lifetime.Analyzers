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

    private static bool IsReturned(IInvocationOperation invocation)
    {
        // Check if the invocation is directly returned
        var parent = invocation.Parent;

        // return CreateScope();
        if (parent is IReturnOperation)
        {
            return true;
        }

        // return _factory.CreateScope();
        if (invocation.Syntax.Parent is ReturnStatementSyntax)
        {
            return true;
        }

        // Arrow expression: => CreateScope()
        if (invocation.Syntax.Parent is ArrowExpressionClauseSyntax)
        {
            return true;
        }

        return false;
    }

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation, SemanticModel semanticModel)
    {
        var syntax = invocation.Syntax;
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

            if (CreationMayRunRepeatedlyBeforeDispose(creationSyntax, invocationSyntax))
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

            if (AreInMutuallyExclusiveBranches(creationSyntax, assignment))
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

    private static bool CreationMayRunRepeatedlyBeforeDispose(SyntaxNode creationSyntax, SyntaxNode disposeSyntax)
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

        return false;
    }

    private static bool AreInMutuallyExclusiveBranches(SyntaxNode firstSyntax, SyntaxNode secondSyntax)
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
