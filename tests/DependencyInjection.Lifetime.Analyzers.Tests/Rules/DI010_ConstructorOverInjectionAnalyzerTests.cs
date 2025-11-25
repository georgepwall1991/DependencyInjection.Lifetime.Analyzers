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
            // Simulating ILogger<T> pattern (analyzer should exclude types named ILogger)
            public interface ILogger<T> { }

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
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, ILogger<MyService> logger) { }
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

        // ILogger<T> should be excluded from the count
        await AnalyzerVerifier<DI010_ConstructorOverInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIOptionsExcluded_NoDiagnostic()
    {
        var source = Usings + """
            // Simulating IOptions<T> pattern (analyzer should exclude types named IOptions)
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

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDep1 d1, IDep2 d2, IDep3 d3, IDep4 d4, IOptions<MyOptions> options) { }
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

        // IOptions<T> should be excluded from the count
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

    #endregion
}
