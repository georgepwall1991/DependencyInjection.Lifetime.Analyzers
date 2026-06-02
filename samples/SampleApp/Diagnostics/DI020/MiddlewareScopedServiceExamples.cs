using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Http
{
    public class HttpContext { }
    public delegate Task RequestDelegate(HttpContext context);
}

namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.AspNetCore.Http;
    public interface IApplicationBuilder
    {
        IApplicationBuilder UseMiddleware<T>(params object[] args);
    }
}

namespace SampleApp.Diagnostics.DI020
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Builder;

    public interface IScopedService { }
    public class ScopedService : IScopedService { }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IScopedService, ScopedService>();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMiddleware<Bad_MiddlewareConstructorCapture>();
        }
    }

    // Rule DI020: Middleware Captures Scoped Service In Constructor
    // Conventional middleware is instantiated once per application lifetime.
    // Injecting scoped services into the constructor captures them for the entire application life.

    public class Bad_MiddlewareConstructorCapture
    {
        private readonly RequestDelegate _next;
        private readonly IScopedService _scoped;

        // [DI020] Middleware 'Bad_MiddlewareConstructorCapture' captures scoped dependency 'IScopedService' in its constructor
        public Bad_MiddlewareConstructorCapture(RequestDelegate next, IScopedService scoped)
        {
            _next = next;
            _scoped = scoped;
        }

        public Task InvokeAsync(HttpContext context)
        {
            return _next(context);
        }
    }

    public class Good_MiddlewareInvokeResolution
    {
        private readonly RequestDelegate _next;

        public Good_MiddlewareInvokeResolution(RequestDelegate next)
        {
            _next = next;
        }

        // Resolve scoped services from Invoke/InvokeAsync parameters instead
        public Task InvokeAsync(HttpContext context, IScopedService scoped)
        {
            return _next(context);
        }
    }
}
