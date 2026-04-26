using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

/// <summary>
/// Tests that verify expected cross-rule diagnostic interactions when multiple
/// analyzers fire on the same source. Each test runs a single analyzer at a time
/// against shared source code to validate the expected multi-rule behavior.
/// </summary>
public class CrossRuleInteractionTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// When IServiceProvider is injected into a constructor AND used to resolve
    /// services, DI011 should fire on the registration and DI007 should fire
    /// on the GetRequiredService call.
    /// </summary>
    [Fact]
    public async Task ServiceLocatorInConstructor_DI007_ReportsOnGetCall()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class BadLocator
            {
                private readonly IMyService _service;

                public BadLocator(IServiceProvider provider)
                {
                    _service = provider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.AddScoped<BadLocator>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(12, 20, 12, 61)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task ServiceLocatorInConstructor_DI011_ReportsOnRegistration()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class BadLocator
            {
                private readonly IMyService _service;

                public BadLocator(IServiceProvider provider)
                {
                    _service = provider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.AddScoped<BadLocator>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(21, 9)
                .WithArguments("BadLocator", "IServiceProvider"));
    }

    [Fact]
    public async Task AddDbContextCapturedBySingleton_DI003_ReportsCaptiveDependency()
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
    public async Task AddDbContextCapturedBySingleton_DI015_DoesNotReportMissingOptions()
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
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    /// <summary>
    /// When a transient IDisposable service is registered twice, DI008 should
    /// fire on the transient IDisposable registration and DI012b should fire
    /// on the duplicate.
    /// </summary>
    [Fact]
    public async Task DisposableTransientWithDuplicate_DI008_ReportsOnTransient()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyDisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyDisposableService>();
                    services.AddTransient<IMyService, MyDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithLocation(13, 9)
                .WithArguments("MyDisposableService", "IDisposable"),
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithLocation(14, 9)
                .WithArguments("MyDisposableService", "IDisposable"));
    }

    [Fact]
    public async Task DisposableTransientWithDuplicate_DI012b_ReportsOnDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyDisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyDisposableService>();
                    services.AddTransient<IMyService, MyDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(14, 9)
                .WithArguments("IMyService", "line 13"));
    }

    /// <summary>
    /// A constructor with 5+ meaningful dependencies (excluding IServiceProvider which
    /// DI010 intentionally excludes to avoid double-penalizing with DI011) plus
    /// IServiceProvider should trigger DI010 for over-injection and DI011 for
    /// provider injection independently.
    /// DI010 threshold is > 4 meaningful deps, and IServiceProvider is excluded from count.
    /// </summary>
    [Fact]
    public async Task OverInjectionWithProviderInjection_DI010_ReportsOverInjection()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public class OverInjected
            {
                public OverInjected(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    IDep5 d5,
                    IServiceProvider provider)
                { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<OverInjected>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(35, 9)
                .WithArguments("OverInjected", "5"));
    }

    [Fact]
    public async Task OverInjectionWithProviderInjection_DI011_ReportsProviderInjection()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public class OverInjected
            {
                public OverInjected(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    IDep5 d5,
                    IServiceProvider provider)
                { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<OverInjected>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(35, 9)
                .WithArguments("OverInjected", "IServiceProvider"));
    }

    /// <summary>
    /// When a singleton captures a scoped dependency that also has a missing
    /// transitive dependency, DI003 should report the captive dependency and
    /// DI015 should report the unresolvable dependency independently.
    /// </summary>
    [Fact]
    public async Task CaptiveWithUnresolvable_DI003_ReportsCaptive()
    {
        var source = Usings + """
            public interface IScopedService { }
            public interface IMissing { }
            public class ScopedService : IScopedService
            {
                public ScopedService(IMissing missing) { }
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
    public async Task CaptiveWithUnresolvable_DI015_ReportsUnresolvable()
    {
        var source = Usings + """
            public interface IScopedService { }
            public interface IMissing { }
            public class ScopedService : IScopedService
            {
                public ScopedService(IMissing missing) { }
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
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        // DI015 reports transitively: both ScopedService (direct) and
        // SingletonService (via ScopedService → IMissing) flag IMissing.
        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithSpan(20, 9, 20, 60)
                .WithArguments("IScopedService", "IMissing"),
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("ISingletonService", "IMissing"));
    }
}
