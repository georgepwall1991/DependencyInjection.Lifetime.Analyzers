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
    public async Task CodeFix_NotOffered_ForPublicField_CrossTypeReferencesPossible()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider ServiceProvider;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 51)
            .WithArguments("IServiceProvider", "ServiceProvider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForPublicProperty_CrossTypeReferencesPossible()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider Provider { get; set; }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 44)
            .WithArguments("IServiceProvider", "Provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_RemovesStatic_FromPrivateProperty()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider Provider { get; set; }
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                private IServiceProvider Provider { get; set; }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 45)
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

    [Fact]
    public async Task CodeFix_NotOffered_WhenStaticFieldIsUsedFromStaticMethod()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider;

                public static object? Resolve()
                {
                    return _provider?.GetService(typeof(object));
                }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 46)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenStaticPropertyIsUsedFromStaticMethod()
    {
        var source = Usings + """
            public class MyClass
            {
                public static IServiceProvider Provider { get; set; }

                public static object? Resolve()
                {
                    return Provider?.GetService(typeof(object));
                }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 44)
            .WithArguments("IServiceProvider", "Provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenContainingTypeIsStaticClass()
    {
        var source = Usings + """
            public static class AppServices
            {
                public static IServiceProvider Provider { get; set; }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 36, 5, 44)
            .WithArguments("IServiceProvider", "Provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenStaticFieldIsUsedFromStaticLocalFunction()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider;

                public void DoWork()
                {
                    static void Helper() => _provider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 46)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenMultiVariableFieldDeclaration()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider, _other;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 46)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenContainingTypeIsPartial()
    {
        var source = Usings + """
            public partial class MyClass
            {
                private static IServiceProvider _provider;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 37, 5, 46)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI006_RemoveStatic");
    }

    [Fact]
    public async Task CodeFix_Offered_WhenFieldIsOnlyUsedFromInstanceMethods()
    {
        var source = Usings + """
            public class MyClass
            {
                private static IServiceProvider _provider;

                public object? Resolve()
                {
                    return _provider?.GetService(typeof(object));
                }
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                private IServiceProvider _provider;

                public object? Resolve()
                {
                    return _provider?.GetService(typeof(object));
                }
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
    public async Task CodeFix_Offered_ForImplicitlyPrivateField()
    {
        var source = Usings + """
            public class MyClass
            {
                static IServiceProvider _provider;
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                IServiceProvider _provider;
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 29, 5, 38)
            .WithArguments("IServiceProvider", "_provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_Offered_ForImplicitlyPrivateProperty()
    {
        var source = Usings + """
            public class MyClass
            {
                static IServiceProvider Provider { get; set; }
            }
            """;

        var fixedSource = Usings + """
            public class MyClass
            {
                IServiceProvider Provider { get; set; }
            }
            """;

        var expected = CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.StaticProviderCache)
            .WithSpan(5, 29, 5, 37)
            .WithArguments("IServiceProvider", "Provider");

        await CodeFixVerifier<DI006_StaticProviderCacheAnalyzer, DI006_StaticProviderCacheCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
