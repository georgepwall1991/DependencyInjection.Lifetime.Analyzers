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
                var mutations = registrationCollector.OrderedMutations.ToList();

                foreach (var registration in registrationCollector.AllRegistrations)
                {
                    // Only instances the container creates are tracked for disposal; a pre-built
                    // instance is the caller's to dispose (DI033).
                    if (
                        registration.HasImplementationInstance
                        // Transient disposables are DI008's finding: it already reports the
                        // whole tracking-and-disposal problem for them, and a second diagnostic
                        // on the same registration is noise.
                        || registration.Lifetime
                            is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped)
                    )
                    {
                        continue;
                    }

                    // A factory result is created by the container and tracked exactly like a type
                    // registration, so a lambda that constructs a known type counts too.
                    var implementationType =
                        registration.ImplementationType
                        ?? GetConstructedFactoryType(
                            registration.FactoryExpression,
                            registration.ServiceType,
                            endContext.Compilation
                        );
                    if (implementationType is null)
                    {
                        continue;
                    }

                    // A descriptor removed or replaced after it was added never reaches the
                    // provider, so it cannot make disposal throw.
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

    /// <summary>
    /// The type a factory lambda constructs when its body is a single object creation. Anything
    /// less direct — a branch, a helper call, a builder — is left alone.
    /// </summary>
    private static INamedTypeSymbol? GetConstructedFactoryType(
        ExpressionSyntax? factoryExpression,
        INamedTypeSymbol serviceType,
        Compilation compilation
    )
    {
        if (factoryExpression is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        var returned = lambda.Body switch
        {
            ExpressionSyntax expressionBody => expressionBody,
            BlockSyntax block
                when block.Statements.Count == 1
                    && block.Statements[0]
                        is ReturnStatementSyntax { Expression: { } returnedValue } => returnedValue,
            _ => null,
        };

        // Parentheses preserve the value; `new()` is target-typed but still an object creation.
        while (returned is ParenthesizedExpressionSyntax parenthesized)
        {
            returned = parenthesized.Expression;
        }

        var created = returned as BaseObjectCreationExpressionSyntax;

        if (created is null || !compilation.ContainsSyntaxTree(created.SyntaxTree))
        {
            return null;
        }

        var semanticModel = compilation.GetSemanticModel(created.SyntaxTree);
        if (semanticModel.GetTypeInfo(created).Type is not INamedTypeSymbol createdType)
        {
            return null;
        }

        // A user-defined conversion means the container never holds the constructed object at
        // all, so its disposal story says nothing about the registered service.
        var conversion = compilation.ClassifyConversion(createdType, serviceType);
        return conversion.IsIdentity || conversion.IsReference ? createdType : null;
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
