using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI019_RootScopedResolutionAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string OptionsStubs = """
        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { }
            public interface IOptionsSnapshot<T> { }
            public interface IOptionsMonitor<T> { }
        }

        """;

    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbContextOptions<TContext> where TContext : DbContext { }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class EntityFrameworkServiceCollectionExtensions
            {
                public static IServiceCollection AddDbContext<TContext>(
                    this IServiceCollection services,
                    ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                    ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
            }
        }

        """;

    [Fact]
    public async Task AppServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public sealed class FakeApp
            {
                public IServiceProvider Services { get; init; } = null!;
            }

            public class Startup
            {
                public void Configure(IServiceCollection services, FakeApp app)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:app.Services.GetRequiredService<IScopedService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task HostServicesResolvingScopedWithTypeof_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public sealed class FakeHost
            {
                public IServiceProvider Services { get; init; } = null!;
            }

            public class Startup
            {
                public void Configure(IServiceCollection services, FakeHost host)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:host.Services.GetService(typeof(IScopedService))|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:services.BuildServiceProvider().GetRequiredService<IScopedService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingOptionsSnapshot_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IOptionsSnapshot<MyOptions>", "IOptionsSnapshot<MyOptions>"));
    }

    [Fact]
    public async Task ScopedProviderResolvingOptionsSnapshot_NoDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingOptionsOrMonitor_NoDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MyOptions>>();
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MyOptions>>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextServices_ReportsDiagnostics()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                    {|#1:provider.GetRequiredService<DbContextOptions<MyDbContext>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"),
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(1)
                .WithArguments("DbContextOptions<MyDbContext>", "DbContextOptions<MyDbContext>"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingSingletonAddDbContextServices_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>(ServiceLifetime.Singleton, ServiceLifetime.Singleton);
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<MyDbContext>();
                    provider.GetRequiredService<DbContextOptions<MyDbContext>>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalRootProviderResolvingTransientWithScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface IJobRunner { }
            public class JobRunner : IJobRunner
            {
                public JobRunner(IScopedService scoped) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddTransient<IJobRunner, JobRunner>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<IJobRunner>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IJobRunner", "IScopedService"));
    }

    [Fact]
    public async Task SingletonImplementationResolvingScopedFromInjectedProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IServiceProvider _provider;

                public SingletonService(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IScopedService Resolve() =>
                    {|#0:_provider.GetRequiredService<IScopedService>()|};
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task HostedServiceResolvingScopedFromInjectedProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionHostedServiceExtensions
                {
                    public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services)
                        where THostedService : class, Microsoft.Extensions.Hosting.IHostedService => services;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly IServiceProvider _provider;

                public Worker(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task StartAsync(CancellationToken cancellationToken)
                {
                    {|#0:_provider.GetRequiredService<IScopedService>()|};
                    return Task.CompletedTask;
                }

                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddHostedService<Worker>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task RootGetServicesWithScopedRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetServices<IScopedService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task RootKeyedResolutionWithScopedRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("blue");
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredKeyedService<IScopedService>("blue")|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.ReferenceAssembliesWithLatestDi,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task ScopedProviderResolvingScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    using var root = services.BuildServiceProvider();
                    using var scope = root.CreateScope();
                    var scopedProvider = scope.ServiceProvider;
                    scopedProvider.GetRequiredService<IScopedService>();
                    scope.ServiceProvider.GetRequiredService<IScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AsyncScopedProviderResolvingScoped_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public async Task ConfigureAsync(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    await using var root = services.BuildServiceProvider();
                    await using var scope = root.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<IScopedService>();
                    await Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderAlias_ReassignedFromRootToScoped_ClassifiesCallsBySourceOrder()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    using var root = services.BuildServiceProvider();
                    IServiceProvider provider = root;
                    {|#0:provider.GetRequiredService<IScopedService>()|};

                    using var scope = root.CreateScope();
                    provider = scope.ServiceProvider;
                    provider.GetRequiredService<IScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task ProviderAlias_ReassignedFromScopedToRoot_ClassifiesCallsBySourceOrder()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    using var root = services.BuildServiceProvider();
                    using var scope = root.CreateScope();
                    IServiceProvider provider = scope.ServiceProvider;
                    provider.GetRequiredService<IScopedService>();

                    provider = root;
                    {|#0:provider.GetRequiredService<IScopedService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task HttpContextRequestServicesResolvingScoped_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Http
            {
                public sealed class HttpContext
                {
                    public IServiceProvider RequestServices { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Endpoint
            {
                public void Invoke(IServiceCollection services, Microsoft.AspNetCore.Http.HttpContext httpContext)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    httpContext.RequestServices.GetRequiredService<IScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryRegistrationResolvingScoped_DoesNotDuplicateDi003()
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
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DynamicServiceType_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Type serviceType)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService(serviceType);
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
