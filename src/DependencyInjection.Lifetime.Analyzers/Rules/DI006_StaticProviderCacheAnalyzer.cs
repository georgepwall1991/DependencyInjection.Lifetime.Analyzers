using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects IServiceProvider or IServiceScopeFactory stored in static fields or properties.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI006_StaticProviderCacheAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.StaticProviderCache);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeField(symbolContext, wellKnownTypes),
                SymbolKind.Field);

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeProperty(symbolContext, wellKnownTypes),
                SymbolKind.Property);
        });
    }

    private static void AnalyzeField(SymbolAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var field = (IFieldSymbol)context.Symbol;

        if (!field.IsStatic)
        {
            return;
        }

        if (!wellKnownTypes.IsServiceProviderOrFactory(field.Type))
        {
            return;
        }

        var typeName = GetSimpleTypeName(field.Type);
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.StaticProviderCache,
            field.Locations[0],
            typeName,
            field.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (!property.IsStatic)
        {
            return;
        }

        if (!wellKnownTypes.IsServiceProviderOrFactory(property.Type))
        {
            return;
        }

        var typeName = GetSimpleTypeName(property.Type);
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.StaticProviderCache,
            property.Locations[0],
            typeName,
            property.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        return type.Name;
    }
}
