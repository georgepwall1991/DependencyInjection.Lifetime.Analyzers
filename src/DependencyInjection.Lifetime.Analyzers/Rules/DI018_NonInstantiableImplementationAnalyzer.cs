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
/// by the DI container: abstract classes, interfaces, static classes, delegates,
/// value types with no declared public constructor, and types with no accessible constructors.
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
            if (registration.FactoryExpression is not null || registration.HasImplementationInstance)
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

        // Delegates carry implicit public (object, IntPtr) constructors that the
        // default DI container cannot populate, so a non-factory registration of
        // a delegate type fails at activation. The user must register the delegate
        // through a factory expression.
        if (type.TypeKind == TypeKind.Delegate)
        {
            return "type is a delegate";
        }

        // Reflection does not surface a value type's implicit parameterless constructor,
        // so constructor activation fails unless the value type declares a public one.
        // Factory and instance registrations are excluded before this method is called.
        if (type.IsValueType &&
            !type.InstanceConstructors.Any(c =>
                !c.IsImplicitlyDeclared &&
                c.DeclaredAccessibility == Accessibility.Public))
        {
            return "value type has no declared public constructors";
        }

        // No public constructors — matches CanSelfBind in DependencyResolutionEngine
        // which requires public constructors for activation.
        if (type.TypeKind == TypeKind.Class &&
            !HasPublicConstructorForRegistration(type))
        {
            return "type has no accessible constructors";
        }

        return null;
    }

    private static bool HasPublicConstructorForRegistration(INamedTypeSymbol type)
    {
        // Open generic registrations are validated on the generic type definition
        // rather than CanSelfBind semantics, so constructor visibility must be
        // read from OriginalDefinition for unbound generic types.
        var typeToCheck = type.IsUnboundGenericType ? type.OriginalDefinition : type;
        return typeToCheck.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public);
    }
}
