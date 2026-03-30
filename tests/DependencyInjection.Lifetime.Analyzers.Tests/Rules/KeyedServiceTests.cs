using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class KeyedServiceTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        namespace Microsoft.Extensions.DependencyInjection
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public class FromKeyedServicesAttribute : Attribute
            {
                public object Key { get; }
                public FromKeyedServicesAttribute(object key) { Key = key; }
            }
            
            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey) 
                    where TService : class where TImplementation : class, TService => services;

                public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey) 
                    where TService : class where TImplementation : class, TService => services;

                public static IServiceCollection AddKeyedTransient<TService, TImplementation>(this IServiceCollection services, object? serviceKey) 
                    where TService : class where TImplementation : class, TService => services;
                    
                public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => default;
                public static T GetKeyedService<T>(this IServiceProvider provider, object? serviceKey) => default;
            }
        }

        """;

    [Fact]
    public async Task SingletonCapturingKeyedScoped_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices("A")] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>("A");
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(42, 9, 42, 69)
                .WithArguments("SingletonService", "scoped", "IService"));
    }

    [Fact]
    public async Task SingletonCapturingKeyedSingleton_ViaConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices("A")] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IService, Service>("A");
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
    
    [Fact]
    public async Task SingletonCapturingKeyedScoped_WithDifferentKeys_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                // Consumes "B" (which is Singleton), "A" is Scoped
                public SingletonService([FromKeyedServices("B")] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>("A");
                    services.AddKeyedSingleton<IService, Service>("B");
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingKeyedScoped_ViaFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>("A");
                    services.AddSingleton<IService>(sp => sp.GetRequiredKeyedService<IService>("A"));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(36, 47, 36, 88)
                .WithArguments("IService", "scoped", "IService"));
    }
    
    [Fact]
    public async Task SingletonCapturingKeyedScoped_ViaFactory_WithKeyMismatch_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>("A");
                    // Consuming "B" which is unknown, should be ignored
                    services.AddSingleton<IService>(sp => sp.GetRequiredKeyedService<IService>("B"));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NullKey_Separation_From_NonKeyed()
    {
        // Test that AddKeyedSingleton(null) is distinct from AddSingleton()
        // And that [FromKeyedServices(null)] resolves AddKeyedSingleton(null)
        // We set up AddKeyedScoped(null) and AddSingleton().
        // Consuming [FromKeyedServices(null)] should match AddKeyedScoped(null) and warn if captive.
        // Consuming normal Service should match AddSingleton() and be safe.

        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices(null)] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IService, Service>(); // Non-keyed Singleton (Safe)
                    services.AddKeyedScoped<IService, Service>(null); // Keyed-Null Scoped (Captive)
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(43, 9, 43, 69)
                .WithArguments("SingletonService", "scoped", "IService"));
    }

    [Fact]
    public async Task IntKey_CaptiveDependency_Detected()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices(42)] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>(42);
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(42, 9, 42, 69)
                .WithArguments("SingletonService", "scoped", "IService"));
    }

    [Fact]
    public async Task NullKey_CaptiveDependency_ViaFactory_ReportsDiagnostic()
    {
        // Regression test: GetRequiredKeyedService<T>(null) inside a singleton factory
        // must still be analyzed — null is a valid keyed identity, not "unknown key".
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>(null);
                    services.AddSingleton<IService>(sp => sp.GetRequiredKeyedService<IService>(null));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(36, 47, 36, 89)
                .WithArguments("IService", "scoped", "IService"));
    }

    [Fact]
    public async Task EnumKey_CaptiveDependency_Detected()
    {
        var source = Usings + """
            public enum ServiceKey { KeyA, KeyB }
            public interface IService { }
            public class Service : IService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService([FromKeyedServices(ServiceKey.KeyA)] IService service) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IService, Service>(ServiceKey.KeyA);
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(43, 9, 43, 69)
                .WithArguments("SingletonService", "scoped", "IService"));
    }
}
