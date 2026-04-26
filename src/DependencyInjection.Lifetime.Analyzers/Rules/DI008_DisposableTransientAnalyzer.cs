using System.Collections.Generic;
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
        if (!IsAddTransientMethod(methodSymbol, wellKnownTypes))
        {
            return;
        }

        // Check if this is a factory registration (has lambda parameter)
        if (IsFactoryRegistration(methodSymbol, invocation, context.SemanticModel))
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

    private static bool IsAddTransientMethod(IMethodSymbol method, WellKnownTypes wellKnownTypes)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        // Check if the method name is AddTransient or AddKeyedTransient (for .NET 8+ keyed services)
        var methodName = originalMethod.Name;
        if (methodName is not ("AddTransient" or "AddKeyedTransient"))
        {
            return false;
        }

        // Check if the containing type is the framework ServiceCollectionServiceExtensions symbol
        var containingType = originalMethod.ContainingType;
        if (!wellKnownTypes.IsServiceCollectionServiceExtensions(containingType))
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return wellKnownTypes.IsServiceCollection(firstParam.Type);
    }

    private static bool IsFactoryRegistration(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var args = invocation.ArgumentList.Arguments;

        for (int i = 0; i < args.Count; i++)
        {
            if (!TryGetArgumentParameter(method, args[i], i, out var param))
            {
                continue;
            }

            // Check if the parameter type is a delegate (Func<...>)
            if (param.Type is not INamedTypeSymbol paramNamed ||
                paramNamed.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var argExpr = args[i].Expression;

            // Lambda or anonymous method — always a factory
            if (argExpr is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                return true;
            }

            // Check if the argument binds to a delegate type (lambda, method group, etc.)
            var argType = semanticModel.GetTypeInfo(argExpr).ConvertedType;
            if (argType is INamedTypeSymbol namedType &&
                namedType.DelegateInvokeMethod is not null)
            {
                return true;
            }

            // Fallback: if the argument is an identifier or member access and the
            // parameter is delegate-typed, treat it as a factory (method group)
            if (argExpr is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                return true;
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

        // Pattern 2: Non-generic with Type parameters AddTransient(typeof(TService), typeof(TImpl)).
        // Prefer Roslyn's argument-to-parameter binding so named arguments can appear
        // out of source order without changing which type is the implementation.
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        var typeofArgs = new List<INamedTypeSymbol>();
        var args = invocation.ArgumentList.Arguments;
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                    if (TryGetArgumentParameter(method, arg, i, out var parameter))
                    {
                        switch (parameter.Name)
                        {
                            case "implementationType":
                                implementationType = namedType;
                                break;
                            case "serviceType":
                                serviceType = namedType;
                                break;
                        }
                    }
                }
            }
        }

        if (implementationType is not null)
        {
            return implementationType;
        }

        if (serviceType is not null)
        {
            return serviceType;
        }

        if (typeofArgs.Count >= 1)
        {
            // If two typeof arguments, second is implementation; otherwise first is both service and impl
            return typeofArgs.Count > 1 ? typeofArgs[1] : typeofArgs[0];
        }

        return null;
    }

    private static bool TryGetArgumentParameter(
        IMethodSymbol method,
        ArgumentSyntax argument,
        int argumentIndex,
        out IParameterSymbol parameter)
    {
        var originalMethod = method.ReducedFrom ?? method;

        if (argument.NameColon is { } nameColon)
        {
            var argumentName = nameColon.Name.Identifier.ValueText;
            foreach (var candidate in originalMethod.Parameters)
            {
                if (candidate.Name == argumentName)
                {
                    parameter = candidate;
                    return true;
                }
            }

            parameter = null!;
            return false;
        }

        // Reduced extension-method invocations omit the receiver from the argument list,
        // while static extension calls include it. Adjust only for the reduced form.
        var parameterIndex = argumentIndex + (method.ReducedFrom is not null ? 1 : 0);
        if (parameterIndex >= 0 && parameterIndex < originalMethod.Parameters.Length)
        {
            parameter = originalMethod.Parameters[parameterIndex];
            return true;
        }

        parameter = null!;
        return false;
    }
}
