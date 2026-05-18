// Local stubs for ASP.NET Core types so SampleApp does not need to reference the
// AspNetCore.App framework. The analyzer matches by namespace + type name, so
// stubs in the Microsoft.AspNetCore.Http namespace exercise the real detection
// path. Mirrors the EF Core stubbing pattern used elsewhere in the analyzer
// test suite.
namespace Microsoft.AspNetCore.Http
{
    public class HttpContext { }

    public delegate System.Threading.Tasks.Task RequestDelegate(HttpContext context);

    public interface IMiddleware
    {
        System.Threading.Tasks.Task InvokeAsync(HttpContext context, RequestDelegate next);
    }
}

namespace SampleApp.Diagnostics.DI020
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using SampleApp.Services;

    /// <summary>
    /// DI020: ASP.NET Core convention-based middleware whose constructor captures a
    /// scoped or transient service. Middleware is activated once and held for the
    /// application lifetime, so capturing a per-request service in the constructor
    /// pins it to the effective-singleton lifetime.
    /// </summary>
    public static class MiddlewareCaptiveDependencyExamples
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddScoped<IScopedService, ScopedService>();
            // Convention-based middleware is typically wired via app.UseMiddleware<T>().
            // DI020 does not require an explicit registration to fire.
        }
    }

    // DI020: The constructor captures the per-request IScopedService in a field on the
    // singleton-lifetime middleware instance. The captured reference becomes stale after
    // the request scope it was activated in is disposed.
    public class BadCaptiveMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScopedService _scoped;

        public BadCaptiveMiddleware(RequestDelegate next, IScopedService scoped)
        {
            _next = next;
            _scoped = scoped;
        }

        public Task InvokeAsync(HttpContext context)
        {
            _scoped.DoWork();
            return _next(context);
        }
    }

    // Good pattern: resolve per-request services as parameters of Invoke/InvokeAsync.
    // The ASP.NET Core request pipeline supplies them from HttpContext.RequestServices.
    public class GoodInvokeParameterMiddleware
    {
        private readonly RequestDelegate _next;

        public GoodInvokeParameterMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context, IScopedService scoped)
        {
            scoped.DoWork();
            return _next(context);
        }
    }

    // Factory-based middleware (implements IMiddleware) is activated per-request by
    // IMiddlewareFactory and its lifetime matches its registration. Capturing a scoped
    // dependency in the constructor is safe here, so DI020 does not fire.
    public class GoodFactoryMiddleware : IMiddleware
    {
        private readonly IScopedService _scoped;

        public GoodFactoryMiddleware(IScopedService scoped)
        {
            _scoped = scoped;
        }

        public Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            _scoped.DoWork();
            return next(context);
        }
    }
}
