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
/// Analyzer that detects one implementation type registered under several service types with a
/// shared lifetime. Each registration is its own descriptor, so the container builds a separate
/// instance per service type — the opposite of what "singleton" reads as at the call site.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI031_SharedImplementationSeparateInstancesAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.SharedImplementationSeparateInstances);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(
                compilationContext.Compilation
            );
            if (registrationCollector is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel
                    ),
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
                AnalyzeRegistrations(endContext, registrationCollector)
            );
        });
    }

    private static void AnalyzeRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector
    )
    {
        // Only type registrations share an instance question. A factory or a pre-built instance
        // states its own answer, and transient means "a new one every time" by definition.
        var candidates = registrationCollector
            .AllRegistrations.Where(registration =>
                registration.ImplementationType is not null
                && registration.FactoryExpression is null
                && !registration.HasImplementationInstance
                && !registration.IsKeyed
                && registration.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped
                && registration.Location.SourceTree is not null
                && IsUnconditionalRegistration(registration.Location)
            )
            .ToList();

        var groups = candidates.GroupBy(
            registration =>
                (
                    Implementation: (ISymbol)registration.ImplementationType!.OriginalDefinition,
                    registration.Lifetime,
                    registration.FlowKey
                ),
            ImplementationGroupComparer.Instance
        );

        foreach (var group in groups)
        {
            var distinctByServiceType = group
                .GroupBy(
                    registration => (ISymbol)registration.ServiceType.OriginalDefinition,
                    SymbolEqualityComparer.Default
                )
                .Select(serviceTypeGroup =>
                    serviceTypeGroup.OrderBy(registration => registration.Order).First()
                )
                .OrderBy(registration => registration.Order)
                .ToList();

            if (distinctByServiceType.Count < 2)
            {
                continue;
            }

            var first = distinctByServiceType[0];
            foreach (var duplicate in distinctByServiceType.Skip(1))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.SharedImplementationSeparateInstances,
                        duplicate.Location,
                        duplicate.ImplementationType!.Name,
                        duplicate.Lifetime.ToString().ToLowerInvariant(),
                        first.ServiceType.Name,
                        duplicate.ServiceType.Name
                    )
                );
            }
        }
    }

    /// <summary>
    /// Registrations guarded by a branch may never both run, so the two-instance claim cannot be
    /// made. Anything inside an if, switch, loop, try, or conditional expression is left alone.
    /// </summary>
    private static bool IsUnconditionalRegistration(Location location)
    {
        if (location.SourceTree?.GetRoot().FindNode(location.SourceSpan) is not { } node)
        {
            return false;
        }

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (
                current
                is IfStatementSyntax
                    or SwitchStatementSyntax
                    or SwitchExpressionSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or ForStatementSyntax
                    or ForEachStatementSyntax
                    or TryStatementSyntax
                    or ConditionalExpressionSyntax
                    or ConditionalAccessExpressionSyntax
            )
            {
                return false;
            }

            if (
                current
                is MethodDeclarationSyntax
                    or LocalFunctionStatementSyntax
                    or CompilationUnitSyntax
            )
            {
                break;
            }
        }

        return true;
    }

    private sealed class ImplementationGroupComparer
        : IEqualityComparer<(ISymbol Implementation, ServiceLifetime Lifetime, string? FlowKey)>
    {
        public static readonly ImplementationGroupComparer Instance = new();

        public bool Equals(
            (ISymbol Implementation, ServiceLifetime Lifetime, string? FlowKey) x,
            (ISymbol Implementation, ServiceLifetime Lifetime, string? FlowKey) y
        ) =>
            SymbolEqualityComparer.Default.Equals(x.Implementation, y.Implementation)
            && x.Lifetime == y.Lifetime
            && x.FlowKey == y.FlowKey;

        public int GetHashCode(
            (ISymbol Implementation, ServiceLifetime Lifetime, string? FlowKey) obj
        ) => SymbolEqualityComparer.Default.GetHashCode(obj.Implementation);
    }
}
