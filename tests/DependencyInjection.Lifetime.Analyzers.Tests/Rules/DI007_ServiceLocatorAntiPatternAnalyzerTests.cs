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
}
