using System.Collections.Immutable;
using System.Linq;
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
            if (registration.ImplementationType is null)
            {
                continue;
            }

            if (!IsCompatible(
                    registration.ServiceType,
                    registration.ImplementationType,
                    registration.HasImplementationInstance))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ImplementationTypeMismatch,
                    registration.Location,
                    registration.ImplementationType.Name,
                    registration.ServiceType.Name));
            }
        }
    }

    private static bool IsCompatible(
        INamedTypeSymbol service,
        INamedTypeSymbol implementation,
        bool hasImplementationInstance)
    {
        if (service.IsUnboundGenericType || implementation.IsUnboundGenericType)
        {
            return !hasImplementationInstance &&
                   IsCompatibleOpenGenericRegistration(service, implementation);
        }

        if (!IsClosedServiceAssignableFromImplementation(service, implementation))
        {
            return false;
        }

        return hasImplementationInstance ||
               IsClosedImplementationActivatable(implementation);
    }

    private static bool IsCompatibleOpenGenericRegistration(
        INamedTypeSymbol service,
        INamedTypeSymbol implementation)
    {
        if (!service.IsUnboundGenericType ||
            !implementation.IsUnboundGenericType)
        {
            return false;
        }

        var serviceDefinition = service.OriginalDefinition;
        var implementationDefinition = implementation.OriginalDefinition;

        if (implementationDefinition.IsAbstract ||
            implementationDefinition.TypeKind is TypeKind.Interface or TypeKind.TypeParameter or TypeKind.Delegate ||
            implementationDefinition.Arity != serviceDefinition.Arity ||
            !HasUsableOpenGenericConstructors(implementationDefinition))
        {
            return false;
        }

        return GetOpenGenericProjections(implementationDefinition)
            .Any(projection =>
                SymbolEqualityComparer.Default.Equals(projection.OriginalDefinition, serviceDefinition) &&
                HasExactTypeParameterProjection(projection, implementationDefinition.TypeParameters));
    }

    private static bool IsClosedServiceAssignableFromImplementation(
        INamedTypeSymbol service,
        INamedTypeSymbol implementation)
    {
        if (SymbolEqualityComparer.Default.Equals(service, implementation))
        {
            return true;
        }

        foreach (var iface in implementation.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, service))
            {
                return true;
            }
        }

        for (var current = implementation.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, service))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClosedImplementationActivatable(INamedTypeSymbol implementation)
    {
        if (implementation.IsAbstract ||
            implementation.TypeKind is TypeKind.Interface or TypeKind.TypeParameter or TypeKind.Delegate ||
            implementation.IsUnboundGenericType ||
            implementation.IsGenericType && implementation.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            return false;
        }

        if (implementation.TypeKind == TypeKind.Struct)
        {
            return true;
        }

        if (implementation.TypeKind != TypeKind.Class)
        {
            return false;
        }

        return implementation.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool HasUsableOpenGenericConstructors(INamedTypeSymbol implementationDefinition)
    {
        return implementationDefinition.TypeKind == TypeKind.Struct ||
               implementationDefinition.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool HasExactTypeParameterProjection(
        INamedTypeSymbol projection,
        ImmutableArray<ITypeParameterSymbol> implementationTypeParameters)
    {
        if (projection.TypeArguments.Length != implementationTypeParameters.Length)
        {
            return false;
        }

        for (var index = 0; index < projection.TypeArguments.Length; index++)
        {
            if (projection.TypeArguments[index] is not ITypeParameterSymbol typeParameter ||
                !SymbolEqualityComparer.Default.Equals(typeParameter, implementationTypeParameters[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<INamedTypeSymbol> GetOpenGenericProjections(INamedTypeSymbol implementationDefinition)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        builder.Add(implementationDefinition);

        foreach (var iface in implementationDefinition.AllInterfaces)
        {
            builder.Add(iface);
        }

        for (var current = implementationDefinition.BaseType; current is not null; current = current.BaseType)
        {
            builder.Add(current);
        }

        return builder.ToImmutable();
    }
}
