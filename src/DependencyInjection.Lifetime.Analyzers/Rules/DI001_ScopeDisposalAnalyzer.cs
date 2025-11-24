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

        // Check if the result is properly handled
        if (IsProperlyDisposed(invocation))
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
        return type?.Name == "ServiceProviderServiceExtensions";
    }

    private static bool IsProperlyDisposed(IInvocationOperation invocation)
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
        if (IsExplicitlyDisposed(invocation))
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
            if (parent is VariableDeclarationSyntax varDecl &&
                varDecl.Parent is LocalDeclarationStatementSyntax localDecl &&
                localDecl.UsingKeyword != default)
            {
                return true;
            }

            // using (var scope = CreateScope()) { }
            if (parent is UsingStatementSyntax)
            {
                return true;
            }

            // await using var scope = CreateAsyncScope();
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

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation)
    {
        // Find the variable this scope is assigned to
        var syntax = invocation.Syntax;

        // Look for pattern: var scope = CreateScope();
        if (syntax.Parent is not EqualsValueClauseSyntax equalsValue)
        {
            return false;
        }

        if (equalsValue.Parent is not VariableDeclaratorSyntax declarator)
        {
            return false;
        }

        var variableName = declarator.Identifier.Text;

        // Find the containing method/block
        var containingBlock = GetContainingBlock(syntax);
        if (containingBlock is null)
        {
            return false;
        }

        // Look for scope.Dispose() call in the same block or try-finally
        return HasDisposeCall(containingBlock, variableName);
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

    private static bool HasDisposeCall(SyntaxNode block, string variableName)
    {
        // Look for variableName.Dispose() in try-finally or direct calls
        foreach (var descendant in block.DescendantNodes())
        {
            if (descendant is InvocationExpressionSyntax invocationSyntax &&
                invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "Dispose" &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == variableName)
            {
                return true;
            }

            // Also check for DisposeAsync
            if (descendant is InvocationExpressionSyntax invocationSyntax2 &&
                invocationSyntax2.Expression is MemberAccessExpressionSyntax memberAccess2 &&
                memberAccess2.Name.Identifier.Text == "DisposeAsync" &&
                memberAccess2.Expression is IdentifierNameSyntax identifier2 &&
                identifier2.Identifier.Text == variableName)
            {
                return true;
            }
        }

        return false;
    }
}
