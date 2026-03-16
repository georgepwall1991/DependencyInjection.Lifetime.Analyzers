using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI003 code fix: Captive dependency lifetime mismatch.
/// </summary>
public class DI003_CaptiveDependencyCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_SingletonCapturingScoped_ChangesToScoped()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IScopedService _scoped;
                public SingletonService(IScopedService scoped)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IScopedService _scoped;
                public SingletonService(IScopedService scoped)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_SingletonCapturingTransient_ChangesToScoped()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddScoped<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "transient", "ITransientService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_SingletonCapturingTransient_ChangesToTransient()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddTransient<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "transient", "ITransientService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToTransient");
    }

    [Fact]
    public async Task CodeFix_NonGenericTypeofRegistration_ChangesToScoped()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IScopedService), typeof(ScopedService));
                    services.AddSingleton(typeof(ISingletonService), typeof(SingletonService));
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IScopedService), typeof(ScopedService));
                    services.AddScoped(typeof(ISingletonService), typeof(SingletonService));
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(17, 9, 17, 83)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_KeyedRegistration_ChangesToScoped()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class FromKeyedServicesAttribute : System.Attribute
                {
                    public FromKeyedServicesAttribute(object? key) { }
                }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices("green")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddKeyedSingleton<ISingletonService, SingletonService>("green");
                }
            }
            """;

        var fixedSource = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class FromKeyedServicesAttribute : System.Attribute
                {
                    public FromKeyedServicesAttribute(object? key) { }
                }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices("green")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddKeyedScoped<ISingletonService, SingletonService>("green");
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(42, 9, 42, 81)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_ServiceDescriptorDescribe_ChangesLifetimeArgument()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(typeof(IScopedService), typeof(ScopedService), ServiceLifetime.Scoped));
                    services.Add(ServiceDescriptor.Describe(typeof(ISingletonService), typeof(SingletonService), ServiceLifetime.Singleton));
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(typeof(IScopedService), typeof(ScopedService), ServiceLifetime.Scoped));
                    services.Add(ServiceDescriptor.Describe(typeof(ISingletonService), typeof(SingletonService), ServiceLifetime.Scoped));
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(17, 9, 17, 129)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_ServiceDescriptorSingletonFactory_ChangesMethodName()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Scoped(typeof(IScopedService), typeof(ScopedService)));
                    services.Add(ServiceDescriptor.Singleton(typeof(ISingletonService), typeof(SingletonService)));
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Scoped(typeof(IScopedService), typeof(ScopedService)));
                    services.Add(ServiceDescriptor.Scoped(typeof(ISingletonService), typeof(SingletonService)));
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(17, 9, 17, 103)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_MethodGroupFactoryDiagnostic_NoFixOffered()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(CreateSingletonService);
                }

                private static ISingletonService CreateSingletonService(IServiceProvider sp)
                {
                    return new SingletonService(sp.GetRequiredService<IScopedService>());
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithLocation(22, 37)
            .WithArguments("ISingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI003_ChangeToScoped");
    }
}
