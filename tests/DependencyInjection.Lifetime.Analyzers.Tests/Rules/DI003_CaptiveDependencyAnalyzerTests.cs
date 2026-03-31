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
    public async Task ScopedCapturingTransient_ViaConstructor_NoDiagnostic()
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

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
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

    [Fact]
    public async Task SingletonWithUnresolvableGreedyConstructor_AndCaptiveFallbackConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IUnregisteredDependency missing, ISingletonDependency singleton) { }

                public SingletonService(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(23, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task EarlierDuplicateRegistration_IsStillAnalyzedForCaptiveDependency()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }

            public interface IMyService { }
            public class CaptiveService : IMyService
            {
                public CaptiveService(IScopedDependency scoped) { }
            }

            public class SafeService : IMyService
            {
                public SafeService(ISingletonDependency singleton) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddSingleton<IMyService, CaptiveService>();
                    services.AddSingleton<IMyService, SafeService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(26, 9)
                .WithArguments("CaptiveService", "scoped", "IScopedDependency"));
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

    #region Factory Registration Tests

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(sp => new SingletonService(sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(17, 77, 17, 116)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_NonGenericGetService_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(sp => new SingletonService((IScopedService)sp.GetService(typeof(IScopedService))!));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 93)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_NonGenericGetRequiredService_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(sp => new SingletonService((IScopedService)sp.GetRequiredService(typeof(IScopedService))));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 93)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaFactory_WithActivatorUtilitiesAttribute_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                [ActivatorUtilitiesConstructor]
                public SingletonService(ISingletonDependency singleton) { }

                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(24, 40)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonWithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                [ActivatorUtilitiesConstructor]
                public SingletonService(ISingletonDependency dep) { }

                public SingletonService(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonWithActivatorUtilitiesConstructor_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(ISingletonDependency dep) { }

                [ActivatorUtilitiesConstructor]
                public SingletonService(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(23, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaMethodGroupFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(CreateSingletonService);
                }

                private static ISingletonService CreateSingletonService(IServiceProvider sp)
                {
                    return new SingletonService(sp.GetRequiredService<IScopedService>());
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(22, 37)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingScoped_ViaLocalFunctionFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(CreateSingletonService);

                    ISingletonService CreateSingletonService(IServiceProvider sp)
                    {
                        return new SingletonService(sp.GetRequiredService<IScopedService>());
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(21, 41)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonCapturingKeyedScoped_ViaFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => throw new System.NotImplementedException();
                }
            }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("green");
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(sp.GetRequiredKeyedService<IScopedService>("green")));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(31, 40)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task SingletonFactory_WithActivatorUtilitiesCreateInstance_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ActivatorUtilities
                {
                    public static T CreateInstance<T>(IServiceProvider provider) => throw new System.NotImplementedException();
                }
            }

            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService>(
                        sp => ActivatorUtilities.CreateInstance<SingletonService>(sp));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(26, 19)
                .WithArguments("ISingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonFactory_WithOpenGenericScopedRegistration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T> { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IRepository<string> repository) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                    services.AddSingleton<ISingletonService>(sp => new SingletonService(sp.GetRequiredService<IRepository<string>>()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(17, 77)
                .WithArguments("ISingletonService", "scoped", "IRepository"));
    }

    [Fact]
    public async Task SingletonWithMultipleCaptiveConstructorsForSameDependency_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency scoped) { }
                public SingletonService(IScopedDependency scoped, int unused = 0) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithLocation(18, 9)
                .WithArguments("SingletonService", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task SingletonFactoryMethodGroup_WithNoServiceResolution_NoDiagnostic()
    {
        var source = Usings + """
            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService>(CreateSingletonService);
                }

                private static ISingletonService CreateSingletonService(IServiceProvider sp)
                {
                    return new SingletonService();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonFactory_WithActivatorUtilitiesCreateInstanceAndExplicitArguments_NoDiagnostic()
    {
        var source = Usings + """
            public static class ActivatorUtilities
            {
                public static T CreateInstance<T>(IServiceProvider provider, params object[] arguments) => throw new System.NotImplementedException();
            }

            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedDependency dependency, object marker) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton<ISingletonService>(
                        sp => ActivatorUtilities.CreateInstance<SingletonService>(sp, new object()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region TryAddSingleton Coverage

    [Fact]
    public async Task TryAddSingletonCapturingScoped_ViaConstructor_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection TryAddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TService : class where TImplementation : class, TService => services;
                }
            }

            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.TryAddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(27, 9, 27, 72)
                .WithArguments("SingletonService", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task TryAddSingletonCapturingSingleton_ViaConstructor_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection TryAddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TService : class where TImplementation : class, TService => services;
                }
            }

            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(ISingletonDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.TryAddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonImplementationInstance_WithScopedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(ISingletonService), new SingletonService(new ScopedService()));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorSingletonImplementationInstance_WithScopedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.Add(ServiceDescriptor.Singleton(
                        typeof(ISingletonService),
                        new SingletonService(new ScopedService())));
                }
            }
            """;

        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Open Generic Constructor Coverage

    [Fact]
    public async Task OpenGenericSingletonConstructor_WithScopedParameter_NoDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        // DI009 handles open generic captive dependencies, not DI003
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Dedup Behavior

    [Fact]
    public async Task FactoryResolvingSameScopedDependencyTwice_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                public SingletonService(IScopedService s1, IScopedService s2) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService>(
                        sp => new SingletonService(
                            sp.GetRequiredService<IScopedService>(),
                            sp.GetRequiredService<IScopedService>()));
                }
            }
            """;

        // One diagnostic per unique captured dependency per registration
        await AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI003_CaptiveDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
                .WithSpan(19, 17, 19, 56)
                .WithArguments("ISingletonService", "scoped", "IScopedService"));
    }

    #endregion
}
