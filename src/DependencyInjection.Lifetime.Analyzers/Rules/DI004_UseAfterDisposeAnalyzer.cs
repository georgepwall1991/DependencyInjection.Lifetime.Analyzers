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

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeMethod(syntaxContext, wellKnownTypes),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Find using statements (not declarations) that might have variables used outside
        foreach (var usingStmt in method.DescendantNodes().OfType<UsingStatementSyntax>())
        {
            AnalyzeUsingStatement(context, usingStmt, wellKnownTypes);
        }

        // Find using declarations (using var scope = ...) in nested blocks
        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (localDecl.UsingKeyword != default)
            {
                AnalyzeUsingDeclaration(context, localDecl, wellKnownTypes);
            }
        }
    }

    private static void AnalyzeUsingStatement(
        SyntaxNodeAnalysisContext context,
        UsingStatementSyntax usingStmt,
        WellKnownTypes wellKnownTypes)
    {
        // Find scope variable
        string? scopeVariableName = null;
        if (usingStmt.Declaration is not null)
        {
            foreach (var variable in usingStmt.Declaration.Variables)
            {
                if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                    IsCreateScopeMethod(invocation))
                {
                    scopeVariableName = variable.Identifier.Text;
                    break;
                }
            }
        }

        if (scopeVariableName is null)
        {
            return;
        }

        // Find services resolved from the scope within the using block
        var serviceVariables = new HashSet<string>();
        if (usingStmt.Statement is not null)
        {
            foreach (var invocation in usingStmt.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (IsServiceResolutionFromScope(invocation, scopeVariableName))
                {
                    // Find what variable the result is assigned to
                    var assignedVariable = GetAssignedVariable(invocation);
                    if (assignedVariable is not null)
                    {
                        serviceVariables.Add(assignedVariable);
                    }
                }
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        // Find the containing method to check for usage after the using block
        var containingMethod = usingStmt.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod is null)
        {
            return;
        }

        // Get the position after the using statement
        var usingEndPosition = usingStmt.Span.End;

        // Check for usage of service variables after the using block
        foreach (var node in containingMethod.DescendantNodes())
        {
            if (node.SpanStart < usingEndPosition)
            {
                continue;
            }

            // Look for invocations on service variables
            if (node is InvocationExpressionSyntax invocationAfter &&
                invocationAfter.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                serviceVariables.Contains(identifier.Identifier.Text))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.UseAfterScopeDisposed,
                    invocationAfter.GetLocation(),
                    identifier.Identifier.Text);

                context.ReportDiagnostic(diagnostic);
            }

            // Look for property accesses on service variables
            if (node is MemberAccessExpressionSyntax memberAccessAfter &&
                memberAccessAfter.Expression is IdentifierNameSyntax identifierAccess &&
                serviceVariables.Contains(identifierAccess.Identifier.Text) &&
                memberAccessAfter.Parent is not InvocationExpressionSyntax)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.UseAfterScopeDisposed,
                    memberAccessAfter.GetLocation(),
                    identifierAccess.Identifier.Text);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzeUsingDeclaration(
        SyntaxNodeAnalysisContext context,
        LocalDeclarationStatementSyntax localDecl,
        WellKnownTypes wellKnownTypes)
    {
        // Find scope variable from the using declaration
        string? scopeVariableName = null;
        foreach (var variable in localDecl.Declaration.Variables)
        {
            if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                IsCreateScopeMethod(invocation))
            {
                scopeVariableName = variable.Identifier.Text;
                break;
            }
        }

        if (scopeVariableName is null)
        {
            return;
        }

        // Get the containing block for the using declaration
        var containingBlock = localDecl.Parent as BlockSyntax;
        if (containingBlock is null)
        {
            return;
        }

        // For using var, the scope extends to the end of the containing block
        // We need to check if services are used AFTER that block ends

        // Find services resolved from the scope within its valid lifetime
        var serviceVariables = new HashSet<string>();
        var blockEndPosition = containingBlock.Span.End;

        foreach (var node in containingBlock.DescendantNodes())
        {
            if (node.SpanStart < localDecl.SpanStart)
            {
                continue;
            }

            if (node is InvocationExpressionSyntax invocation &&
                IsServiceResolutionFromScope(invocation, scopeVariableName))
            {
                var assignedVariable = GetAssignedVariable(invocation);
                if (assignedVariable is not null)
                {
                    serviceVariables.Add(assignedVariable);
                }
            }
        }

        if (serviceVariables.Count == 0)
        {
            return;
        }

        // Find the method containing this block to check for usage after the block ends
        var containingMethod = localDecl.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod is null)
        {
            return;
        }

        // Check for service usage after the containing block ends
        foreach (var node in containingMethod.DescendantNodes())
        {
            if (node.SpanStart < blockEndPosition)
            {
                continue;
            }

            // Look for invocations on service variables
            if (node is InvocationExpressionSyntax invocationAfter &&
                invocationAfter.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                serviceVariables.Contains(identifier.Identifier.Text))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.UseAfterScopeDisposed,
                    invocationAfter.GetLocation(),
                    identifier.Identifier.Text);

                context.ReportDiagnostic(diagnostic);
            }

            // Look for property accesses on service variables
            if (node is MemberAccessExpressionSyntax memberAccessAfter &&
                memberAccessAfter.Expression is IdentifierNameSyntax identifierAccess &&
                serviceVariables.Contains(identifierAccess.Identifier.Text) &&
                memberAccessAfter.Parent is not InvocationExpressionSyntax)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.UseAfterScopeDisposed,
                    memberAccessAfter.GetLocation(),
                    identifierAccess.Identifier.Text);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsCreateScopeMethod(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name.Identifier.Text;
            return name == "CreateScope" || name == "CreateAsyncScope";
        }

        return false;
    }

    private static bool IsServiceResolutionFromScope(
        InvocationExpressionSyntax invocation,
        string scopeVariableName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax outerMember)
        {
            return false;
        }

        var methodName = outerMember.Name.Identifier.Text;
        if (!methodName.StartsWith("Get") || !methodName.Contains("Service"))
        {
            return false;
        }

        // Check if called on scope.ServiceProvider
        if (outerMember.Expression is MemberAccessExpressionSyntax innerMember &&
            innerMember.Name.Identifier.Text == "ServiceProvider" &&
            innerMember.Expression is IdentifierNameSyntax scopeId &&
            scopeId.Identifier.Text == scopeVariableName)
        {
            return true;
        }

        return false;
    }

    private static string? GetAssignedVariable(InvocationExpressionSyntax invocation)
    {
        // Check for: service = scope.ServiceProvider.GetService<T>();
        if (invocation.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }

        // Check for: var service = scope.ServiceProvider.GetService<T>();
        if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator)
        {
            return declarator.Identifier.Text;
        }

        return null;
    }
}
