using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI020_MiddlewareScopedServiceAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.Extensions.DependencyInjection;

        namespace Microsoft.AspNetCore.Http
        {
            public class HttpContext { }
            public interface IMiddleware
            {
                Task InvokeAsync(HttpContext context, RequestDelegate next);
            }
            public delegate Task RequestDelegate(HttpContext context);
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public interface IApplicationBuilder
            {
                IApplicationBuilder UseMiddleware<TMiddleware>(params object[] args);
                IApplicationBuilder UseMiddleware(Type middleware, params object[] args);
            }
        }

        """;

    [Fact]
    public async Task Middleware_WithScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
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

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_NonGenericTypeOfRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
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

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware(typeof(MyMiddleware));
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_NonGenericTypeOfRegistration_ExplicitArgumentFillsScopedParameter_NoDiagnostic()
    {
        // The scoped-looking parameter is satisfied by the explicit UseMiddleware argument, not
        // resolved from the container at pipeline-build time.
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService scoped)
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

                public void Configure(IApplicationBuilder app, IScopedService preBuilt)
                {
                    app.UseMiddleware(typeof(MyMiddleware), preBuilt);
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_KeyedScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
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
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class where TImplementation : class, TService => services;

                    public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class where TImplementation : class, TService => services;
                }
            }
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(
                    RequestDelegate next,
                    [FromKeyedServices("tenant")] IScopedService [|scoped|])
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
                    services.AddKeyedScoped<IScopedService, ScopedService>("tenant");
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_KeyedDependency_DifferentKeyRegisteredSingleton_NoDiagnostic()
    {
        // The keyed lookup must match the parameter's key: the scoped registration uses another
        // key, and the matching key is a singleton.
        var source = Usings + """
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
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class where TImplementation : class, TService => services;

                    public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class where TImplementation : class, TService => services;
                }
            }
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(
                    RequestDelegate next,
                    [FromKeyedServices("global")] IScopedService scoped)
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
                    services.AddKeyedScoped<IScopedService, ScopedService>("tenant");
                    services.AddKeyedSingleton<IScopedService, ScopedService>("global");
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_RegisteredOnEndpointRouteBuilder_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Routing
            {
                public interface IEndpointRouteBuilder
                {
                    IEndpointRouteBuilder UseMiddleware<TMiddleware>(params object[] args);
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
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

                public void Configure(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
                {
                    endpoints.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_ConditionalAccessRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
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

                public void Configure(IApplicationBuilder? app)
                {
                    app?.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_ExtensionMethodRegistration_ReportsDiagnostic()
    {
        // The real SDK ships UseMiddleware as an extension method — the receiver type comes from
        // the reduced method's first parameter. Self-contained stubs: the builder interface
        // declares no instance UseMiddleware, so the extension is what binds.
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
                public delegate Task RequestDelegate(HttpContext context);
            }

            namespace Microsoft.AspNetCore.Builder
            {
                public interface IApplicationBuilder { }

                public static class UseMiddlewareExtensions
                {
                    public static IApplicationBuilder UseMiddleware<TMiddleware>(this IApplicationBuilder app, params object[] args) => app;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
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

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithInheritedInvokeAndScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public abstract class MiddlewareBase
            {
                protected readonly RequestDelegate Next;

                protected MiddlewareBase(RequestDelegate next)
                {
                    Next = next;
                }

                public Task InvokeAsync(HttpContext context) => Next(context);
            }

            public class MyMiddleware : MiddlewareBase
            {
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|])
                    : base(next)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithTransitiveScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface IRepository { }
            public class Repository : IRepository
            {
                public Repository(IScopedService scoped) { }
            }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, IRepository [|repo|])
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddTransient<IRepository, Repository>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithEnumerableScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, System.Collections.Generic.IEnumerable<IScopedService> [|services|])
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithTransientNonScopedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IOther { }
            public class Other : IOther { }
            public interface IRepository { }
            public class Repository : IRepository
            {
                public Repository(IOther other) { }
            }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, IRepository repo)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IOther, Other>();
                    services.AddTransient<IRepository, Repository>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithScopedInvokeDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next)
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

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareShapedScopedService_NotRegisteredWithUseMiddleware_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MiddlewareShapedService
            {
                private readonly IScopedService _scoped;

                public MiddlewareShapedService(IScopedService scoped)
                {
                    _scoped = scoped;
                }

                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped<MiddlewareShapedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithScopedRequestDelegateConstructorParameter_NoDiagnostic()
    {
        var source = Usings + """
            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<RequestDelegate>(_ => context => Task.CompletedTask);
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithActivatorUtilitiesConstructor_DoesNotAnalyzeUnselectedConstructor()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                [ActivatorUtilitiesConstructor]
                public MyMiddleware(RequestDelegate next)
                {
                    _next = next;
                }

                public MyMiddleware(RequestDelegate next, IScopedService scoped)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithUnresolvableGreedyScopedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next)
                {
                    _next = next;
                }

                public MyMiddleware(RequestDelegate next, IScopedService scoped, string name)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithUseMiddlewareArgumentAndScopedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, string name, IScopedService [|scoped|])
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>("name");
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithDifferentlyTypedUseMiddlewareArguments_ReportsScopedSelectingCall()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, string name)
                {
                    _next = next;
                }

                public MyMiddleware(RequestDelegate next, int id, IScopedService [|scoped|])
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>("name");
                    app.UseMiddleware<MyMiddleware>(1);
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithNoUseMiddlewareArgumentAndUnresolvableConstructorParameter_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, string name, IScopedService scoped)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithArgumentBoundToOptionalParameterAndScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, string name = "x", IScopedService [|scoped|] = null)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>("name");
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithArgumentConsumingNonScopedOverload_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next, string name)
                {
                    _next = next;
                }

                public MyMiddleware(RequestDelegate next, IScopedService scoped)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>("name");
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Middleware_WithResolvableGreedyScopedConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }
            public interface IOtherService { }
            public class OtherService : IOtherService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;

                public MyMiddleware(RequestDelegate next)
                {
                    _next = next;
                }

                public MyMiddleware(RequestDelegate next, IScopedService [|scoped|], IOtherService other)
                {
                    _next = next;
                }

                public Task InvokeAsync(HttpContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<IOtherService, OtherService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryBasedMiddleware_WithScopedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
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

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyFactoryMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonAspNetCoreMiddlewareBuilder_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            // Custom builder with matching name but incorrect namespace
            namespace MyCustomNamespace
            {
                public interface IApplicationBuilder
                {
                    IApplicationBuilder UseMiddleware<TMiddleware>(params object[] args);
                }
                
                public class CustomContext { }
                public delegate Task CustomDelegate(CustomContext context);
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly MyCustomNamespace.CustomDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(MyCustomNamespace.CustomDelegate next, IScopedService scoped)
                {
                    _next = next;
                    _scoped = scoped;
                }

                public Task InvokeAsync(MyCustomNamespace.CustomContext context) => _next(context);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(MyCustomNamespace.IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ValueTaskReturningMiddleware_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly IScopedService _scoped;

                public MyMiddleware(RequestDelegate next, IScopedService scoped)
                {
                    _next = next;
                    _scoped = scoped;
                }

                // Returns ValueTask instead of Task, so it is not a conventional middleware
                public ValueTask InvokeAsync(HttpContext context) => new ValueTask();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                }

                public void Configure(IApplicationBuilder app)
                {
                    app.UseMiddleware<MyMiddleware>();
                }
            }
            """;

        await AnalyzerVerifier<DI020_MiddlewareScopedServiceAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
