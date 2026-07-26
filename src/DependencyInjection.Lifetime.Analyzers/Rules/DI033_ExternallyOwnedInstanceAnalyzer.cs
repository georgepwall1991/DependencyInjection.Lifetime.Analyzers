using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects a disposable instance handed to the container as a pre-built singleton.
/// The container disposes only what it creates, so such an instance is never disposed by the
/// provider and its resources outlive everything unless the caller disposes it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI033_ExternallyOwnedInstanceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ExternallyOwnedDisposableInstance);

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
                var mutations = registrationCollector.OrderedMutations.ToList();

                foreach (var registration in registrationCollector.AllRegistrations)
                {
                    if (
                        !registration.HasImplementationInstance
                        || registration.ImplementationType is not { } instanceType
                    )
                    {
                        continue;
                    }

                    if (
                        !wellKnownTypes.ImplementsIDisposable(instanceType)
                        && !wellKnownTypes.ImplementsIAsyncDisposable(instanceType)
                    )
                    {
                        continue;
                    }

                    // A descriptor removed or replaced after it was added never reaches the
                    // provider, so nobody is left holding it.
                    if (
                        mutations.Any(mutation =>
                            SymbolEqualityComparer.Default.Equals(
                                mutation.ServiceType,
                                registration.ServiceType
                            )
                            && IsAfter(mutation.Location, registration.Location)
                        )
                    )
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(
                        Diagnostic.Create(
                            DiagnosticDescriptors.ExternallyOwnedDisposableInstance,
                            registration.Location,
                            instanceType.Name
                        )
                    );
                }
            });
        });
    }

    /// <summary>Source order, treating locations in different files as incomparable.</summary>
    private static bool IsAfter(Location candidate, Location reference)
    {
        if (candidate.SourceTree?.FilePath != reference.SourceTree?.FilePath)
        {
            return false;
        }

        var byStart = candidate.SourceSpan.Start.CompareTo(reference.SourceSpan.Start);
        return byStart != 0
            ? byStart > 0
            : candidate.SourceSpan.End > reference.SourceSpan.End;
    }
}
