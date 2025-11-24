using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects CreateScope() usage in async methods where CreateAsyncScope() should be used.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI005_AsyncDisposalAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.AsyncScopeRequired);

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
                syntaxContext => AnalyzeInvocation(syntaxContext, wellKnownTypes),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a call to CreateScope
        if (!IsCreateScopeInvocation(invocation))
        {
            return;
        }

        // Check if this invocation is inside an async context
        var containingMethod = GetContainingAsyncContext(invocation);
        if (containingMethod is null)
        {
            return;
        }

        // Verify this is actually IServiceScopeFactory.CreateScope()
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check if the method is CreateScope on IServiceScopeFactory
        if (methodSymbol.Name != "CreateScope")
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
        {
            return;
        }

        // Check if it's IServiceScopeFactory.CreateScope or the extension method
        if (!wellKnownTypes.IsServiceScopeFactory(containingType) &&
            !IsServiceProviderServiceExtensionsCreateScope(containingType, methodSymbol))
        {
            return;
        }

        var methodName = GetContainingMethodName(containingMethod);
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.AsyncScopeRequired,
            invocation.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsCreateScopeInvocation(InvocationExpressionSyntax invocation)
    {
        // Check for pattern: xxx.CreateScope()
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "CreateScope";
        }

        // Check for pattern: CreateScope() (unlikely but handle it)
        if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text == "CreateScope";
        }

        return false;
    }

    private static SyntaxNode? GetContainingAsyncContext(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            switch (current)
            {
                // Check for async method
                case MethodDeclarationSyntax methodDecl when methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return methodDecl;

                // Check for async local function
                case LocalFunctionStatementSyntax localFunc when localFunc.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return localFunc;

                // Check for async lambda
                case ParenthesizedLambdaExpressionSyntax lambda when lambda.AsyncKeyword != default:
                    return lambda;

                case SimpleLambdaExpressionSyntax simpleLambda when simpleLambda.AsyncKeyword != default:
                    return simpleLambda;

                // Check for async anonymous method
                case AnonymousMethodExpressionSyntax anonymousMethod when anonymousMethod.AsyncKeyword != default:
                    return anonymousMethod;

                // If we reach a non-async method boundary, stop searching
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    return null;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetContainingMethodName(SyntaxNode asyncContext)
    {
        return asyncContext switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            LocalFunctionStatementSyntax localFunc => localFunc.Identifier.Text,
            _ => string.Empty // Anonymous methods and lambdas don't have names
        };
    }

    private static bool IsServiceProviderServiceExtensionsCreateScope(ITypeSymbol containingType, IMethodSymbol method)
    {
        // Check for ServiceProviderServiceExtensions.CreateScope extension method
        if (containingType.Name == "ServiceProviderServiceExtensions" &&
            method.IsExtensionMethod &&
            method.Name == "CreateScope")
        {
            return true;
        }

        return false;
    }
}
