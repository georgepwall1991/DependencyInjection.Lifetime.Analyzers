using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI007_ServiceLocatorAntiPatternAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task GetRequiredService_InConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IMyService _service;

                public MyClass(IServiceProvider provider)
                {
                    _service = provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(11, 20, 11, 61)
                .WithArguments("IMyService"));
        
    }

    [Fact]
    public async Task GetService_InConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IMyService? _service;

                public MyClass(IServiceProvider provider)
                {
                    _service = provider.GetService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(11, 20, 11, 53)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InRegularMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    var service = _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(16, 23, 16, 65)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InLambdaInsideRegularMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    Action action = () => _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(16, 31, 16, 73)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetServices_PluralMethod_InConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IEnumerable<IMyService> _services;

                public MyClass(IServiceProvider provider)
                {
                    _services = provider.GetServices<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(13, 21, 13, 55)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InPropertyGetter_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IMyService Service => _provider.GetRequiredService<IMyService>();
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(14, 34, 14, 76)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetService_Typeof_InConstructor_ReportsResolvedTypeName()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IMyService _service;

                public MyClass(IServiceProvider provider)
                {
                    _service = (IMyService)provider.GetService(typeof(IMyService))!;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(11, 32, 11, 71)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_Typeof_InRegularMethod_ReportsResolvedTypeName()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    var service = (IMyService)_provider.GetRequiredService(typeof(IMyService));
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(16, 35, 16, 83)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredKeyedService_Typeof_InRegularMethod_ReportsResolvedTypeName()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IKeyedServiceProvider
                {
                    object? GetRequiredKeyedService(System.Type serviceType, object? serviceKey);
                }
            }

            public interface IMyService { }

            public class MyClass
            {
                private readonly IKeyedServiceProvider _provider;

                public MyClass(IKeyedServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    var service = (IMyService)_provider.GetRequiredKeyedService(typeof(IMyService), "primary");
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(24, 35, 24, 99)
                .WithArguments("IMyService"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task GetRequiredService_InFactoryRegistration_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IDependency { }
            public class MyService : IMyService
            {
                public MyService(IDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp =>
                        new MyService(sp.GetRequiredService<IDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InInvokeMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;

                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke()
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InInvokeAsyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;

                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task InvokeAsync()
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InCreateFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyFactory
            {
                private readonly IServiceProvider _provider;

                public MyFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IMyService CreateService()
                {
                    return _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InLambdaInsideCreateMethod_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyFactory
            {
                private readonly IServiceProvider _provider;

                public MyFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Action CreateWork()
                {
                    return () => _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InMethodWithIServiceProviderParameter_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                public IMyService ResolveService(IServiceProvider provider)
                {
                    return provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(9, 16, 9, 57)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task ConstructorInjection_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IMyService _service;

                public MyClass(IMyService service)
                {
                    _service = service;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InBuildMethod_NoDiagnostic()
    {
        // Build* methods are treated as factory patterns - allowed
        var source = Usings + """
            public interface IMyService { }

            public class ServiceBuilder
            {
                private readonly IServiceProvider _provider;

                public ServiceBuilder(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IMyService BuildService()
                {
                    return _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetService_NonGeneric_InConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly object? _service;

                public MyClass(IServiceProvider provider)
                {
                    _service = provider.GetService(typeof(IMyService));
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(11, 20, 11, 59)
                .WithArguments("IMyService"));
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task GetRequiredService_InAnonymousMethod_FactoryRegistration_NoDiagnostic()
    {
        // Anonymous method in factory registration - allowed
        var source = Usings + """
            public interface IMyService { }
            public interface IDependency { }
            public class MyService : IMyService
            {
                public MyService(IDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(delegate(IServiceProvider sp)
                    {
                        return new MyService(sp.GetRequiredService<IDependency>());
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InLocalFunction_ReportsDiagnostic()
    {
        // Local functions are not allowed contexts - should report
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceProvider _provider;

                public MyClass(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    IMyService GetService()
                    {
                        return _provider.GetRequiredService<IMyService>();
                    }
                    var service = GetService();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(18, 20, 18, 62)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetService_InStaticMethod_ReportsDiagnostic()
    {
        // Static helpers are ordinary application code, so they should still report.
        var source = Usings + """
            public interface IMyService { }

            public static class MyClass
            {
                public static IMyService GetServiceFromProvider(IServiceProvider provider)
                {
                    return provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(9, 16, 9, 57)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetService_InExtensionMethod_ReportsDiagnostic()
    {
        // Extension methods are helper code, not a DI-composition boundary.
        var source = Usings + """
            public interface IMyService { }

            public static class ServiceProviderExtensions
            {
                public static IMyService GetMyService(this IServiceProvider provider)
                {
                    return provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(9, 16, 9, 57)
                .WithArguments("IMyService"));
    }

    #endregion

    #region Hosting / Options factory contexts

    [Fact]
    public async Task GetRequiredService_InConstructorLocalProviderFactoryDelegate_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                public MyClass()
                {
                    Func<IServiceProvider, IMyService> factory =
                        sp => sp.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(10, 19, 10, 54)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InRegularMethodLocalProviderFactoryDelegate_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                public void DoWork()
                {
                    Func<IServiceProvider, IMyService> factory =
                        sp => sp.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(10, 19, 10, 54)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InBackgroundServiceExecuteAsync_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IMyService { }

            namespace Microsoft.Extensions.Hosting
            {
                public abstract class BackgroundService : IHostedService, System.IDisposable
                {
                    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
                    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                    public void Dispose() { }
                }

                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public class MyWorker : Microsoft.Extensions.Hosting.BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    await Task.Yield();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InHostedServiceStartAsync_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IMyService { }

            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public class MyHosted : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly IServiceProvider _provider;

                public MyHosted(IServiceProvider provider) => _provider = provider;

                public Task StartAsync(CancellationToken cancellationToken)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }

                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InHostedServiceHelperExecuteAsync_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IMyService { }

            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public class MyHosted : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly IServiceProvider _provider;

                public MyHosted(IServiceProvider provider) => _provider = provider;

                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task ExecuteAsync()
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(28, 23, 28, 65)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InHostedLifecycleMethods_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IMyService { }

            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedLifecycleService : IHostedService
                {
                    Task StartingAsync(CancellationToken cancellationToken);
                    Task StartedAsync(CancellationToken cancellationToken);
                    Task StoppingAsync(CancellationToken cancellationToken);
                    Task StoppedAsync(CancellationToken cancellationToken);
                }

                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public class MyHosted : Microsoft.Extensions.Hosting.IHostedLifecycleService
            {
                private readonly IServiceProvider _provider;

                public MyHosted(IServiceProvider provider) => _provider = provider;

                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StartingAsync(CancellationToken cancellationToken)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
                public Task StartedAsync(CancellationToken cancellationToken)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
                public Task StoppingAsync(CancellationToken cancellationToken)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
                public Task StoppedAsync(CancellationToken cancellationToken)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InIConfigureOptionsConfigure_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyOptions { }

            namespace Microsoft.Extensions.Options
            {
                public interface IConfigureOptions<TOptions> where TOptions : class
                {
                    void Configure(TOptions options);
                }
            }

            public class MyConfigure : Microsoft.Extensions.Options.IConfigureOptions<MyOptions>
            {
                private readonly IServiceProvider _provider;

                public MyConfigure(IServiceProvider provider) => _provider = provider;

                public void Configure(MyOptions options)
                {
                    var service = _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InOptionsHelperConfigure_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyOptions { }

            namespace Microsoft.Extensions.Options
            {
                public interface IConfigureOptions<TOptions> where TOptions : class
                {
                    void Configure(TOptions options);
                }
            }

            public class MyConfigure : Microsoft.Extensions.Options.IConfigureOptions<MyOptions>
            {
                private readonly IServiceProvider _provider;

                public MyConfigure(IServiceProvider provider) => _provider = provider;

                public void Configure(MyOptions options) { }

                public void Configure()
                {
                    var service = _provider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceLocatorAntiPattern)
                .WithSpan(24, 23, 24, 65)
                .WithArguments("IMyService"));
    }

    [Fact]
    public async Task GetRequiredService_InOptionsBuilderProviderDelegate_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.Options;

            public interface IMyService { }
            public class MyOptions { }

            namespace Microsoft.Extensions.Options
            {
                public sealed class OptionsBuilder<TOptions> where TOptions : class { }

                public static class OptionsBuilderExtensions
                {
                    public static OptionsBuilder<TOptions> Configure<TOptions>(
                        this OptionsBuilder<TOptions> builder,
                        Action<TOptions, IServiceProvider> configure)
                        where TOptions : class => builder;
                }
            }

            public class Startup
            {
                public void Configure(OptionsBuilder<MyOptions> builder)
                {
                    builder.Configure((options, sp) => sp.GetRequiredService<IMyService>());
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetRequiredService_InThirdPartyFactoryDelegate_NoDiagnostic()
    {
        // Convention-based: any delegate whose first parameter is IServiceProvider is a factory boundary.
        var source = Usings + """
            public interface IMyService { }
            public interface IDependency { }

            public static class ThirdPartyExtensions
            {
                public static IServiceCollection AddCustom<T>(this IServiceCollection services, Func<IServiceProvider, T> factory)
                    where T : class
                {
                    return services.AddTransient<T>(factory);
                }
            }

            public class DepImpl : IDependency { }

            public class MyService : IMyService
            {
                public MyService(IDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddCustom<IMyService>(sp => new MyService(sp.GetRequiredService<IDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
