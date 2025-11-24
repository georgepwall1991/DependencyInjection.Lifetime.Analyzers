using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI003_CaptiveDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task SingletonCapturingScoped_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IScopedService _scoped;
                public SingletonService(IScopedService scoped)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingTransient_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "transient", "ITransientService"));
    }

    [Fact]
    public async Task ScopedCapturingTransient_ViaConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly ITransientService _transient;
                public ScopedService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        // Scoped capturing transient is also a captive dependency
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 60)
                .WithArguments("ScopedService", "transient", "ITransientService"));
    }

    [Fact]
    public async Task SingletonCapturingMultipleScopedDependencies_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IScopedService1 { }
            public class ScopedService1 : IScopedService1 { }

            public interface IScopedService2 { }
            public class ScopedService2 : IScopedService2 { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService1 scoped1, IScopedService2 scoped2) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService1, ScopedService1>();
                    services.AddScoped<IScopedService2, ScopedService2>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService1"),
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(21, 9, 21, 69)
                .WithArguments("SingletonService", "scoped", "IScopedService2"));
    }

    #endregion

    #region Should Not Report Diagnostic (False Positives)

    [Fact]
    public async Task SingletonCapturingSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IDependency _dependency;
                public SingletonService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedCapturingScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly IDependency _dependency;
                public ScopedService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedCapturingSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly IDependency _dependency;
                public ScopedService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientCapturingAnything_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ITransientService { }
            public class TransientService : ITransientService
            {
                private readonly IScopedDependency _dependency;
                public TransientService(IScopedDependency dependency)
                {
                    _dependency = dependency;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddTransient<ITransientService, TransientService>();
                }
            }
            """;

        // Transient can capture anything - it's created fresh each time anyway
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceWithUnknownDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IUnregisteredDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    // IUnregisteredDependency is not registered - don't report
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        // Unknown dependencies should not trigger false positives
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
