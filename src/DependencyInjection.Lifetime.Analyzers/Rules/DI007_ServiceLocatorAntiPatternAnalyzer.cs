using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects service locator anti-pattern - using IServiceProvider to resolve services
/// instead of constructor injection.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI007_ServiceLocatorAntiPatternAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ServiceLocatorAntiPattern);

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

        // Get the method symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check if this is a GetService/GetRequiredService call
        if (!IsServiceResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return;
        }

        // Get the resolved service type
        var resolvedType = GetResolvedServiceType(methodSymbol);
        if (resolvedType is null)
        {
            return;
        }

        // Check if we're in an allowed context (factory registration, middleware Invoke, etc.)
        if (IsAllowedContext(invocation, context.SemanticModel))
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ServiceLocatorAntiPattern,
            invocation.GetLocation(),
            resolvedType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol method, WellKnownTypes wellKnownTypes)
    {
        // Check for extension methods on IServiceProvider
        var originalMethod = method.ReducedFrom ?? method;

        // Check if it's an extension method from ServiceProviderServiceExtensions
        if (originalMethod.IsExtensionMethod)
        {
            var containingType = originalMethod.ContainingType;
            if (containingType?.Name == "ServiceProviderServiceExtensions")
            {
                var methodName = originalMethod.Name;
                return methodName == "GetService" ||
                       methodName == "GetRequiredService" ||
                       methodName == "GetServices";
            }
        }

        // Check for IServiceProvider.GetService directly
        if (method.Name == "GetService" && method.Parameters.Length == 1)
        {
            var containingType = method.ContainingType;
            if (containingType?.Name == "IServiceProvider")
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? GetResolvedServiceType(IMethodSymbol method)
    {
        // For generic methods like GetService<T>, get T
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            return method.TypeArguments[0];
        }

        // For non-generic methods, the type is passed as a parameter
        // We can't easily determine the type in that case, so return a placeholder
        return method.ReturnType;
    }

    private static bool IsAllowedContext(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Walk up the syntax tree to check the context
        var node = invocation.Parent;
        while (node is not null)
        {
            switch (node)
            {
                // Check if we're inside a lambda/anonymous function (factory registration)
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    return IsFactoryRegistrationContext(node, semanticModel);

                // Check if we're inside a method
                case MethodDeclarationSyntax methodDecl:
                    return IsAllowedMethod(methodDecl, semanticModel);

                // Check if we're inside a constructor
                case ConstructorDeclarationSyntax:
                    // Service locator in constructors is generally not allowed
                    return false;
            }

            node = node.Parent;
        }

        return false;
    }

    private static bool IsFactoryRegistrationContext(SyntaxNode lambda, SemanticModel semanticModel)
    {
        // Check if the lambda is an argument to an Add* method on IServiceCollection
        var parent = lambda.Parent;
        while (parent is not null)
        {
            if (parent is InvocationExpressionSyntax parentInvocation)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(parentInvocation);
                if (symbolInfo.Symbol is IMethodSymbol parentMethod)
                {
                    var originalMethod = parentMethod.ReducedFrom ?? parentMethod;
                    var containingType = originalMethod.ContainingType;

                    // Check if it's a service registration method
                    if (containingType?.Name == "ServiceCollectionServiceExtensions" &&
                        (originalMethod.Name.StartsWith("Add") || originalMethod.Name == "TryAdd"))
                    {
                        return true;
                    }
                }
            }

            // Don't traverse too far up
            if (parent is MethodDeclarationSyntax || parent is ClassDeclarationSyntax)
            {
                break;
            }

            parent = parent.Parent;
        }

        return false;
    }

    private static bool IsAllowedMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var methodName = methodDecl.Identifier.Text;

        // Allow in middleware Invoke/InvokeAsync methods
        if (methodName == "Invoke" || methodName == "InvokeAsync")
        {
            return true;
        }

        // Allow in methods named Create* or Build* (factory pattern)
        if (methodName.StartsWith("Create") || methodName.StartsWith("Build"))
        {
            return true;
        }

        // Check if method has IServiceProvider parameter (factory delegate pattern)
        foreach (var parameter in methodDecl.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                var typeInfo = semanticModel.GetTypeInfo(parameter.Type);
                if (typeInfo.Type?.Name == "IServiceProvider")
                {
                    return true;
                }
            }
        }

        return false;
    }
}
