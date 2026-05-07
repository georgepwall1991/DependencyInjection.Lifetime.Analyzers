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

    [Fact]
    public async Task Constructor_WithUnresolvableGreedyConstructor_AndServiceProviderFallback_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IUnregisteredDependency missing, IDependency dependency) { }

                public MyService(IServiceProvider provider) { }
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

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(20, 9)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIKeyedServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IKeyedServiceProvider { }
            }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IKeyedServiceProvider provider) { }
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
                .WithSpan(18, 9, 18, 52)
                .WithArguments("MyService", "IKeyedServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InNonMiddlewareInvokeClass_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class MyCommand
            {
                private readonly IServiceProvider _provider;

                public MyCommand(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyCommand>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(21, 9)
                .WithArguments("MyCommand", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InGenericTaskInvokeClass_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public sealed class Order { }

            public class MyCommand
            {
                private readonly IServiceProvider _provider;

                public MyCommand(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task<Order> InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) =>
                    Task.FromResult(new Order());
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<MyCommand>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("MyCommand", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithoutFactoryMember_ReportsDiagnostic()
    {
        var source = Usings + """
            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithVoidCreateMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void CreateCache() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithPlainTaskCreateMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task CreateCacheAsync() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    #endregion

    #region Should Not Report Diagnostic (Allowed Cases)

    [Fact]
    public async Task FactoryClass_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IMyServiceFactory
            {
                IMyService CreateService();
            }

            public class MyServiceFactory : IMyServiceFactory
            {
                private readonly IServiceProvider _provider;
                public MyServiceFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IMyService CreateService() => _provider.GetRequiredService<IMyService>();
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
            public sealed class Order { }
            public interface IOrderFactory
            {
                Order CreateOrder();
            }

            public class OrderFactory : IOrderFactory
            {
                private readonly IServiceProvider _provider;
                public OrderFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Order CreateOrder() => _provider.GetRequiredService<Order>();
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
    public async Task FactoryInterface_WithAsyncFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public sealed class Order { }
            public interface IOrderFactory
            {
                Task<Order> CreateOrderAsync();
            }

            public class OrderResolver : IOrderFactory
            {
                private readonly IServiceProvider _provider;
                public OrderResolver(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task<Order> CreateOrderAsync() =>
                    Task.FromResult(_provider.GetRequiredService<Order>());
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IOrderFactory, OrderResolver>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryClass_WithInheritedFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public abstract class FactoryBase
            {
                public IMyService CreateService() => throw new NotImplementedException();
            }

            public class MyServiceFactory : FactoryBase
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
                    services.AddScoped<MyServiceFactory>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke(Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed when they match the ASP.NET Core middleware shape.
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeAsyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed when they match the ASP.NET Core middleware shape.
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HostedService_NoDiagnostic()
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

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly IServiceProvider _provider;

                public Worker(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Worker>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Singleton_WithIServiceScopeFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class ScopedWorker
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public ScopedWorker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ScopedWorker>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InProtectedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                public MyService() { }

                protected MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task EndpointFilterFactory_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Http
            {
                public delegate object EndpointFilterFactoryContext();
                public delegate object EndpointFilterDelegate();

                public interface IEndpointFilterFactory
                {
                    object CreateInstance(IServiceProvider serviceProvider, EndpointFilterFactoryContext context, EndpointFilterDelegate next);
                }
            }

            public class MyFilterFactory : Microsoft.AspNetCore.Http.IEndpointFilterFactory
            {
                private readonly IServiceProvider _provider;

                public MyFilterFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public object CreateInstance(IServiceProvider serviceProvider, Microsoft.AspNetCore.Http.EndpointFilterFactoryContext context, Microsoft.AspNetCore.Http.EndpointFilterDelegate next)
                    => new object();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<MyFilterFactory>();
                }
            }
            """;

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
    public async Task Constructor_WithResolvableCleanConstructor_AndServiceProviderFallback_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IAnotherDependency { }
            public class AnotherDependency : IAnotherDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency, IAnotherDependency anotherDependency) { }

                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IAnotherDependency, AnotherDependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                [ActivatorUtilitiesConstructor]
                public MyService(IDependency dependency) { }

                public MyService(IServiceProvider provider) { }
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

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_OnServiceProviderConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }

                [ActivatorUtilitiesConstructor]
                public MyService(IServiceProvider provider) { }
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

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(20, 9)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task ImplementationInstance_WithIServiceProviderConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class FakeProvider : IServiceProvider
            {
                public object? GetService(Type serviceType) => null;
            }

            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), new MyService(new FakeProvider()));
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorImplementationInstance_WithIServiceScopeFactoryConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class FakeScopeFactory : IServiceScopeFactory
            {
                public IServiceScope CreateScope() => throw new NotImplementedException();
            }

            public class MyService : IMyService
            {
                public MyService(IServiceScopeFactory scopeFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton(
                        typeof(IMyService),
                        new MyService(new FakeScopeFactory())));
                }
            }
            """;

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
