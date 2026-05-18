using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI020_MiddlewareCaptiveDependencyCodeFixTests
{
    private const string Usings = """
        using System;
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

    [Fact]
    public async Task SuppressFix_AddsPragmaAroundConstructor()
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

        var fixedSource = Usings + AspNetCoreStubs + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public class MyMiddleware
            {
            #pragma warning disable DI020
                public MyMiddleware(RequestDelegate next, IScopedService scoped) { }
            #pragma warning restore DI020

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

        var expected = CodeFixVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer, DI020_MiddlewareCaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.MiddlewareCaptiveDependency)
            .WithLocation(0)
            .WithArguments("MyMiddleware", "scoped", "IScopedService", "InvokeAsync");

        await CodeFixVerifier<DI020_MiddlewareCaptiveDependencyAnalyzer, DI020_MiddlewareCaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                DI020_MiddlewareCaptiveDependencyCodeFixProvider.SuppressEquivalenceKey);
    }
}
