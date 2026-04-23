using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI013_ImplementationTypeMismatchAnalyzer : DiagnosticAnalyzer
{
    internal const string ServiceTypeNamePropertyName = "ServiceTypeName";
    internal const string ImplementationTypeNamePropertyName = "ImplementationTypeName";
    internal const string RegistrationShapePropertyName = "RegistrationShape";
    internal const string IsKeyedPropertyName = "IsKeyed";
    internal const string MismatchKindPropertyName = "MismatchKind";

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

            if (!IsCompatible(context.Compilation, registration.ServiceType, registration.ImplementationType))
            {
                var properties = ImmutableDictionary<string, string?>.Empty
                    .Add(ServiceTypeNamePropertyName, registration.ServiceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Add(ImplementationTypeNamePropertyName, registration.ImplementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Add(RegistrationShapePropertyName, registration.HasImplementationInstance ? "Instance" : "Type")
                    .Add(IsKeyedPropertyName, registration.IsKeyed.ToString())
                    .Add(MismatchKindPropertyName, registration.HasImplementationInstance ? "InstanceVsService" : "TypeVsType");

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ImplementationTypeMismatch,
                    registration.Location,
                    properties,
                    registration.ImplementationType.Name,
                    registration.ServiceType.Name));
            }
        }
    }

    private static bool IsCompatible(
        Compilation compilation,
        INamedTypeSymbol service,
        INamedTypeSymbol implementation)
    {
        if (service.IsUnboundGenericType)
        {
            return IsOpenGenericCompatible(service, implementation);
        }

        if (implementation.IsUnboundGenericType)
        {
            return false;
        }

        if (service.IsGenericType && implementation.IsGenericType)
        {
            return IsClosedTypeCompatible(compilation, service, implementation);
        }

        return IsClosedTypeCompatible(compilation, service, implementation);
    }

    private static bool IsOpenGenericCompatible(INamedTypeSymbol service, INamedTypeSymbol implementation)
    {
        if (!implementation.IsGenericType) return false;

        // Open generic registrations must pair an open service with an open implementation.
        if (!implementation.IsUnboundGenericType) return false;

        var implDef = implementation.OriginalDefinition;

        if (implDef.TypeParameters.Length != service.TypeParameters.Length) return false;

        if (SymbolEqualityComparer.Default.Equals(implDef, service.OriginalDefinition)) return true;

        var serviceArity = service.TypeParameters.Length;

        foreach (var iface in implDef.AllInterfaces)
        {
            if (MatchesOpenGenericProjection(iface, service, serviceArity)) return true;
        }

        var current = implDef.BaseType;
        while (current != null)
        {
            if (MatchesOpenGenericProjection(current, service, serviceArity)) return true;
            current = current.BaseType;
        }

        return false;
    }

    private static bool MatchesOpenGenericProjection(
        INamedTypeSymbol candidate,
        INamedTypeSymbol service,
        int serviceArity)
    {
        INamedTypeSymbol candidateUnbound;
        if (candidate.IsUnboundGenericType)
        {
            candidateUnbound = candidate;
        }
        else if (candidate.IsGenericType)
        {
            candidateUnbound = candidate.ConstructUnboundGenericType();
        }
        else
        {
            return serviceArity == 0;
        }

        if (!SymbolEqualityComparer.Default.Equals(candidateUnbound, service))
            return false;

        if (candidate.IsUnboundGenericType)
        {
            return candidate.TypeParameters.Length == serviceArity;
        }

        var typeArgs = candidate.TypeArguments;
        if (typeArgs.Length != serviceArity) return false;

        for (int i = 0; i < typeArgs.Length; i++)
        {
            var arg = typeArgs[i];
            if (arg is not ITypeParameterSymbol typeParam) return false;
            if (typeParam.Ordinal != i) return false;
        }

        return true;
    }

    private static bool IsClosedTypeCompatible(
        Compilation compilation,
        INamedTypeSymbol service,
        INamedTypeSymbol implementation) =>
        compilation is CSharpCompilation csharpCompilation &&
        csharpCompilation.ClassifyConversion(implementation, service).IsImplicit;
}
