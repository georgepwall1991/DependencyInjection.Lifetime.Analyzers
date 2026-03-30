using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects registered implementation types that cannot be constructed
/// by the DI container: abstract classes, interfaces, static classes, and types
/// with no accessible constructors.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI018_NonInstantiableImplementationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.NonInstantiableImplementation);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeRegistrations(endContext, registrationCollector));
        });
    }

    private static void AnalyzeRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector)
    {
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            // Skip factory registrations — the factory is responsible for construction
            if (registration.FactoryExpression is not null)
            {
                continue;
            }

            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                continue;
            }

            var reason = GetNonInstantiableReason(implementationType);
            if (reason is null)
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.NonInstantiableImplementation,
                registration.Location,
                implementationType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                registration.ServiceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                reason);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static string? GetNonInstantiableReason(INamedTypeSymbol type)
    {
        // Static classes
        if (type.IsStatic)
        {
            return "type is static";
        }

        // Interfaces
        if (type.TypeKind == TypeKind.Interface)
        {
            return "type is an interface";
        }

        // Abstract classes
        if (type.IsAbstract)
        {
            return "type is abstract";
        }

        // No accessible constructors (public or internal)
        // Skip unbound generics — Roslyn reports their constructors differently
        // when type parameters are involved, leading to false positives.
        if (!type.IsUnboundGenericType &&
            type.TypeKind == TypeKind.Class &&
            !type.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
        {
            return "type has no accessible constructors";
        }

        return null;
    }
}
