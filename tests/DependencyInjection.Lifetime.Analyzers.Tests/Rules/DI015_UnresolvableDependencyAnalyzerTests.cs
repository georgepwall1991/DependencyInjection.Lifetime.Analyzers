using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI015_UnresolvableDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;

        namespace Microsoft.Extensions.DependencyInjection
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class FromKeyedServicesAttribute : Attribute
            {
                public FromKeyedServicesAttribute(object? key) { }
            }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;

                public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => default!;
            }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger<T> { }
            public interface ILoggerFactory { }
        }

        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { }
            public interface IOptionsSnapshot<T> { }
            public interface IOptionsMonitor<T> { }
        }

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task RegisteredService_WithMissingConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(48, 9)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task RegisteredService_WithMissingKeyedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService([FromKeyedServices("blue")] IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("green");
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(50, 9)
                .WithArguments("IMyService", "IMyDependency (key: blue)"));
    }

    [Fact]
    public async Task RegisteredService_WithActivatorUtilitiesConstructorOnMissingDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }

                [ActivatorUtilitiesConstructor]
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(54, 9)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredService<IMissingDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(49, 33)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingNonGenericGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService((IMissingDependency)sp.GetRequiredService(typeof(IMissingDependency))));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(49, 53)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingGetRequiredKeyedServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("green");
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredKeyedService<IMyDependency>("blue")));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(51, 33)
                .WithArguments("IMyService", "IMyDependency (key: blue)"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task RegisteredService_WithRegisteredConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyDependency, MyDependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithOptionalConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency? dependency = null) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithIEnumerableDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IEnumerable<IMyDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithOpenGenericDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T> { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IRepository<string> repository) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithMatchingKeyedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService([FromKeyedServices("blue")] IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("blue");
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithActivatorUtilitiesConstructorOnResolvableDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                [ActivatorUtilitiesConstructor]
                public MyService(IDependency dependency) { }

                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithUnknownDynamicKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                private static object GetKey() => "blue";

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredKeyedService<IMyDependency>(GetKey())));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithGetServiceMissingDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency? dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService((IMissingDependency?)sp.GetService(typeof(IMissingDependency))));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithLoggerAndOptionsDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public class MyOptions { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(ILogger<MyService> logger, IOptions<MyOptions> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
