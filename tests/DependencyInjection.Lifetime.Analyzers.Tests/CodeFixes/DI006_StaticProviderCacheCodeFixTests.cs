using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI006 code fix: Remove static modifier from IServiceProvider/IServiceScopeFactory cache.
/// </summary>
public class DI006_StaticProviderCacheCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_RemovesStatic_FromPrivateField()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider;
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                private IServiceProvider _provider;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 46)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_RemovesStatic_FromPublicField()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider ServiceProvider;
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                public IServiceProvider ServiceProvider;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 51)
            .WithArguments("IServiceProvider", "ServiceProvider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_RemovesStatic_FromProperty()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider Provider { get; set; }
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                public IServiceProvider Provider { get; set; }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 44)
            .WithArguments("IServiceProvider", "Provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_RemovesStatic_FromIServiceScopeFactoryField()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceScopeFactory _scopeFactory;
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                private IServiceScopeFactory _scopeFactory;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 41, 5, 54)
            .WithArguments("IServiceScopeFactory", "_scopeFactory");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
