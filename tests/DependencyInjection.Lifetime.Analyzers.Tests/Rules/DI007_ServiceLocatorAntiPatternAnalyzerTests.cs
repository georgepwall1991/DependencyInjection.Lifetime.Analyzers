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
    public async Task GetRequiredService_InMethodWithIServiceProviderParameter_NoDiagnostic()
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

        await AnalyzerVerifier<DI007_ServiceLocatorAntiPatternAnalyzer>.VerifyNoDiagnosticsAsync(source);
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

    #endregion
}
