using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects container-created services implementing only <c>IAsyncDisposable</c>.
/// The container tracks them for disposal, but a synchronous <c>Dispose()</c> on the provider or
/// scope cannot dispose them and throws <c>InvalidOperationException</c> instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI032_AsyncOnlyDisposableRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.AsyncOnlyDisposableRegistration);

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

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
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
            {
                foreach (var registration in registrationCollector.AllRegistrations)
                {
                    // Only instances the container creates are tracked for disposal. A pre-built
                    // instance is the caller's to dispose (DI033), and a factory registration
                    // states its own ownership.
                    if (
                        registration.ImplementationType is not { } implementationType
                        || registration.HasImplementationInstance
                        // Transient disposables are DI008's finding: it already reports the
                        // whole tracking-and-disposal problem for them, and a second diagnostic
                        // on the same registration is noise.
                        || registration.Lifetime
                            is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped)
                    )
                    {
                        continue;
                    }

                    if (
                        !wellKnownTypes.ImplementsIAsyncDisposable(implementationType)
                        || wellKnownTypes.ImplementsIDisposable(implementationType)
                    )
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(
                        Diagnostic.Create(
                            DiagnosticDescriptors.AsyncOnlyDisposableRegistration,
                            registration.Location,
                            implementationType.Name
                        )
                    );
                }
            });
        });
    }
}
