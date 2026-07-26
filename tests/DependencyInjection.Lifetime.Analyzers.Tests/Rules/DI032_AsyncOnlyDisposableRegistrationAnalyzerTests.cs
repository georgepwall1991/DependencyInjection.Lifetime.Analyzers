using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI032_AsyncOnlyDisposableRegistrationAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IWorker { }

        """;

    [Fact]
    public async Task Singleton_AsyncOnlyDisposable_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker, AsyncWorker>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Scoped_AsyncOnlyDisposable_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddScoped<IWorker, AsyncWorker>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BothDisposableInterfaces_NoDiagnostic()
    {
        // Implementing IDisposable alongside IAsyncDisposable is the documented fix: a
        // synchronous provider disposal has something to call.
        var source =
            Usings
            + """
                public class Worker : IWorker, IDisposable, IAsyncDisposable
                {
                    public void Dispose() { }
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, Worker>();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonDisposableService_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Worker : IWorker { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, Worker>();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PreBuiltInstance_NoDiagnostic()
    {
        // The container never disposes an instance it did not create, so it never reaches the
        // synchronous-disposal throw. That registration is DI033's finding.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker>(new AsyncWorker());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FactoryConstructingAsyncOnlyDisposable_ReportsDiagnostic()
    {
        // The container creates and tracks whatever a factory returns, so the synchronous
        // disposal throw applies exactly as it does to a type registration.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker>(sp => new AsyncWorker())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueFactory_NoDiagnostic()
    {
        // Nothing at the registration site proves what this factory builds.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    private static IWorker Create() => new AsyncWorker();

                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker>(sp => Create());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RemovedRegistration_NoDiagnostic()
    {
        // A descriptor removed after it was added never reaches the provider.
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IWorker { }

            public class AsyncWorker : IWorker, IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IWorker, AsyncWorker>();
                    services.RemoveAll<IWorker>();
                }
            }
            """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }
}
