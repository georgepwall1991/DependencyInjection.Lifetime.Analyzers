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
/// Analyzer that detects when scoped services escape their scope lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI002_ScopeEscapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ScopedServiceEscapes);

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

        // Find all using declarations with CreateScope
        var scopeVariables = new HashSet<string>();
        var serviceVariables = new Dictionary<string, InvocationExpressionSyntax>();

        // First pass: find scope variables from using declarations
        foreach (var node in method.DescendantNodes())
        {
            // using var scope = _scopeFactory.CreateScope();
            if (node is LocalDeclarationStatementSyntax localDecl &&
                (localDecl.UsingKeyword != default || localDecl.AwaitKeyword != default))
            {
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                        IsCreateScopeMethod(invocation))
                    {
                        scopeVariables.Add(variable.Identifier.Text);
                    }
                }
            }

            // using (var scope = ...) { }
            if (node is UsingStatementSyntax usingStmt &&
                usingStmt.Declaration is not null)
            {
                foreach (var variable in usingStmt.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                        IsCreateScopeMethod(invocation))
                    {
                        scopeVariables.Add(variable.Identifier.Text);
                    }
                }
            }
        }

        if (scopeVariables.Count == 0)
        {
            return;
        }

        // Second pass: find service resolutions from scope.ServiceProvider
        foreach (var node in method.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                IsServiceResolutionFromScope(invocation, scopeVariables))
            {
                // Track variable assignments
                if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
                    equalsValue.Parent is VariableDeclaratorSyntax declarator)
                {
                    serviceVariables[declarator.Identifier.Text] = invocation;
                }

                // Check for direct return
                if (invocation.Parent is ReturnStatementSyntax)
                {
                    ReportDiagnostic(context, invocation, "return");
                }

                // Check for direct field assignment
                if (invocation.Parent is AssignmentExpressionSyntax assignment &&
                    assignment.Left is IdentifierNameSyntax identifier)
                {
                    var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
                    if (symbol is IFieldSymbol fieldSymbol)
                    {
                        ReportDiagnostic(context, invocation, fieldSymbol.Name);
                    }
                }
            }
        }

        // Third pass: check for escaping service variables
        foreach (var node in method.DescendantNodes())
        {
            // return service;
            if (node is ReturnStatementSyntax returnStmt &&
                returnStmt.Expression is IdentifierNameSyntax returnId &&
                serviceVariables.TryGetValue(returnId.Identifier.Text, out var sourceInvocation))
            {
                ReportDiagnostic(context, sourceInvocation, "return");
            }

            // _field = service;
            if (node is AssignmentExpressionSyntax fieldAssignment &&
                fieldAssignment.Right is IdentifierNameSyntax valueId &&
                serviceVariables.TryGetValue(valueId.Identifier.Text, out var srcInvocation))
            {
                var symbol = context.SemanticModel.GetSymbolInfo(fieldAssignment.Left).Symbol;
                if (symbol is IFieldSymbol field)
                {
                    ReportDiagnostic(context, srcInvocation, field.Name);
                }
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
        HashSet<string> scopeVariables)
    {
        // scope.ServiceProvider.GetService<T>() or GetRequiredService<T>()
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
            scopeVariables.Contains(scopeId.Identifier.Text))
        {
            return true;
        }

        return false;
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string escapeTarget)
    {
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ScopedServiceEscapes,
            invocation.GetLocation(),
            escapeTarget);

        context.ReportDiagnostic(diagnostic);
    }
}
