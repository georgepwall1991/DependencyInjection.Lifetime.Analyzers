using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI013_ImplementationTypeMismatchAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ImplementationTypeMismatch);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var collector = RegistrationCollector.Create(compilationContext.Compilation);
            if (collector == null) return;

            compilationContext.RegisterSyntaxNodeAction(
                ctx => collector.AnalyzeInvocation((Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel),
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(ctx => AnalyzeRegistrations(ctx, collector));
        });
    }

    private static void AnalyzeRegistrations(CompilationAnalysisContext context, RegistrationCollector collector)
    {
        foreach (var registration in collector.AllRegistrations)
        {
            if (registration.ImplementationType == null) continue;

            // Skip self-registrations where Impl == Service (e.g. AddSingleton<Service>())
            // RegistrationCollector sets ImplementationType = ServiceType for these.
            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, registration.ImplementationType))
            {
                continue;
            }

            // Check compatibility
            if (!IsCompatible(registration.ServiceType, registration.ImplementationType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ImplementationTypeMismatch,
                    registration.Location,
                    registration.ImplementationType.Name,
                    registration.ServiceType.Name));
            }
        }
    }

    private static bool IsCompatible(INamedTypeSymbol service, INamedTypeSymbol implementation)
    {
        // Handle Open Generics (unbound)
        if (service.IsUnboundGenericType)
        {
            // Implementation must also be generic
            if (!implementation.IsGenericType) return false;

            var originalService = service.OriginalDefinition;

            // Check if implementation (definition) implements service (definition)
            
            // 1. Check direct identity (e.g. Add(typeof(Repo<>), typeof(Repo<>))) - usually caught by self-reg check but good for safety
            if (SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, originalService)) return true;

            // 2. Check interfaces
            foreach (var iface in implementation.OriginalDefinition.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, originalService)) return true;
            }

            // 3. Check base classes
            var current = implementation.OriginalDefinition.BaseType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, originalService)) return true;
                current = current.BaseType;
            }

            return false;
        }

        // Standard check for closed types
        
        // 1. Check interfaces
        foreach (var iface in implementation.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, service)) return true;
        }

        // 2. Check base classes
        var baseType = implementation.BaseType;
        while (baseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, service)) return true;
            baseType = baseType.BaseType;
        }

        return false;
    }
}
