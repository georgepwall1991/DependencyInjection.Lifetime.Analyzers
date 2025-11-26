using System.Collections.Immutable;
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
        if (containingType?.Name != "ServiceCollectionContainerBuilderExtensions")
        {
            return;
        }

        // Check if the result is properly handled
        if (IsProperlyDisposed(invocation))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RootProviderNotDisposed,
            invocation.Syntax.GetLocation());

        context.ReportDiagnostic(diagnostic);
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
            // using var provider = ...;
            if (parent is VariableDeclarationSyntax varDecl &&
                varDecl.Parent is LocalDeclarationStatementSyntax localDecl &&
                localDecl.UsingKeyword != default)
            {
                return true;
            }

            // using (var provider = ...) { }
            if (parent is UsingStatementSyntax)
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

    private static bool IsExplicitlyDisposed(IInvocationOperation invocation)
    {
        var syntax = invocation.Syntax;

        if (syntax.Parent is not EqualsValueClauseSyntax equalsValue)
        {
            return false;
        }

        if (equalsValue.Parent is not VariableDeclaratorSyntax declarator)
        {
            return false;
        }

        var variableName = declarator.Identifier.Text;

        var containingBlock = GetContainingBlock(syntax);
        if (containingBlock is null)
        {
            return false;
        }

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
        foreach (var descendant in block.DescendantNodes())
        {
            if (descendant is InvocationExpressionSyntax invocationSyntax &&
                invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess &&
                (memberAccess.Name.Identifier.Text == "Dispose" || memberAccess.Name.Identifier.Text == "DisposeAsync") &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == variableName)
            {
                return true;
            }
        }

        return false;
    }
}
