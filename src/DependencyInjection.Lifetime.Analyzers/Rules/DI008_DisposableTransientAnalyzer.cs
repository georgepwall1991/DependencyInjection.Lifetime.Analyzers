using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects transient services implementing IDisposable or IAsyncDisposable.
/// The DI container does not track transient services, so Dispose will never be called.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI008_DisposableTransientAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DisposableTransient);

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

        // Check if this is an AddTransient method on IServiceCollection
        if (!IsAddTransientMethod(methodSymbol))
        {
            return;
        }

        // Check if this is a factory registration (has lambda parameter)
        if (IsFactoryRegistration(methodSymbol, invocation))
        {
            return;
        }

        // Extract the implementation type
        var implementationType = ExtractImplementationType(methodSymbol, invocation, context.SemanticModel);
        if (implementationType is null)
        {
            return;
        }

        // Check if the implementation type is disposable
        var disposableInterface = wellKnownTypes.GetDisposableInterfaceName(implementationType);
        if (disposableInterface is null)
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DisposableTransient,
            invocation.GetLocation(),
            implementationType.Name,
            disposableInterface);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAddTransientMethod(IMethodSymbol method)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        // Check if the method name is AddTransient or AddKeyedTransient (for .NET 8+ keyed services)
        var methodName = originalMethod.Name;
        if (!methodName.StartsWith("AddTransient") && !methodName.StartsWith("AddKeyedTransient"))
        {
            return false;
        }

        // Check if the containing type is ServiceCollectionServiceExtensions
        var containingType = originalMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionServiceExtensions")
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return firstParam.Type.Name == "IServiceCollection";
    }

    private static bool IsFactoryRegistration(IMethodSymbol method, InvocationExpressionSyntax invocation)
    {
        // Check if any argument is a lambda or method group
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            switch (argument.Expression)
            {
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                case IdentifierNameSyntax: // Could be a method group
                    // Check if the method signature accepts a factory delegate
                    var originalMethod = method.ReducedFrom ?? method;
                    foreach (var param in originalMethod.Parameters)
                    {
                        // Factory parameters are typically Func<IServiceProvider, T>
                        if (param.Type is INamedTypeSymbol namedType &&
                            namedType.Name == "Func" &&
                            namedType.TypeArguments.Length >= 1)
                        {
                            return true;
                        }
                    }
                    break;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? ExtractImplementationType(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Pattern 1: Generic method AddTransient<TService>() or AddTransient<TService, TImplementation>()
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            // For AddTransient<TService, TImpl>, use TImpl
            // For AddTransient<TService>(), use TService as implementation
            return method.TypeArguments.Length > 1
                ? method.TypeArguments[1] as INamedTypeSymbol
                : method.TypeArguments[0] as INamedTypeSymbol;
        }

        // Pattern 2: Non-generic with Type parameters AddTransient(typeof(TService), typeof(TImpl))
        var typeofArgs = new System.Collections.Generic.List<INamedTypeSymbol>();
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                }
            }
        }

        if (typeofArgs.Count >= 1)
        {
            // If two typeof arguments, second is implementation; otherwise first is both service and impl
            return typeofArgs.Count > 1 ? typeofArgs[1] : typeofArgs[0];
        }

        return null;
    }
}
