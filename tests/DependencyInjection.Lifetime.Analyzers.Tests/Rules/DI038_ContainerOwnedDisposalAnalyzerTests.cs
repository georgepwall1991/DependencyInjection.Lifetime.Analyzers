using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI038_ContainerOwnedDisposalAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IConnection : IDisposable { }
        public sealed class Connection : IConnection { public void Dispose() { } }

        """;

    // ---- Injected leg: positives ----

    [Fact]
    public async Task InjectedSingleton_DisposedInConsumerDispose_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        {|DI038:_connection.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InjectedScoped_DisposedInConsumerDispose_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        {|DI038:_connection.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IConnection, Connection>();
                        services.AddScoped<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InjectedSingleton_ConditionalDispose_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        {|DI038:_connection?.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddSingleton<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InjectedSingleton_CastDispose_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public interface IPipe { }
                public sealed class Pipe : IPipe, IDisposable { public void Dispose() { } }

                public sealed class Consumer : IDisposable
                {
                    private readonly IPipe _pipe;

                    public Consumer(IPipe pipe) { _pipe = pipe; }

                    public void Dispose()
                    {
                        {|DI038:((IDisposable)_pipe).Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IPipe, Pipe>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InjectedSingleton_DisposeAsync_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public interface IQueue : IAsyncDisposable { }
                public sealed class Queue : IQueue
                {
                    public ValueTask DisposeAsync() => default;
                }

                public sealed class Consumer : IAsyncDisposable
                {
                    private readonly IQueue _queue;

                    public Consumer(IQueue queue) { _queue = queue; }

                    public async ValueTask DisposeAsync()
                    {
                        await {|DI038:_queue.DisposeAsync()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IQueue, Queue>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConstructorParameter_DisposedDirectly_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer
                {
                    public Consumer(IConnection connection)
                    {
                        {|DI038:connection.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetOnlyAutoProperty_AssignedFromConstructor_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    public IConnection Connection { get; }

                    public Consumer(IConnection connection) { Connection = connection; }

                    public void Dispose()
                    {
                        {|DI038:Connection.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NullClearOutAfterDispose_StillReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        {|DI038:_connection.Dispose()|};
                        _connection = null;
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PrimaryConstructor_FieldInitializerFromParameter_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer(IConnection connection) : IDisposable
                {
                    private readonly IConnection _connection = connection;

                    public void Dispose()
                    {
                        {|DI038:_connection.Dispose()|};
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FrameworkSingleton_InjectedMemoryCacheDisposed_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.Caching.Memory;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class CacheConsumer : IDisposable
            {
                private readonly IMemoryCache _cache;

                public CacheConsumer(IMemoryCache cache) { _cache = cache; }

                public void Dispose()
                {
                    {|DI038:_cache.Dispose()|};
                }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddMemoryCache();
                    services.AddTransient<CacheConsumer>();
                }
            }
            """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.ReferenceAssembliesWithFrameworkExtensions);
    }

    // ---- Injected leg: negatives ----

    [Fact]
    public async Task TransientDependency_NoDiagnostic()
    {
        // A transient disposable is exclusively the consumer's instance; the whole
        // tracking-and-disposal problem for transients is DI008's finding.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddTransient<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredDependency_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredConsumer_NoDiagnostic()
    {
        // A type the container does not construct receives its constructor arguments from
        // whoever created it, and that caller may be transferring ownership.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryRegisteredConsumer_NoDiagnostic()
    {
        // A factory lambda passes whatever it likes to the constructor — here a caller-owned
        // instance — so container ownership of the argument is not provable.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddSingleton<Consumer>(sp => new Consumer(new Connection()));
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FieldAlsoAssignedFromNew_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Reset()
                    {
                        _connection = new Connection();
                    }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PublicMutableField_NoDiagnostic()
    {
        // An externally assignable member can be replaced from code the assignment scan never
        // sees, so ownership is not provable.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    public IConnection Connection;

                    public Consumer(IConnection connection) { Connection = connection; }

                    public void Dispose()
                    {
                        Connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExpressionBodiedProperty_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    private IConnection Active => _connection ?? throw new InvalidOperationException();

                    public void Dispose()
                    {
                        Active.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PreBuiltInstanceRegistration_NoDiagnostic()
    {
        // Disposing a pre-built instance deliberately is DI033's documented remediation: the
        // container never disposes what it did not create.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection>(new Connection());
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConstructorParameterReassigned_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer
                {
                    public Consumer(IConnection connection)
                    {
                        connection = new Connection();
                        connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SelfDisposeCall_NoDiagnostic()
    {
        // `this.Dispose()` binds to the implicit this parameter, not an injected dependency.
        var source =
            Usings
            + """
                public sealed class SelfCleaning : IDisposable
                {
                    public SelfCleaning()
                    {
                        this.Dispose();
                    }

                    public void Shutdown()
                    {
                        this.Dispose();
                    }

                    public void Dispose() { }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<SelfCleaning>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MixedLifetimeRegistrations_NoDiagnostic()
    {
        // With a transient registration in the mix, the resolved dependency may be the
        // consumer's own instance, so the shared-instance claim does not hold.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MixedSingletonScopedRegistrations_NoDiagnostic()
    {
        // Singleton and scoped registrations of the same service type disagree on which
        // instance is shared, so the claim is ambiguous.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddScoped<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeFactoryResolution_NoDiagnostic()
    {
        // Provider infrastructure types have their own disposal rules (DI001, DI014); they are
        // never this rule's finding even when a registration exists.
        var source =
            Usings
            + """
                public sealed class PoolingScopeFactory : IServiceScopeFactory, IDisposable
                {
                    public IServiceScope CreateScope() => default;
                    public void Dispose() { }
                }

                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IServiceScopeFactory, PoolingScopeFactory>();
                        var provider = services.BuildServiceProvider();
                        var factory = provider.GetRequiredService<IServiceScopeFactory>();
                        ((IDisposable)factory).Dispose();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ThisConstructorChaining_NoDiagnostic()
    {
        // `: this(...)` can route a caller-created argument into the assigning constructor.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer() : this(new Connection()) { }

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        _connection.Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedParameter_NoDiagnostic()
    {
        // A [FromKeyedServices] parameter resolves from a keyed registration slot the unkeyed
        // lifetime proof does not inspect.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IConnection : IDisposable { }
            public sealed class Connection : IConnection { public void Dispose() { } }

            public sealed class Consumer : IDisposable
            {
                private readonly IConnection _connection;

                public Consumer([FromKeyedServices("primary")] IConnection connection)
                {
                    _connection = connection;
                }

                public void Dispose()
                {
                    _connection.Dispose();
                }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IConnection, Connection>();
                    services.AddKeyedSingleton<IConnection, Connection>("primary");
                    services.AddTransient<Consumer>();
                }
            }
            """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    // ---- Resolved leg: positives ----

    [Fact]
    public async Task UsingDeclarationOnResolvedSingleton_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using var connection = {|DI038:provider.GetRequiredService<IConnection>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImmediateDisposeOnResolvedSingleton_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        {|DI038:provider.GetRequiredService<IConnection>().Dispose()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalResolvedThenDisposed_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        var connection = provider.GetRequiredService<IConnection>();
                        {|DI038:connection.Dispose()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingStatementOnResolvedSingleton_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using (var connection = {|DI038:provider.GetService<IConnection>()|})
                        {
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    // ---- Resolved leg: negatives ----

    [Fact]
    public async Task ResolvedScopedService_NoDiagnostic()
    {
        // Disposing a scoped resolution inside the scope you own is a double dispose at worst;
        // v1 keeps the resolved tier to singletons.
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddScoped<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using var scope = provider.CreateScope();
                        using var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ResolvedTransient_NoDiagnostic()
    {
        // Disposing a resolved transient is the caller doing DI008's cleanup; it is not a
        // shared-instance defect.
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddTransient<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using var connection = provider.GetRequiredService<IConnection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalReassignedBeforeDispose_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        var connection = provider.GetRequiredService<IConnection>();
                        connection = new Connection();
                        connection.Dispose();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UserDefinedResolutionHelper_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Locator
                {
                    public static T GetRequiredService<T>(IServiceProvider provider) => default;
                }

                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using var connection = (IDisposable)Locator.GetRequiredService<IConnection>(provider);
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScopeUsing_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        var provider = services.BuildServiceProvider();
                        using var scope = provider.CreateScope();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
