using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI020_MiddlewareCaptiveDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string AspNetCoreStubs = """
        namespace Microsoft.AspNetCore.Http
        {
            public class HttpContext { }
            public delegate System.Threading.Tasks.Task RequestDelegate(HttpContext context);
            public interface IMiddleware
            {
                System.Threading.Tasks.Task InvokeAsync(HttpContext context, RequestDelegate next);
            }
        }

        """;

    private const string OptionsStubs = """
        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { }
            public interface IOptionsSnapshot<T> { }
            public interface IOptionsMonitor<T> { }
        }

        """;

    private const string LoggingStubs = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public interface ILogger<T> { }
        }

        """;

    private const string ConfigurationStubs = """
        namespace Microsoft.Extensions.Configuration
        {
            public interface IConfiguration { }
        }

        """;

    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbContextOptionsBuilder { }
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

    #region Should Report Diagnostic

    [Fact]
    public async Task ScopedDependency_InMiddlewareConstructor_ReportsDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, {|#0:IScopedService scoped|})
                {
                    _next = next;
                    _scoped = scoped;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync"));
    }

    [Fact]
    public async Task TransientDependency_InMiddlewareConstructor_ReportsDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:ITransientService transient|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "transient", "ITransientService", "InvokeAsync"));
    }

    [Fact]
    public async Task OptionsSnapshotDependency_InMiddlewareConstructor_ReportsDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + OptionsStubs + """
            public class MyOptions { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> options|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IOptionsSnapshot", "InvokeAsync"));
    }

    [Fact]
    public async Task DbContextDependency_InMiddlewareConstructor_ReportsDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + EfCoreStubs + """
            public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:AppDbContext db|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "AppDbContext", "InvokeAsync"));
    }

    [Fact]
    public async Task EnumerableOfScopedDependency_InMiddlewareConstructor_ReportsDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:IEnumerable<IScopedService> services|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync"));
    }

    [Fact]
    public async Task InvokeAsyncOnly_WithScopedDependency_ReportsDiagnosticWithInvokeAsyncMessage()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:IScopedService scoped|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync"));
    }

    [Fact]
    public async Task InvokeOnly_WithScopedDependency_ReportsDiagnosticWithInvokeMessage()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:IScopedService scoped|}) { }

                public Task Invoke(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "Invoke"));
    }

    [Fact]
    public async Task InvokeAndInvokeAsync_Coexist_ReportsDiagnosticWithInvokeAsyncMessage()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:IScopedService scoped|}) { }

                public Task Invoke(HttpContext context) => Task.CompletedTask;
                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync"));
    }

    [Fact]
    public async Task MultipleScopedConstructorParameters_ReportsOnePerParameter()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedA { }
            public class ScopedA : IScopedA { }
            public interface IScopedB { }
            public class ScopedB : IScopedB { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, {|#0:IScopedA a|}, {|#1:IScopedB b|}) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedA, ScopedA>();
                    services.AddScoped<IScopedB, ScopedB>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedA", "InvokeAsync"),
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(1)
                .WithArguments("MyMiddleware", "scoped", "IScopedB", "InvokeAsync"));
    }

    [Fact]
    public async Task MiddlewareInDifferentFile_StillFlagged()
    {
        var middlewareSource = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;

            namespace TestApp.Middleware
            {
                public class MyMiddleware
                {
                    public MyMiddleware(RequestDelegate next, {|#0:TestApp.Services.IScopedService scoped|}) { }
                    public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
                }
            }
            """;

        var startupSource = AspNetCoreStubs + """
            using Microsoft.Extensions.DependencyInjection;

            namespace TestApp.Services
            {
                public interface IScopedService { }
                public class ScopedService : IScopedService { }
            }

            namespace TestApp
            {
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddScoped<Services.IScopedService, Services.ScopedService>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            new[]
            {
                ("Middleware.cs", middlewareSource),
                ("Startup.cs", startupSource),
            },
            AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
                .WithLocation(0)
                .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task SingletonOnlyDependencies_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface ISingletonService { }
            public class SingletonService : ISingletonService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, ISingletonService singleton) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LoggerDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + LoggingStubs + """
            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, Microsoft.Extensions.Logging.ILogger<MyMiddleware> logger) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IConfigurationDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + ConfigurationStubs + """
            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, Microsoft.Extensions.Configuration.IConfiguration configuration) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IOptionsDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + OptionsStubs + """
            public class MyOptions { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, Microsoft.Extensions.Options.IOptions<MyOptions> options) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IServiceProviderDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, IServiceProvider provider) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IServiceScopeFactoryDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, IServiceScopeFactory factory) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IMiddlewareFactoryClass_WithScopedDependency_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyFactoryMiddleware : IMiddleware
            {
                private readonly IScopedService _scoped;

                public MyFactoryMiddleware(IScopedService scoped)
                {
                    _scoped = scoped;
                }

                public Task InvokeAsync(HttpContext context, RequestDelegate next) => next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped<MyFactoryMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AbstractMiddlewareBaseClass_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public abstract class MyMiddlewareBase
            {
                protected MyMiddlewareBase(RequestDelegate next, IScopedService scoped) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonMiddlewareWithInvokeMethod_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            // Has Invoke(HttpContext) but no RequestDelegate ctor parameter.
            // Not convention-based middleware - should be ignored by DI020.
            public class NotMiddleware
            {
                public NotMiddleware(IScopedService scoped) { }

                public Task Invoke(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareWithPrivateInvoke_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            // Invoke is not public, so this is not convention middleware shape.
            public class NotQuiteMiddleware
            {
                public NotQuiteMiddleware(RequestDelegate next, IScopedService scoped) { }

                private Task Invoke(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareWithScopedDependencyOnInvokeOnly_DoesNotReportDiagnostic()
    {
        var source = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            // The canonical fix shape: per-request injection on InvokeAsync.
            public class GoodMiddleware
            {
                private readonly RequestDelegate _next;

                public GoodMiddleware(RequestDelegate next)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context, IScopedService scoped) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareWithUnregisteredDependency_DoesNotReportDiagnostic()
    {
        // When lifetime is unknown (not registered, not a known framework type), DI003
        // chooses silence over a false positive. DI020 follows the same convention.
        var source = Usings + AspNetCoreStubs + """
            public interface IUnknownService { }

            public class MyMiddleware
            {
                public MyMiddleware(RequestDelegate next, IUnknownService unknown) { }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
