using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI011_ServiceProviderInjectionAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task Constructor_WithIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                private readonly IServiceProvider _provider;
                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(17, 9, 17, 52)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceScopeFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(17, 9, 17, 52)
                .WithArguments("MyService", "IServiceScopeFactory"));
    }

    [Fact]
    public async Task Constructor_WithBothTypes_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider, IServiceScopeFactory scopeFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 52)
                .WithArguments("MyService", "IServiceProvider"),
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 52)
                .WithArguments("MyService", "IServiceScopeFactory"));
    }

    [Fact]
    public async Task Singleton_WithIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 55)
                .WithArguments("MyService", "IServiceProvider"));
    }

    #endregion

    #region Should Not Report Diagnostic (Allowed Cases)

    [Fact]
    public async Task FactoryClass_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyServiceFactory { }
            public class MyServiceFactory : IMyServiceFactory
            {
                private readonly IServiceProvider _provider;
                public MyServiceFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyServiceFactory, MyServiceFactory>();
                }
            }
            """;

        // Factory classes are allowed to inject IServiceProvider
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClassWithFactorySuffix_NoDiagnostic()
    {
        var source = Usings + """
            public interface IOrderFactory { }
            public class OrderFactory : IOrderFactory
            {
                private readonly IServiceProvider _provider;
                public OrderFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IOrderFactory, OrderFactory>();
                }
            }
            """;

        // Factory classes are allowed
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed (has Invoke method)
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeAsyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task InvokeAsync() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed (has InvokeAsync method)
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithOtherDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        // Normal dependencies don't trigger
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredService_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    // MyService is not registered
                }
            }
            """;

        // Unregistered services are not analyzed
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
