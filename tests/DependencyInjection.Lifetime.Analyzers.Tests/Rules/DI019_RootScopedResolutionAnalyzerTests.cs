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
            public class DbContextOptionsBuilder { }
            public interface IDbContextFactory<TContext> where TContext : DbContext
            {
                TContext CreateDbContext();
            }
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

                public static IServiceCollection AddDbContext<TContextService, TContextImplementation>(
                    this IServiceCollection services,
                    ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                    ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                    where TContextService : class
                    where TContextImplementation : Microsoft.EntityFrameworkCore.DbContext, TContextService => services;

                public static IServiceCollection AddDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>? optionsAction = null,
                    ServiceLifetime lifetime = ServiceLifetime.Singleton)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddDbContextPool<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddDbContextPool<TContextService, TContextImplementation>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContextService : class
                    where TContextImplementation : Microsoft.EntityFrameworkCore.DbContext, TContextService => services;

                public static IServiceCollection AddPooledDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
            }
        }

        """;

    [Fact]
    public async Task AppServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Builder
            {
                public sealed class WebApplication
                {
                    public IServiceProvider Services { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.Builder.WebApplication app)
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
    public async Task NullableAppServicesSuppressedResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Builder
            {
                public sealed class WebApplication
                {
                    public IServiceProvider? Services { get; init; }
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.Builder.WebApplication app)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:app.Services!.GetRequiredService<IScopedService>()|};
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
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHost
                {
                    IServiceProvider Services { get; }
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.Extensions.Hosting.IHost host)
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
    public async Task GenericHostServicesResolvingScopedThroughHostConstraint_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHost
                {
                    IServiceProvider Services { get; }
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure<THost>(IServiceCollection services, THost host)
                    where THost : Microsoft.Extensions.Hosting.IHost
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:host.Services.GetRequiredService<IScopedService>()|};
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
    public async Task WebApplicationFactoryServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Mvc.Testing
            {
                public class WebApplicationFactory<TEntryPoint>
                    where TEntryPoint : class
                {
                    public IServiceProvider Services { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public sealed class Program { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:factory.Services.GetRequiredService<IScopedService>()|};
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
    public async Task DerivedWebApplicationFactoryServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Mvc.Testing
            {
                public class WebApplicationFactory<TEntryPoint>
                    where TEntryPoint : class
                {
                    public IServiceProvider Services { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public sealed class Program { }
            public sealed class CustomFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> { }

            public class Startup
            {
                public void Configure(IServiceCollection services, CustomFactory factory)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:factory.Services.GetRequiredService<IScopedService>()|};
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
    public async Task GenericFactoryServicesResolvingScopedThroughFactoryConstraint_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Mvc.Testing
            {
                public class WebApplicationFactory<TEntryPoint>
                    where TEntryPoint : class
                {
                    public IServiceProvider Services { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public sealed class Program { }

            public class Startup
            {
                public void Configure<TFactory>(IServiceCollection services, TFactory factory)
                    where TFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:factory.Services.GetRequiredService<IScopedService>()|};
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
    public async Task TestServerServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.TestHost
            {
                public sealed class TestServer
                {
                    public IServiceProvider Services { get; init; } = null!;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.TestHost.TestServer server)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:server.Services.GetRequiredService<IScopedService>()|};
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
    public async Task NullableBuildServiceProviderSuppressedThenResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    IServiceProvider? provider = services.BuildServiceProvider();
                    {|#0:provider!.GetRequiredService<IScopedService>()|};
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
    public async Task BuildServiceProviderResolvingAddDbContextImplementationFromServiceOverload_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<IMyDbContext, MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingSingletonAddDbContextImplementationFromServiceOverload_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<IMyDbContext, MyDbContext>(
                        ServiceLifetime.Singleton,
                        ServiceLifetime.Singleton);
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<MyDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingSingletonImplementationBeforeAddDbContextServiceOverload_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContext<IMyDbContext, MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<MyDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextFactoryContext_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextFactoryAndOptions_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<IDbContextFactory<MyDbContext>>();
                    provider.GetRequiredService<DbContextOptions<MyDbContext>>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingSingletonContextBeforeAddDbContextFactory_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContextFactory<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<MyDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingScopedOptionsBeforeAddDbContextFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<DbContextOptions<MyDbContext>>();
                    services.AddDbContextFactory<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<DbContextOptions<MyDbContext>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("DbContextOptions<MyDbContext>", "DbContextOptions<MyDbContext>"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingScopedFactoryBeforeAddDbContextFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IDbContextFactory<MyDbContext>>();
                    services.AddDbContextFactory<MyDbContext>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<IDbContextFactory<MyDbContext>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IDbContextFactory<MyDbContext>", "IDbContextFactory<MyDbContext>"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextFactoryTransientContext_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>(lifetime: ServiceLifetime.Transient);
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<MyDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextPoolContext_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextPool<MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingAddDbContextPoolImplementationFromServiceOverload_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextPool<IMyDbContext, MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingScopedOptionsBeforeAddDbContextPool_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<DbContextOptions<MyDbContext>>();
                    services.AddDbContextPool<MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<DbContextOptions<MyDbContext>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("DbContextOptions<MyDbContext>", "DbContextOptions<MyDbContext>"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingPooledDbContextFactoryContext_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<MyDbContext>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("MyDbContext", "MyDbContext"));
    }

    [Fact]
    public async Task BuildServiceProviderResolvingPooledDbContextFactoryAndOptions_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    provider.GetRequiredService<IDbContextFactory<MyDbContext>>();
                    provider.GetRequiredService<DbContextOptions<MyDbContext>>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProviderResolvingExistingScopedFactoryBeforePooledDbContextFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IDbContextFactory<MyDbContext>>();
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<IDbContextFactory<MyDbContext>>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments("IDbContextFactory<MyDbContext>", "IDbContextFactory<MyDbContext>"));
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
                .WithArguments("IJobRunner", "IJobRunner -> IScopedService"));
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
    public async Task RootGetRequiredServiceEnumerableWithScopedRegistration_ReportsDiagnostic()
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
                    {|#0:provider.GetRequiredService<IEnumerable<IScopedService>>()|};
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
    public async Task RootGetServiceTypeofEnumerableWithScopedRegistration_ReportsDiagnostic()
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
                    {|#0:provider.GetService(typeof(IEnumerable<IScopedService>))|};
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
    public async Task RootKeyedResolutionWithAnyKeyScopedRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>(KeyedService.AnyKey);
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredKeyedService<IScopedService>("tenant-a")|};
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
    public async Task ApplicationBuilderApplicationServicesResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Builder
            {
                public interface IApplicationBuilder
                {
                    IServiceProvider ApplicationServices { get; }
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.Builder.IApplicationBuilder app)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:app.ApplicationServices.GetRequiredService<IScopedService>()|};
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
    public async Task EndpointRouteBuilderServiceProviderResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Routing
            {
                public interface IEndpointRouteBuilder
                {
                    IServiceProvider ServiceProvider { get; }
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:endpoints.ServiceProvider.GetRequiredService<IScopedService>()|};
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
    public async Task NullConditionalRootProviderResolvingScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    IServiceProvider? provider = services.BuildServiceProvider();
                    provider?.GetService(typeof(IScopedService));
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithSpan(13, 18, 13, 53)
                .WithArguments("IScopedService", "IScopedService"));
    }

    [Fact]
    public async Task UnrelatedServiceProviderPropertyResolvingScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public sealed class ProviderHolder
            {
                public IServiceProvider ServiceProvider { get; init; } = null!;
            }

            public class Startup
            {
                public void Configure(IServiceCollection services, ProviderHolder holder)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    holder.ServiceProvider.GetRequiredService<IScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnrelatedServicesPropertyResolvingScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public sealed class ScopedProviderHolder
            {
                public IServiceProvider Services { get; init; } = null!;
            }

            public class Startup
            {
                public void Configure(IServiceCollection services, ScopedProviderHolder holder)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    holder.Services.GetRequiredService<IScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyNoDiagnosticsAsync(source);
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

    [Fact]
    public async Task DeepTransitiveScopedDependency_ReportsResolutionPath()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface IRepository { }
            public class Repository : IRepository
            {
                public Repository(IScopedService scoped) { }
            }
            public interface IInvoiceBuilder { }
            public class InvoiceBuilder : IInvoiceBuilder
            {
                public InvoiceBuilder(IRepository repository) { }
            }
            public interface IOrderProcessor { }
            public class OrderProcessor : IOrderProcessor
            {
                public OrderProcessor(IInvoiceBuilder builder) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddTransient<IRepository, Repository>();
                    services.AddTransient<IInvoiceBuilder, InvoiceBuilder>();
                    services.AddTransient<IOrderProcessor, OrderProcessor>();
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<IOrderProcessor>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments(
                    "IOrderProcessor",
                    "IOrderProcessor -> IInvoiceBuilder -> IRepository -> IScopedService"));
    }

    [Fact]
    public async Task TransitiveScopedDependencyThroughFactory_ReportsResolutionPath()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface IRepository { }
            public class Repository : IRepository
            {
                public Repository(IScopedService scoped) { }
            }
            public interface IUnitOfWork { }
            public class UnitOfWork : IUnitOfWork
            {
                public UnitOfWork(IRepository repository) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddTransient<IRepository, Repository>();
                    services.AddTransient<IUnitOfWork>(sp =>
                        new UnitOfWork(sp.GetRequiredService<IRepository>()));
                    var provider = services.BuildServiceProvider();
                    {|#0:provider.GetRequiredService<IUnitOfWork>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI019_RootScopedResolutionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootScopedResolution)
                .WithLocation(0)
                .WithArguments(
                    "IUnitOfWork",
                    "IUnitOfWork -> IRepository -> IScopedService"));
    }

    [Fact]
    public async Task DirectScopedResolution_ReportsSingleNodePath()
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
}
