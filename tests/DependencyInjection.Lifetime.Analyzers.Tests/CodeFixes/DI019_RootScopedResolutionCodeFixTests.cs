using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI019_RootScopedResolutionCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public void GetFixAllProvider_DisabledForScopeWrappingFix()
    {
        var provider = new DI019_RootScopedResolutionCodeFixProvider();

        Assert.Null(provider.GetFixAllProvider());
    }

    [Fact]
    public async Task RootResolution_WrapsInScope()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = [|provider.GetRequiredService<IScoped>()|];
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IScoped>();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource);
    }

    [Fact]
    public async Task RootResolution_InAsyncMethod_WrapsInAsyncScope()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public async Task RunAsync(IServiceCollection services)
                {
                    await Task.CompletedTask;
                    var provider = services.BuildServiceProvider();
                    var service = [|provider.GetRequiredService<IScoped>()|];
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var fixedSource = Usings + """
            using System.Threading.Tasks;

            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public async Task RunAsync(IServiceCollection services)
                {
                    await Task.CompletedTask;
                    var provider = services.BuildServiceProvider();
                    await using var scope = provider.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IScoped>();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource);
    }

    [Fact]
    public async Task RootResolution_InAsyncMethodWithoutCreateAsyncScope_NoCodeFix()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public interface IServiceScope : IDisposable
                {
                    IServiceProvider ServiceProvider { get; }
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
                        where TImplementation : TService => services;
                }

                public static class ServiceCollectionContainerBuilderExtensions
                {
                    public static IServiceProvider BuildServiceProvider(this IServiceCollection services) => throw null;
                }

                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this IServiceProvider provider) => throw null;
                    public static IServiceScope CreateScope(this IServiceProvider provider) => throw null;
                }
            }

            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }
            public class ServiceCollection : IServiceCollection { }

            public class Program
            {
                public async Task RunAsync(IServiceCollection services)
                {
                    await Task.CompletedTask;
                    var provider = services.BuildServiceProvider();
                    var service = provider.GetRequiredService<IScoped>();
                    service.DoWork();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyCodeFixNotOfferedWithReferencesAsync(
                source,
                expected,
                ReferenceAssemblies.Net.Net60,
                "DI019_FixTitle_WrapInScope");
    }

    [Fact]
    public async Task RootResolution_WhenLaterLocalUsesScopeName_WrapsWithNonCollidingName()
    {
        var source = Usings + """
            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = [|provider.GetRequiredService<IScoped>()|];
                    service.DoWork();
                    var scope = "already used";
                    Console.WriteLine(scope);
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    using var scope1 = provider.CreateScope();
                    var service = scope1.ServiceProvider.GetRequiredService<IScoped>();
                    service.DoWork();
                    var scope = "already used";
                    Console.WriteLine(scope);
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource);
    }

    [Fact]
    public async Task RootResolution_NullConditional_NoCodeFix()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = provider?{|#0:.GetRequiredService<IScoped>()|};
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_EscapesViaReturn_NoCodeFix()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public IScoped Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    return {|#0:provider.GetRequiredService<IScoped>()|};
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_EscapesViaAssignmentToField_NoCodeFix()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                private IScoped _field;

                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = {|#0:provider.GetRequiredService<IScoped>()|};
                    _field = service;
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_LocalCapturedByReturnedLambda_NoCodeFix()
    {
        var source = Usings + """
            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }

            public class Program
            {
                public Action Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = {|#0:provider.GetRequiredService<IScoped>()|};
                    return () => service.DoWork();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_LocalEscapesViaReceiverCallResult_NoCodeFix()
    {
        var source = Usings + """
            using System.Collections.Generic;
            using System.Linq;

            public interface IScoped { IEnumerable<int> Values { get; } }
            public class Scoped : IScoped { public IEnumerable<int> Values => new[] { 1 }; }

            public class Program
            {
                public List<int> Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = {|#0:provider.GetRequiredService<IScoped>()|};
                    return service.Values.ToList();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_LocalEscapesViaLaterArgument_NoCodeFix()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                private readonly List<IScoped> _cache = new();

                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = {|#0:provider.GetRequiredService<IScoped>()|};
                    _cache.Add(service);
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_LocalUsedOnlyAsReceiver_WrapsInScope()
    {
        var source = Usings + """
            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = [|provider.GetRequiredService<IScoped>()|];
                    service.DoWork();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScoped { void DoWork(); }
            public class Scoped : IScoped { public void DoWork() { } }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IScoped>();
                    service.DoWork();
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource);
    }

    [Fact]
    public async Task RootResolution_ExpressionStatementAssignsToField_NoCodeFix()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                private IScoped _field;

                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    _field = {|#0:provider.GetRequiredService<IScoped>()|};
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_ExpressionStatementPassesResultAsArgument_NoCodeFix()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                private readonly List<IScoped> _cache = new();

                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    _cache.Add({|#0:provider.GetRequiredService<IScoped>()|});
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
            .WithLocation(0)
            .WithArguments("IScoped", "IScoped");

        await CodeFixVerifier<DI019_RootScopedResolutionAnalyzer, DI019_RootScopedResolutionCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }

    [Fact]
    public async Task RootResolution_StaticExtensionForm_NoDiagnosticSoNoBrokenFix()
    {
        var source = Usings + """
            public interface IScoped { }
            public class Scoped : IScoped { }

            public class Program
            {
                public void Run(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    var service = ServiceProviderServiceExtensions.GetRequiredService<IScoped>(provider);
                }

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScoped, Scoped>();
                }
            }
            """;

        // In the explicit static-extension form the member-access receiver is the declaring type
        // (`ServiceProviderServiceExtensions`), not a provider instance, so DI019 does not report
        // here. The fix can therefore never rewrite it into the uncompilable
        // `ServiceProviderServiceExtensions.CreateScope()`. The code fix also guards type receivers
        // directly as defense in depth.
        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
