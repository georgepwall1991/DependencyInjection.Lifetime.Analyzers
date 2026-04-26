using System.Collections.Immutable;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects provider abstractions stored in static fields or properties.
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

        if (!TryGetStaticCacheTypeName(field.Type, wellKnownTypes, out var typeName))
        {
            return;
        }

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

        if (!TryGetStaticCacheTypeName(property.Type, wellKnownTypes, out var typeName))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.StaticProviderCache,
            property.Locations[0],
            typeName,
            property.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryGetStaticCacheTypeName(
        ITypeSymbol type,
        WellKnownTypes wellKnownTypes,
        out string typeName)
    {
        if (wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            typeName = GetSimpleTypeName(type);
            return true;
        }

        if (type is INamedTypeSymbol namedType &&
            IsSystemLazy(namedType) &&
            namedType.TypeArguments.Length == 1 &&
            wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(namedType.TypeArguments[0]))
        {
            typeName = $"{GetSimpleTypeName(namedType)}<{GetSimpleTypeName(namedType.TypeArguments[0])}>";
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    private static bool IsSystemLazy(INamedTypeSymbol type)
    {
        var originalDefinition = type.OriginalDefinition;
        return originalDefinition.MetadataName == "Lazy`1" &&
               originalDefinition.ContainingNamespace.ToDisplayString() == "System";
    }

    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        return type.Name;
    }
}
