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

    #endregion
}
