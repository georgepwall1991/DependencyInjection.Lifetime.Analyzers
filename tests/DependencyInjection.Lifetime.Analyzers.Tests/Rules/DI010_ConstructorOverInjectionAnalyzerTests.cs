using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI010_ConstructorOverInjectionAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task Constructor_WithFiveDependencies_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithSpan(29, 9, 29, 52)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task Constructor_WithSixDependencies_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }
            public interface IDep6 { }
            public class Dep6 : IDep6 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5, IDep6 d6) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IDep6, Dep6>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithSpan(32, 9, 32, 52)
                .WithArguments("MyService", 6));
    }

    [Fact]
    public async Task Constructor_WithSingletonLifetime_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDep1, Dep1>();
                    services.AddSingleton<IDep2, Dep2>();
                    services.AddSingleton<IDep3, Dep3>();
                    services.AddSingleton<IDep4, Dep4>();
                    services.AddSingleton<IDep5, Dep5>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithSpan(29, 9, 29, 55)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task Constructor_WithOverInjectedFallbackConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ILogger<T> { }
            public interface IOptions<T> { }
            public class MyOptions { }

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, ILogger<MyService> logger, IOptions<MyOptions> options) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(35, 9)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task Constructor_WithLongestResolvableConstructor_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }
            public interface IDep6 { }
            public class Dep6 : IDep6 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5, IDep6 d6) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(35, 9)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task FactoryRegistration_WithDirectObjectCreation_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService>(sp => new MyService(
                        sp.GetRequiredService<IDep1>(),
                        sp.GetRequiredService<IDep2>(),
                        sp.GetRequiredService<IDep3>(),
                        sp.GetRequiredService<IDep4>(),
                        sp.GetRequiredService<IDep5>()));
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithSpan(29, 9, 34, 45)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task FactoryRegistration_WithActivatorUtilitiesCreateInstance_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService>(sp => ActivatorUtilities.CreateInstance<MyService>(sp));
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(29, 9)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task FactoryRegistration_WithMethodGroupReturningDirectObjectCreation_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService>(CreateMyService);
                }

                private static MyService CreateMyService(IServiceProvider sp)
                {
                    return new MyService(
                        (IDep1)sp.GetService(typeof(IDep1)),
                        (IDep2)sp.GetService(typeof(IDep2)),
                        (IDep3)sp.GetService(typeof(IDep3)),
                        (IDep4)sp.GetService(typeof(IDep4)),
                        (IDep5)sp.GetService(typeof(IDep5)));
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(29, 9)
                .WithArguments("MyService", 5));
    }

    [Fact]
    public async Task Constructor_WithUserDefinedLoggerTypeInDifferentNamespace_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace MyApp.Logging
            {
                public interface ILogger<T> { }
            }

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    IDep5 d5,
                    MyApp.Logging.ILogger<MyService> logger) { }
            }

            public sealed class MyServiceLogger : MyApp.Logging.ILogger<MyService> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<MyApp.Logging.ILogger<MyService>, MyServiceLogger>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(43, 9)
                .WithArguments("MyService", 6));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task Constructor_WithFourDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithOneDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithILoggerExcluded_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger<T> { }
            }

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    Microsoft.Extensions.Logging.ILogger<MyService> logger) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIOptionsExcluded_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.Options
            {
                public interface IOptions<T> { }
            }

            public class MyOptions { }

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    Microsoft.Extensions.Options.IOptions<MyOptions> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIConfigurationExcluded_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.Configuration
            {
                public interface IConfiguration { }
            }

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(
                    IDep1 d1,
                    IDep2 d2,
                    IDep3 d3,
                    IDep4 d4,
                    Microsoft.Extensions.Configuration.IConfiguration configuration) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithValueTypeExcluded_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading;

            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, CancellationToken cancellationToken) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        // Value types like CancellationToken should be excluded from the count
        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }
            public interface IDep6 { }
            public class Dep6 : IDep6 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                [ActivatorUtilitiesConstructor]
                public MyService(IDep1 d1) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5, IDep6 d6) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IDep6, Dep6>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithOnlyShorterResolvableConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithOptionalFifthDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5 = null) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryRegistration_WithComplexBody_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1) { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService>(sp =>
                    {
                        if (DateTime.UtcNow.Ticks > 0)
                        {
                            return new MyService(
                                sp.GetRequiredService<IDep1>(),
                                sp.GetRequiredService<IDep2>(),
                                sp.GetRequiredService<IDep3>(),
                                sp.GetRequiredService<IDep4>(),
                                sp.GetRequiredService<IDep5>());
                        }

                        return new MyService(sp.GetRequiredService<IDep1>());
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithEditorConfigThresholdOfFive_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI010.max_dependencies = 5
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source, editorConfig);
    }

    [Fact]
    public async Task Constructor_WithEditorConfigThresholdOfThree_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI010.max_dependencies = 3
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            editorConfig,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(26, 9)
                .WithArguments("MyService", 4));
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_OnOverInjectedConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }
            public interface IDep6 { }
            public class Dep6 : IDep6 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1) { }

                [ActivatorUtilitiesConstructor]
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5, IDep6 d6) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDep1, Dep1>();
                    services.AddScoped<IDep2, Dep2>();
                    services.AddScoped<IDep3, Dep3>();
                    services.AddScoped<IDep4, Dep4>();
                    services.AddScoped<IDep5, Dep5>();
                    services.AddScoped<IDep6, Dep6>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ConstructorOverInjection)
                .WithLocation(35, 9)
                .WithArguments("MyService", 6));
    }

    [Fact]
    public async Task UnregisteredService_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    // MyService is not registered
                    services.AddScoped<IDep1, Dep1>();
                }
            }
            """;

        // Only registered services should be analyzed
        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationInstance_WithOverInjectedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDep1 { }
            public class Dep1 : IDep1 { }
            public interface IDep2 { }
            public class Dep2 : IDep2 { }
            public interface IDep3 { }
            public class Dep3 : IDep3 { }
            public interface IDep4 { }
            public class Dep4 : IDep4 { }
            public interface IDep5 { }
            public class Dep5 : IDep5 { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                private MyService() { }

                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IDep5 d5) { }

                public static MyService Create() => new MyService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), MyService.Create());
                }
            }
            """;

        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
