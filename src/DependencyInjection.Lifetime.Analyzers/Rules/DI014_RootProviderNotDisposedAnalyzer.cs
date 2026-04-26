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

    private static bool IsReturned(IInvocationOperation invocation)
    {
        var parent = invocation.Parent;

        if (parent is IReturnOperation)
        {
            return true;
        }

        if (invocation.Syntax.Parent is ReturnStatementSyntax)
        {
            return true;
        }

        if (invocation.Syntax.Parent is ArrowExpressionClauseSyntax)
        {
            return true;
        }

        return false;
    }

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation, SemanticModel semanticModel)
    {
        var assignmentTarget = GetAssignmentTarget(invocation.Syntax, semanticModel);
        if (assignmentTarget is null)
        {
            return false;
        }

        var (targetSymbol, containingType) = assignmentTarget.Value;
        if (targetSymbol is ILocalSymbol localSymbol)
        {
            var searchRoot = GetExecutableBoundary(invocation.Syntax) ?? GetContainingBlock(invocation.Syntax);
            if (searchRoot is null)
            {
                return false;
            }

            return HasDisposeCallInBlock(searchRoot, invocation.Syntax, localSymbol, semanticModel);
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
