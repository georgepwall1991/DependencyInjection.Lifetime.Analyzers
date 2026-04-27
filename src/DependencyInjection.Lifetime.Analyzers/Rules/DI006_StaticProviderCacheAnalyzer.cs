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
    private const string DetectHolderPatternOption = "dotnet_code_quality.DI006.detect_holder_pattern";

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

            var optionsProvider = compilationContext.Options.AnalyzerConfigOptionsProvider;

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeField(symbolContext, wellKnownTypes, optionsProvider),
                SymbolKind.Field);

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeProperty(symbolContext, wellKnownTypes, optionsProvider),
                SymbolKind.Property);
        });
    }

    private static void AnalyzeField(
        SymbolAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var field = (IFieldSymbol)context.Symbol;

        if (!field.IsStatic)
        {
            return;
        }

        var detectHolderPattern = IsHolderPatternEnabled(optionsProvider, GetSourceTree(field));
        if (!TryGetStaticCacheTypeName(field.Type, wellKnownTypes, detectHolderPattern, out var typeName))
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

    private static void AnalyzeProperty(
        SymbolAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (!property.IsStatic)
        {
            return;
        }

        var detectHolderPattern = IsHolderPatternEnabled(optionsProvider, GetSourceTree(property));
        if (!TryGetStaticCacheTypeName(property.Type, wellKnownTypes, detectHolderPattern, out var typeName))
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
        bool detectHolderPattern,
        out string typeName)
    {
        if (wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(type))
        {
            typeName = GetSimpleTypeName(type);
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            // Single-arg wrappers: Lazy<T>, ThreadLocal<T>, AsyncLocal<T>, Task<T>, ValueTask<T>, Func<T>
            if (IsKnownProviderWrapper(namedType) &&
                namedType.TypeArguments.Length == 1 &&
                TryGetStaticCacheTypeName(namedType.TypeArguments[0], wellKnownTypes, detectHolderPattern, out var inner))
            {
                typeName = $"{GetSimpleTypeName(namedType)}<{inner}>";
                return true;
            }

            // Dictionary-of-providers shapes: TValue is a provider type.
            if (IsKnownProviderDictionary(namedType) &&
                namedType.TypeArguments.Length == 2 &&
                wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(namedType.TypeArguments[1]))
            {
                typeName = $"{GetSimpleTypeName(namedType)}<{GetSimpleTypeName(namedType.TypeArguments[0])}, {GetSimpleTypeName(namedType.TypeArguments[1])}>";
                return true;
            }

            if (detectHolderPattern &&
                TryGetProviderHolderTypeName(namedType, wellKnownTypes, out var holderTypeName))
            {
                typeName = holderTypeName;
                return true;
            }
        }

        typeName = string.Empty;
        return false;
    }

    private static bool TryGetProviderHolderTypeName(
        INamedTypeSymbol type,
        WellKnownTypes wellKnownTypes,
        out string typeName)
    {
        typeName = string.Empty;

        if (IsFrameworkType(type))
        {
            return false;
        }

        ITypeSymbol? heldType = null;
        var instanceMemberCount = 0;

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field:
                    instanceMemberCount++;
                    heldType = field.Type;
                    break;
                case IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false } property:
                    instanceMemberCount++;
                    heldType = property.Type;
                    break;
            }
        }

        if (instanceMemberCount != 1 ||
            heldType is null ||
            !TryGetStaticCacheTypeName(heldType, wellKnownTypes, detectHolderPattern: false, out var heldTypeName))
        {
            return false;
        }

        typeName = $"{GetSimpleTypeName(type)}<{heldTypeName}>";
        return true;
    }

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is not null &&
               (ns == "System" ||
                ns.StartsWith("System.") ||
                ns == "Microsoft.Extensions.DependencyInjection" ||
                ns.StartsWith("Microsoft.Extensions.DependencyInjection."));
    }

    private static bool IsHolderPatternEnabled(
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree? syntaxTree)
    {
        if (syntaxTree is not null &&
            TryParseHolderPatternOption(optionsProvider.GetOptions(syntaxTree), out var treeValue))
        {
            return treeValue;
        }

        if (TryParseHolderPatternOption(optionsProvider.GlobalOptions, out var globalValue))
        {
            return globalValue;
        }

        return true;
    }

    private static bool TryParseHolderPatternOption(
        AnalyzerConfigOptions options,
        out bool enabled)
    {
        enabled = true;

        if (!options.TryGetValue(DetectHolderPatternOption, out var value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsedValue))
        {
            return false;
        }

        enabled = parsedValue;
        return true;
    }

    private static SyntaxTree? GetSourceTree(ISymbol symbol)
    {
        return symbol.Locations.Length > 0 ? symbol.Locations[0].SourceTree : null;
    }

    private static bool IsKnownProviderWrapper(INamedTypeSymbol type)
    {
        var originalDefinition = type.OriginalDefinition;
        var ns = originalDefinition.ContainingNamespace?.ToDisplayString();
        var metadataName = originalDefinition.MetadataName;

        return (ns, metadataName) switch
        {
            ("System", "Lazy`1") => true,
            ("System.Threading", "ThreadLocal`1") => true,
            ("System.Threading", "AsyncLocal`1") => true,
            ("System.Threading.Tasks", "Task`1") => true,
            ("System.Threading.Tasks", "ValueTask`1") => true,
            ("System", "Func`1") => true,
            _ => false,
        };
    }

    private static bool IsKnownProviderDictionary(INamedTypeSymbol type)
    {
        var originalDefinition = type.OriginalDefinition;
        var ns = originalDefinition.ContainingNamespace?.ToDisplayString();
        var metadataName = originalDefinition.MetadataName;

        return (ns, metadataName) switch
        {
            ("System.Collections.Generic", "Dictionary`2") => true,
            ("System.Collections.Generic", "IDictionary`2") => true,
            ("System.Collections.Generic", "IReadOnlyDictionary`2") => true,
            ("System.Collections.Concurrent", "ConcurrentDictionary`2") => true,
            _ => false,
        };
    }

    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        return type.Name;
    }
}
