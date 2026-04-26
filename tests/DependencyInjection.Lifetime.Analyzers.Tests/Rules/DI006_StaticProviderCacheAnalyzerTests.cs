using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI006_StaticProviderCacheAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task StaticField_IServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider;
            }
            """;

        // Symbol analysis reports on the symbol location (the identifier), not the full declaration
        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 37, 5, 46)
                .WithArguments("IServiceProvider", "_provider"));
    }

    [Fact]
    public async Task StaticField_IServiceScopeFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceScopeFactory _scopeFactory;
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 41, 5, 54)
                .WithArguments("IServiceScopeFactory", "_scopeFactory"));
    }

    [Fact]
    public async Task StaticProperty_IServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider Provider { get; set; }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 36, 5, 44)
                .WithArguments("IServiceProvider", "Provider"));
    }

    [Fact]
    public async Task PublicStaticField_IServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider ServiceProvider;
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 36, 5, 51)
                .WithArguments("IServiceProvider", "ServiceProvider"));
    }

    [Fact]
    public async Task StaticField_LazyIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static Lazy<IServiceProvider> _provider = new(() => null!);
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 43, 5, 52)
                .WithArguments("Lazy<IServiceProvider>", "_provider"));
    }

    [Fact]
    public async Task StaticProperty_LazyIServiceScopeFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                public static Lazy<IServiceScopeFactory> ScopeFactory { get; } = new(() => null!);
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 46, 5, 58)
                .WithArguments("Lazy<IServiceScopeFactory>", "ScopeFactory"));
    }

    [Fact]
    public async Task StaticField_LazyIKeyedServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static Lazy<IKeyedServiceProvider> _provider = new(() => null!);
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.ReferenceAssembliesWithLatestKeyedDi,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 48, 5, 57)
                .WithArguments("Lazy<IKeyedServiceProvider>", "_provider"));
    }

    #endregion

    #region Should Not Report Diagnostic (False Positives)

    [Fact]
    public async Task InstanceField_IServiceProvider_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InstanceProperty_IServiceProvider_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                public IServiceProvider Provider { get; }

                public MyClass(IServiceProvider provider)
                {
                    Provider = provider;
                }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StaticField_OtherType_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static string _name;
                private static int _count;
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InstanceField_IServiceScopeFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StaticField_LazyOtherType_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private static Lazy<string> _name = new(() => "name");
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Additional Coverage

    [Fact]
    public async Task StaticField_InheritedServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyProvider : IServiceProvider { }

            public class MyClass
            {
                private static IMyProvider _provider;
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(7, 32, 7, 41)
                .WithArguments("IMyProvider", "_provider"));
    }

    [Fact]
    public async Task StaticProperty_InheritedServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyScopeFactory : IServiceScopeFactory { }

            public class MyClass
            {
                public static IMyScopeFactory ScopeFactory { get; set; }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(7, 35, 7, 47)
                .WithArguments("IMyScopeFactory", "ScopeFactory"));
    }

    [Fact]
    public async Task StaticField_InStaticClass_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class AppServices
            {
                public static IServiceProvider Provider { get; set; }
            }
            """;

        await AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI006_StaticProviderCacheAnalyzer>
                .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
                .WithSpan(5, 36, 5, 44)
                .WithArguments("IServiceProvider", "Provider"));
    }

    #endregion
}
