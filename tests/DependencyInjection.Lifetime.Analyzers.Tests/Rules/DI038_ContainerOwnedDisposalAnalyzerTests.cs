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
    public async Task TransientConsumer_ScopedDependency_ReportsDiagnostic()
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
                        services.AddTransient<Consumer>();
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
                        services.AddScoped<Consumer>();
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

    [Fact]
    public async Task ThisQualifiedFieldDispose_ReportsDiagnostic()
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
                        {|DI038:this._connection.Dispose()|};
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

    // ---- Injected leg: negatives ----

    [Fact]
    public async Task WrapThenStoreDecorator_NoDiagnostic()
    {
        // The constructor rebinds the parameter to a consumer-owned wrapper before storing it,
        // so the disposed member holds the consumer's own object.
        var source =
            Usings
            + """
                public sealed class LoggingConnection : IConnection
                {
                    public LoggingConnection(IConnection inner) { }
                    public void Dispose() { }
                }

                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;

                    public Consumer(IConnection connection)
                    {
                        connection = new LoggingConnection(connection);
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
                        services.AddTransient<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ManuallyComposedInstanceMemberDispose_NoDiagnostic()
    {
        // Disposing through another instance's member may target a manually composed graph the
        // caller owns; only this-rooted access proves the container-built instance.
        var source =
            Usings
            + """
                public sealed class Holder
                {
                    public Holder(IConnection connection) { Connection = connection; }

                    public IConnection Connection { get; }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Holder>();
                    }

                    public static void ManualComposition()
                    {
                        var mine = new Holder(new Connection());
                        mine.Connection.Dispose();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InitOnlyProperty_NoDiagnostic()
    {
        // An init-only property is assignable from object initializers outside the type, which
        // the assignment scan never sees.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    public IConnection Connection { get; init; }

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
    public async Task RefAliasRebindsField_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Swap()
                    {
                        ref IConnection alias = ref _connection;
                        alias = new Connection();
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
    public async Task EqualLifetime_ScopedPair_NoDiagnostic()
    {
        // A scoped consumer and its scoped dependency are torn down together at scope end; the
        // flagged call would be a contractually idempotent co-teardown double dispose, the same
        // shape the resolved tier's scoped exclusion and DI026's same-scope reasoning accept.
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
                        services.AddScoped<IConnection, Connection>();
                        services.AddScoped<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task EqualLifetime_SingletonPair_NoDiagnostic()
    {
        // A singleton consumer's Dispose runs only at provider teardown, when its singleton
        // dependency is being disposed in the same pass; CA2213 actively prescribes this code.
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
                        services.AddSingleton<Consumer>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NamedDisposeWithoutInterface_NoDiagnostic()
    {
        // The container disposes only through the disposal interfaces; a method that merely
        // shares the Dispose name is an ordinary method the consumer may legitimately call.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IPool
            {
                void Dispose();
            }

            public sealed class Pool : IPool
            {
                public void Dispose() { }
            }

            public sealed class Consumer
            {
                private readonly IPool _pool;

                public Consumer(IPool pool) { _pool = pool; }

                public void Shutdown()
                {
                    _pool.Dispose();
                }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IPool, Pool>();
                    services.AddTransient<Consumer>();
                }
            }
            """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NamedDisposeAsyncWithoutInterface_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            public interface IFlusher
            {
                ValueTask DisposeAsync();
            }

            public sealed class Flusher : IFlusher
            {
                public ValueTask DisposeAsync() => default;
            }

            public sealed class Consumer
            {
                private readonly IFlusher _flusher;

                public Consumer(IFlusher flusher) { _flusher = flusher; }

                public async Task ShutdownAsync()
                {
                    await _flusher.DisposeAsync();
                }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IFlusher, Flusher>();
                    services.AddTransient<Consumer>();
                }
            }
            """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OverriddenConsumerRegistration_NoDiagnostic()
    {
        // The later factory registration wins the consumer's slot, so at runtime the consumer is
        // factory-built with a caller-owned argument; the superseded type-based default must not
        // prove container construction.
        var source =
            Usings
            + """
                public interface IWidget { }

                public sealed class Widget : IWidget, IDisposable
                {
                    private readonly IConnection _connection;

                    public Widget(IConnection connection) { _connection = connection; }

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
                        services.AddScoped<IWidget, Widget>();
                        services.AddScoped<IWidget>(sp => new Widget(new Connection()));
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClearedConsumerRegistration_NoDiagnostic()
    {
        // The type-based consumer registration is removed by Clear() before the provider is
        // built, so the container never constructs the consumer.
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
                        services.Clear();
                        services.AddSingleton<IConnection, Connection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OwnershipFlagGuard_NoDiagnostic()
    {
        // The dual-use leaveOpen idiom: the container fills the default (non-owning) flag, so
        // the guarded disposal never runs on the container path.
        var source =
            Usings
            + """
                public sealed class Exporter : IDisposable
                {
                    private readonly IConnection _connection;
                    private readonly bool _owns;

                    public Exporter(IConnection connection, bool owns = false)
                    {
                        _connection = connection;
                        _owns = owns;
                    }

                    public void Dispose()
                    {
                        if (_owns)
                        {
                            _connection.Dispose();
                        }
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Exporter>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PartialConsumerOwnershipFlag_NoDiagnostic()
    {
        // The ownership flag is assigned in one partial declaration file and read in another;
        // the proof must cross syntax trees.
        var file1 = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IConnection : IDisposable { }
            public sealed class Connection : IConnection { public void Dispose() { } }

            public sealed partial class Exporter
            {
                private readonly IConnection _connection;
                private readonly bool _owns;

                public Exporter(IConnection connection, bool owns = false)
                {
                    _connection = connection;
                    _owns = owns;
                }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IConnection, Connection>();
                    services.AddTransient<Exporter>();
                }
            }
            """;
        var file2 = """
            using System;

            public sealed partial class Exporter : IDisposable
            {
                public void Dispose()
                {
                    if (_owns)
                    {
                        _connection.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(
            new[] { ("/0/File1.cs", file1), ("/0/File2.cs", file2) });
    }

    [Fact]
    public async Task NegatedOwnershipFlag_ReportsDiagnostic()
    {
        // `if (!_owns)` guards the branch the non-owning container path executes.
        var source =
            Usings
            + """
                public sealed class Exporter : IDisposable
                {
                    private readonly IConnection _connection;
                    private readonly bool _owns;

                    public Exporter(IConnection connection, bool owns = false)
                    {
                        _connection = connection;
                        _owns = owns;
                    }

                    public void Dispose()
                    {
                        if (!_owns)
                        {
                            {|DI038:_connection.Dispose()|};
                        }
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Exporter>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ElseBranchOwnershipFlag_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Exporter : IDisposable
                {
                    private readonly IConnection _connection;
                    private readonly bool _owns;

                    public Exporter(IConnection connection, bool owns = false)
                    {
                        _connection = connection;
                        _owns = owns;
                    }

                    public void Dispose()
                    {
                        if (_owns)
                        {
                        }
                        else
                        {
                            {|DI038:_connection.Dispose()|};
                        }
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Exporter>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MutableOwnershipFlagRewritten_ReportsDiagnostic()
    {
        // A flag rewritten in a method can flip on a container-built instance, so it proves no
        // conditional ownership.
        var source =
            Usings
            + """
                public sealed class Exporter : IDisposable
                {
                    private readonly IConnection _connection;
                    private bool _owns;

                    public Exporter(IConnection connection, bool owns = false)
                    {
                        _connection = connection;
                        _owns = owns;
                    }

                    public void TakeOwnership()
                    {
                        _owns = true;
                    }

                    public void Dispose()
                    {
                        if (_owns)
                        {
                            {|DI038:_connection.Dispose()|};
                        }
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        services.AddTransient<Exporter>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DisposedLatchGuard_StillReportsDiagnostic()
    {
        // A run-once latch is assigned in methods, not from a constructor parameter, so it does
        // not read as conditional ownership.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private readonly IConnection _connection;
                    private bool _disposed;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Dispose()
                    {
                        if (!_disposed)
                        {
                            {|DI038:_connection.Dispose()|};
                            _disposed = true;
                        }
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
                        services.AddTransient<Consumer>(sp => new Consumer(new Connection()));
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
    public async Task ScopeFactoryDependency_NoDiagnostic()
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

                public sealed class Consumer : IDisposable
                {
                    private readonly IServiceScopeFactory _factory;

                    public Consumer(IServiceScopeFactory factory) { _factory = factory; }

                    public void Dispose()
                    {
                        ((IDisposable)_factory).Dispose();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IServiceScopeFactory, PoolingScopeFactory>();
                        services.AddTransient<Consumer>();
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

    [Fact]
    public async Task DeconstructionReassignsField_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;
                    private int _generation;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Swap()
                    {
                        (_connection, _generation) = (new Connection(), 1);
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
    public async Task OutArgumentRebindsField_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Reconnect()
                    {
                        Create(out _connection);
                    }

                    private static void Create(out IConnection connection) =>
                        connection = new Connection();

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
    public async Task RefAliasRebindsConstructorParameter_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer
                {
                    public Consumer(IConnection connection)
                    {
                        ref IConnection alias = ref connection;
                        alias = new Connection();
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
    public async Task ConditionalAccessMemberWrite_NoDiagnostic()
    {
        // `other?._connection = ...` (C# 14) writes the member through a receiver the ownership
        // scan cannot classify.
        var source =
            Usings
            + """
                public sealed class Consumer : IDisposable
                {
                    private IConnection _connection;

                    public Consumer(IConnection connection) { _connection = connection; }

                    public void Adopt(Consumer other)
                    {
                        other?._connection = new Connection();
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
    public async Task DeconstructionReassignsConstructorParameter_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class Consumer
                {
                    public Consumer(IConnection connection)
                    {
                        (connection, _) = ((IConnection)new Connection(), 0);
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

    // ---- Resolved leg: positives ----

    [Fact]
    public async Task UsingDeclarationOnResolvedSingleton_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
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
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
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
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
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
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
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
    public async Task AwaitUsingResolvedAsyncDisposable_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public interface IQueue : IAsyncDisposable { }
                public sealed class Queue : IQueue
                {
                    public ValueTask DisposeAsync() => default;
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IQueue, Queue>();
                    }

                    public static async Task RunAsync(IServiceProvider provider)
                    {
                        await using var queue = {|DI038:provider.GetRequiredService<IQueue>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CastDisposeResolvedDisposableImplementation_ReportsDiagnostic()
    {
        // The service abstraction is not disposable, but the registered implementation is, so
        // the container disposes it and the cast-dispose tears down the shared instance.
        var source =
            Usings
            + """
                public interface IExportChannel { }

                public sealed class ExportChannel : IExportChannel, IDisposable
                {
                    public void Dispose() { }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IExportChannel, ExportChannel>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        {|DI038:((IDisposable)provider.GetRequiredService<IExportChannel>()).Dispose()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AwaitUsingPatternOnlyAsyncDisposable_NoDiagnostic()
    {
        // Pattern-based DisposeAsync satisfies `await using`, but the container disposes only
        // through the real interfaces, so this is not container-run disposal.
        var source =
            Usings
            + """
                public interface IBatch
                {
                    ValueTask DisposeAsync();
                }

                public sealed class Batch : IBatch
                {
                    public ValueTask DisposeAsync() => default;
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IBatch, Batch>();
                    }

                    public static async Task RunAsync(IServiceProvider provider)
                    {
                        await using var batch = provider.GetRequiredService<IBatch>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ThrowawayProviderResolution_NoDiagnostic()
    {
        // A provider built and disposed by the same member has one consumer; disposing the
        // resolution early is an idempotent double dispose, not a shared-instance defect.
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Run(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                        using var provider = services.BuildServiceProvider();
                        using var connection = provider.GetRequiredService<IConnection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CustomProviderReceiver_NoDiagnostic()
    {
        // A hand-rolled provider can hand out caller-owned instances; only the framework
        // provider types prove the instance came from the container the registrations describe.
        var source =
            Usings
            + """
                public sealed class StubProvider : IServiceProvider
                {
                    public object GetService(Type serviceType) => new Connection();
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run()
                    {
                        var custom = new StubProvider();
                        using var connection = custom.GetRequiredService<IConnection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }



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
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddTransient<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        using var connection = provider.GetRequiredService<IConnection>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RefAliasRebindsLocal_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        var connection = provider.GetRequiredService<IConnection>();
                        ref IConnection alias = ref connection;
                        alias = new Connection();
                        connection.Dispose();
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
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        var connection = provider.GetRequiredService<IConnection>();
                        connection = new Connection();
                        connection.Dispose();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_ContainerOwnedDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DeconstructionReassignsLocal_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        var connection = provider.GetRequiredService<IConnection>();
                        (connection, _) = ((IConnection)new Connection(), 0);
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
                public static class LocatorExtensions
                {
                    public static T GetRequiredService<T>(this IServiceProvider provider, int marker) => default;
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IConnection, Connection>();
                    }

                    public static void Run(IServiceProvider provider)
                    {
                        using var connection = (IDisposable)LocatorExtensions.GetRequiredService<IConnection>(provider, 1);
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
