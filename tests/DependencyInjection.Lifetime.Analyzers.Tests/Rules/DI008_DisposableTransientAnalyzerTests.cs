using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI008_DisposableTransientAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task TransientDisposable_GenericTwoTypeArgs_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 63)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_GenericSingleTypeArg_ReportsDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(12, 9, 12, 51)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientAsyncDisposable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }
            public class AsyncDisposableService : IMyService, IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, AsyncDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 15, 68)
                .WithArguments("AsyncDisposableService", "IAsyncDisposable"));
    }

    [Fact]
    public async Task TransientBothDisposable_ReportsAsyncDisposable()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }
            public class BothDisposableService : IMyService, IDisposable, IAsyncDisposable
            {
                public void Dispose() { }
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, BothDisposableService>();
                }
            }
            """;

        // When both are implemented, we report IAsyncDisposable (checked first)
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(16, 9, 16, 67)
                .WithArguments("BothDisposableService", "IAsyncDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_TypeofSyntax_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient(typeof(IMyService), typeof(DisposableService));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 77)
                .WithArguments("DisposableService", "IDisposable"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task TransientNonDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class NonDisposableService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, NonDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryRegistration_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp => new DisposableService());
                }
            }
            """;

        // Factory registrations are OK - caller is responsible for disposal
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryRegistrationWithDep_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IDependency { }
            public class DisposableService : IMyService, IDisposable
            {
                public DisposableService(IDependency dep) { }
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp =>
                        new DisposableService(sp.GetRequiredService<IDependency>()));
                }
            }
            """;

        // Factory registrations with dependencies are OK
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryMethodGroupMemberAccess_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public static class FactoryMethods
            {
                public static DisposableService Create(IServiceProvider sp) => new DisposableService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<DisposableService>(FactoryMethods.Create);
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CustomLookalikeServiceCollection_NoDiagnostic()
    {
        var source = """
            using System;
            using Custom;

            namespace Custom
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddTransient<T>(this IServiceCollection services)
                        where T : class => services;
                }
            }

            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(Custom.IServiceCollection services)
                {
                    services.AddTransient<DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
