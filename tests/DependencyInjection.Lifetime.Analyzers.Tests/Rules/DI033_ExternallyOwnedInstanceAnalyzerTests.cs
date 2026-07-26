using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI033_ExternallyOwnedInstanceAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IConnectionPool { }

        """;

    [Fact]
    public async Task DisposableInstance_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class ConnectionPool : IConnectionPool, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI033:services.AddSingleton<IConnectionPool>(new ConnectionPool())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AsyncDisposableInstance_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class ConnectionPool : IConnectionPool, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI033:services.AddSingleton<IConnectionPool>(new ConnectionPool())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonDisposableInstance_NoDiagnostic()
    {
        // Nothing to dispose means no ownership question.
        var source =
            Usings
            + """
                public class ConnectionPool : IConnectionPool { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IConnectionPool>(new ConnectionPool());
                    }
                }
                """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TypeRegistration_NoDiagnostic()
    {
        // The documented fix: let the container create the instance and it disposes it too.
        var source =
            Usings
            + """
                public class ConnectionPool : IConnectionPool, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IConnectionPool, ConnectionPool>();
                    }
                }
                """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FactoryRegistration_NoDiagnostic()
    {
        // A factory result IS created by the container, so the container disposes it.
        var source =
            Usings
            + """
                public class ConnectionPool : IConnectionPool, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IConnectionPool>(sp => new ConnectionPool());
                    }
                }
                """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RemovedInstanceRegistration_NoDiagnostic()
    {
        // The descriptor is removed after it was added, so nobody is left holding the instance
        // through the container.
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IConnectionPool { }

            public class ConnectionPool : IConnectionPool, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IConnectionPool>(new ConnectionPool());
                    services.RemoveAll<IConnectionPool>();
                }
            }
            """;

        await AnalyzerVerifier<DI033_ExternallyOwnedInstanceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }
}
