using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects BuildServiceProvider usage while composing service registrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI016_BuildServiceProviderMisuseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BuildServiceProviderMisuse);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var iServiceCollectionType = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection");
            var iServiceProviderType = compilationContext.Compilation.GetTypeByMetadataName(
                "System.IServiceProvider");
            if (iServiceCollectionType is null)
            {
                return;
            }

            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(
                    operationContext,
                    iServiceCollectionType,
                    iServiceProviderType),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol iServiceCollectionType,
        INamedTypeSymbol? iServiceProviderType)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsBuildServiceProviderInvocation(invocation, iServiceCollectionType))
        {
            return;
        }

        if (!IsRegistrationContext(
                invocation,
                context.Compilation,
                iServiceCollectionType,
                iServiceProviderType))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.BuildServiceProviderMisuse,
            invocation.Syntax.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsBuildServiceProviderInvocation(
        IInvocationOperation invocation,
        INamedTypeSymbol iServiceCollectionType)
    {
        var sourceMethod = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (sourceMethod.Name != "BuildServiceProvider")
        {
            return false;
        }

        var containingType = sourceMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionContainerBuilderExtensions" ||
            containingType.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (!sourceMethod.IsExtensionMethod || sourceMethod.Parameters.Length == 0)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(sourceMethod.Parameters[0].Type, iServiceCollectionType);
    }

    private static bool IsRegistrationContext(
        IInvocationOperation invocation,
        Compilation compilation,
        INamedTypeSymbol iServiceCollectionType,
        INamedTypeSymbol? iServiceProviderType)
    {
        var syntax = invocation.Syntax;

        #pragma warning disable RS1030
        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
        #pragma warning restore RS1030

        for (var node = syntax.Parent; node is not null; node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                if (semanticModel.GetSymbolInfo(anonymousFunction).Symbol is IMethodSymbol anonymousMethod &&
                    HasIServiceCollectionParameter(anonymousMethod, iServiceCollectionType))
                {
                    return !ReturnsIServiceProvider(anonymousMethod, iServiceProviderType);
                }

                continue;
            }

            if (node is LocalFunctionStatementSyntax localFunction)
            {
                if (semanticModel.GetDeclaredSymbol(localFunction) is not IMethodSymbol localMethod)
                {
                    continue;
                }

                if (!HasIServiceCollectionParameter(localMethod, iServiceCollectionType))
                {
                    continue;
                }

                return !ReturnsIServiceProvider(localMethod, iServiceProviderType);
            }

            if (node is MethodDeclarationSyntax methodDeclaration)
            {
                if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
                {
                    return false;
                }

                if (!HasIServiceCollectionParameter(methodSymbol, iServiceCollectionType))
                {
                    return false;
                }

                return !ReturnsIServiceProvider(methodSymbol, iServiceProviderType);
            }

            if (node is ConstructorDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool HasIServiceCollectionParameter(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol iServiceCollectionType)
    {
        foreach (var parameter in methodSymbol.Parameters)
        {
            if (SymbolEqualityComparer.Default.Equals(parameter.Type, iServiceCollectionType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReturnsIServiceProvider(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? iServiceProviderType)
    {
        return iServiceProviderType is not null &&
               SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, iServiceProviderType);
    }
}
