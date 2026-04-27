using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI003_CaptiveDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
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
            }
        }

        """;

    private const string HostedServiceStubs = """
        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService
            {
                System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
                System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
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

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task SingletonCapturingScoped_ViaConstructor_ReportsDiagnostic()
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

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingTransient_ViaConstructor_ReportsDiagnostic()
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

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "transient", "ITransientService"));
    }

    [Fact]
    public async Task ScopedCapturingTransient_ViaConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly ITransientService _transient;
                public ScopedService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingMultipleScopedDependencies_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IScopedService1 { }
            public class ScopedService1 : IScopedService1 { }

            public interface IScopedService2 { }
            public class ScopedService2 : IScopedService2 { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService1 scoped1, IScopedService2 scoped2) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService1, ScopedService1>();
                    services.AddScoped<IScopedService2, ScopedService2>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService1"),
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService2"));
    }

    [Fact]
    public async Task SingletonCapturingScopedEnumerable_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
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

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(17, 9, 17, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingEnumerableWithEarlierScopedElement_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class ScopedDependency : IDependency { }
            public class SingletonDependency : IDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, ScopedDependency>();
                    services.AddSingleton<IDependency, SingletonDependency>();
                    {|#0:services.AddSingleton<ISingletonService, SingletonService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "IDependency"));
    }

    [Fact]
    public async Task SingletonCapturingEnumerableWithOnlySingletonElements_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class SingletonDependency1 : IDependency { }
            public class SingletonDependency2 : IDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, SingletonDependency1>();
                    services.AddSingleton<IDependency, SingletonDependency2>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedCapturingEnumerableWithScopedElement_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class ScopedDependency : IDependency { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                public ScopedService(System.Collections.Generic.IEnumerable<IDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, ScopedDependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingOptionsSnapshot_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton<ISingletonService, SingletonService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task SingletonCapturingOptionsSnapshot_ViaFactory_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService({|#0:sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>>()|}));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("ISingletonService", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task SingletonCapturingOptionsOrMonitor_NoDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(
                    Microsoft.Extensions.Options.IOptions<MyOptions> options,
                    Microsoft.Extensions.Options.IOptionsMonitor<MyOptions> monitor) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingAddDbContextContext_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext
            {
                public MyDbContext(DbContextOptions<MyDbContext> options) { }
            }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(MyDbContext dbContext) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    {|#0:services.AddSingleton<ISingletonService, SingletonService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "MyDbContext"));
    }

    [Fact]
    public async Task SingletonCapturingAddDbContextOptions_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(DbContextOptions<MyDbContext> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    {|#0:services.AddSingleton<ISingletonService, SingletonService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "DbContextOptions"));
    }

    [Fact]
    public async Task SingletonCapturingScopedRepositoryAndUnitOfWork_ReportsDiagnostics()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public interface IRepository<T> { }
            public sealed class EfRepository<T> : IRepository<T>
            {
                public EfRepository(MyDbContext dbContext) { }
            }

            public interface IUnitOfWork { }
            public sealed class EfUnitOfWork : IUnitOfWork
            {
                public EfUnitOfWork(MyDbContext dbContext) { }
            }

            public interface ISingletonService { }
            public sealed class SingletonService : ISingletonService
            {
                public SingletonService(IRepository<string> repository, IUnitOfWork unitOfWork) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
                    services.AddScoped<IUnitOfWork, EfUnitOfWork>();
                    {|#0:services.AddSingleton<ISingletonService, SingletonService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "IRepository"),
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "IUnitOfWork"));
    }

    [Fact]
    public async Task SingletonCapturingDbContextFactory_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IDbContextFactory<MyDbContext> dbContextFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HostedServiceResolvingScopedProcessorFromScope_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.Hosting;

            """ + EfCoreStubs + HostedServiceStubs + """
            public class MyDbContext : DbContext { }

            public interface IProcessor
            {
                System.Threading.Tasks.Task RunAsync(System.Threading.CancellationToken cancellationToken);
            }

            public sealed class Processor : IProcessor
            {
                public Processor(MyDbContext dbContext) { }
                public System.Threading.Tasks.Task RunAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class ProcessorHostedService : IHostedService
            {
                private readonly System.IServiceProvider _serviceProvider;

                public ProcessorHostedService(System.IServiceProvider serviceProvider)
                {
                    _serviceProvider = serviceProvider;
                }

                public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IProcessor>();
                    return processor.RunAsync(cancellationToken);
                }

                public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    services.AddScoped<IProcessor, Processor>();
                    services.AddHostedService<ProcessorHostedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingAddDbContextContext_WithSingletonLifetime_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.EntityFrameworkCore;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(MyDbContext dbContext) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>(ServiceLifetime.Singleton, ServiceLifetime.Singleton);
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonWithUnresolvableGreedyConstructor_AndCaptiveFallbackConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IUnregisteredDependency missing, ISingletonDependency singleton) { }

                public SingletonService(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(23, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task EarlierDuplicateRegistration_IsStillAnalyzedForCaptiveDependency()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }

            public interface IMyService { }
            public class CaptiveService : IMyService
            {
                public CaptiveService(IScopedDependency scoped) { }
            }

            public class SafeService : IMyService
            {
                public SafeService(ISingletonDependency singleton) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddSingleton<IMyService, CaptiveService>();
                    services.AddSingleton<IMyService, SafeService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(26, 9)
                .WithArguments("CaptiveService", "scoped", "IScopedDependency"));
    }

    #endregion

    #region Should Not Report Diagnostic (False Positives)

    [Fact]
    public async Task SingletonCapturingSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IDependency _dependency;
                public SingletonService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedCapturingScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly IDependency _dependency;
                public ScopedService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedCapturingSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly IDependency _dependency;
                public ScopedService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientCapturingAnything_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ITransientService { }
            public class TransientService : ITransientService
            {
                private readonly IScopedDependency _dependency;
                public TransientService(IScopedDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddTransient<ITransientService, TransientService>();
                }
            }
            """;

        // Transient can capture anything - it's created fresh each time anyway
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceWithUnknownDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IUnregisteredDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    // IUnregisteredDependency is not registered - don't report
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        // Unknown dependencies should not trigger false positives
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Factory Registration Tests

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_ReportsDiagnostic()
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
                    services.AddSingleton<ISingletonService>(sp => new SingletonService(sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(17, 77, 17, 116)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_NonGenericGetService_ReportsDiagnostic()
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
                    services.AddSingleton<ISingletonService>(sp => new SingletonService((IScopedService)sp.GetService(typeof(IScopedService))!));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 93)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_NonGenericGetRequiredService_ReportsDiagnostic()
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
                    services.AddSingleton<ISingletonService>(sp => new SingletonService((IScopedService)sp.GetRequiredService(typeof(IScopedService))));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 93)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScopedEnumerable_ViaFactoryGetServices_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(sp => new SingletonService(sp.GetServices<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(17, 77, 17, 109)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingEnumerableWithEarlierScopedElement_ViaFactoryGetServices_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class ScopedDependency : IDependency { }
            public class SingletonDependency : IDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, ScopedDependency>();
                    services.AddSingleton<IDependency, SingletonDependency>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService({|#0:sp.GetServices<IDependency>()|}));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("ISingletonService", "scoped", "IDependency"));
    }

    [Fact]
    public async Task SingletonFactoryCallingUnrelatedGetServices_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface IRepository
            {
                System.Collections.Generic.IEnumerable<T> GetServices<T>();
            }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var repository = new Repository();

                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(_ => new SingletonService(repository.GetServices<IScopedService>()));
                }
            }

            public class Repository : IRepository
            {
                public System.Collections.Generic.IEnumerable<T> GetServices<T>() => System.Array.Empty<T>();
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_WithActivatorUtilitiesAttribute_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                [ActivatorUtilitiesConstructor]
                public SingletonService(ISingletonDependency singleton) { }

                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(24, 40)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonWithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                [ActivatorUtilitiesConstructor]
                public SingletonService(ISingletonDependency dep) { }

                public SingletonService(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonWithActivatorUtilitiesConstructor_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(ISingletonDependency dep) { }

                [ActivatorUtilitiesConstructor]
                public SingletonService(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(23, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaMethodGroupFactory_ReportsDiagnostic()
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

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(22, 37)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaLocalFunctionFactory_ReportsDiagnostic()
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

                    ISingletonService CreateSingletonService(IServiceProvider sp)
                    {
                        return new SingletonService(sp.GetRequiredService<IScopedService>());
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(21, 41)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingKeyedScoped_ViaFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => throw new System.NotImplementedException();
                }
            }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetRequiredKeyedService<IScopedService>("green")));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(31, 40)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task KeyedSingletonCapturingInheritedKeyedScoped_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class FromKeyedServicesAttribute : System.Attribute
                {
                    public FromKeyedServicesAttribute() { }
                    public FromKeyedServicesAttribute(object? serviceKey) { }
                }

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
                public SingletonService([FromKeyedServices] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    {|#0:services.AddKeyedSingleton<ISingletonService, SingletonService>("green")|};
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task KeyedSingletonFactoryUsingInheritedKey_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddKeyedSingleton<TService>(this IServiceCollection services, object? serviceKey, System.Func<System.IServiceProvider, object?, TService> implementationFactory)
                        where TService : class
                        => services;

                    public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => throw new System.NotImplementedException();
                }
            }

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
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddKeyedSingleton<ISingletonService>(
                        "green",
                        (sp, key) => new SingletonService({|#0:sp.GetRequiredKeyedService<IScopedService>(key)|}));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingKeyedScopedEnumerable_ViaFactoryGetKeyedServices_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(this IServiceProvider provider, object? serviceKey) => throw new System.NotImplementedException();
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService({|#0:sp.GetKeyedServices<IScopedService>("green")|}));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(0)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonFactoryGetKeyedServices_WithDynamicKey_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(this IServiceProvider provider, object? serviceKey) => throw new System.NotImplementedException();
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                private object? GetKey() => "green";

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetKeyedServices<IScopedService>(GetKey())));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonFactoryCallingUnrelatedGetKeyedServices_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface IRepository
            {
                System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey);
            }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(System.Collections.Generic.IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var repository = new Repository();

                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(_ => new SingletonService(repository.GetKeyedServices<IScopedService>("green")));
                }
            }

            public class Repository : IRepository
            {
                public System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey) => System.Array.Empty<T>();
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonFactory_WithActivatorUtilitiesCreateInstance_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ActivatorUtilities
                {
                    public static T CreateInstance<T>(IServiceProvider provider) => throw new System.NotImplementedException();
                }
            }

            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService>(
                        sp => ActivatorUtilities.CreateInstance<SingletonService>(sp));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(26, 19)
                .WithArguments("ISingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonFactory_WithOpenGenericScopedRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T> { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IRepository<string> repository) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                    services.AddSingleton<ISingletonService>(sp => new SingletonService(sp.GetRequiredService<IRepository<string>>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 77)
                .WithArguments("ISingletonService", "scoped", "IRepository"));
    }

    [Fact]
    public async Task SingletonWithMultipleCaptiveConstructorsForSameDependency_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency scoped) { }
                public SingletonService(IScopedDependency scoped, int unused = 0) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(18, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonFactoryMethodGroup_WithNoServiceResolution_NoDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService>(CreateSingletonService);
                }

                private static ISingletonService CreateSingletonService(IServiceProvider sp)
                {
                    return new SingletonService();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonFactory_WithActivatorUtilitiesCreateInstanceAndExplicitArguments_NoDiagnostic()
    {
        var source = Usings + """
            public static class ActivatorUtilities
            {
                public static T CreateInstance<T>(IServiceProvider provider, params object[] arguments) => throw new System.NotImplementedException();
            }

            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency dependency, object marker) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService>(
                        sp => ActivatorUtilities.CreateInstance<SingletonService>(sp, new object()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region TryAddSingleton Coverage

    [Fact]
    public async Task TryAddSingletonCapturingScoped_ViaConstructor_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection TryAddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TService : class where TImplementation : class, TService => services;
                }
            }

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
                    services.TryAddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(27, 9, 27, 72)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task TryAddSingletonCapturingSingleton_ViaConstructor_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection TryAddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TService : class where TImplementation : class, TService => services;
                }
            }

            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(ISingletonDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.TryAddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonImplementationInstance_WithScopedConstructorDependency_NoDiagnostic()
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
                    services.AddSingleton(typeof(ISingletonService), new SingletonService(new ScopedService()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorSingletonImplementationInstance_WithScopedConstructorDependency_NoDiagnostic()
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
                    services.Add(ServiceDescriptor.Singleton(
                        typeof(ISingletonService),
                        new SingletonService(new ScopedService())));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Open Generic Constructor Coverage

    [Fact]
    public async Task OpenGenericSingletonConstructor_WithScopedParameter_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        // DI009 handles open generic captive dependencies, not DI003
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Dedup Behavior

    [Fact]
    public async Task FactoryResolvingSameScopedDependencyTwice_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService s1, IScopedService s2) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(
                            sp.GetRequiredService<IScopedService>(),
                            sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        // One diagnostic per unique captured dependency per registration
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(19, 17, 19, 56)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    #endregion
}
