using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI024_HostedServiceScopePerIterationAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService
            {
                Task StartAsync(CancellationToken cancellationToken);
                Task StopAsync(CancellationToken cancellationToken);
            }

            public abstract class BackgroundService : IHostedService
            {
                protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

                public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
        }

        public interface IWorker
        {
            Task DoWorkAsync(CancellationToken token);
        }

        public class Worker : IWorker
        {
            public Task DoWorkAsync(CancellationToken token) => Task.CompletedTask;
        }

        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddScoped<IWorker, Worker>();
            }
        }

        """;

    [Fact]
    public async Task HoistedScope_UsedInsideStoppingTokenLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedAsyncScope_UsedInsideLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    await using var scope = [|_scopeFactory.CreateAsyncScope()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_WhileTrueLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceProvider _provider;

                public PollingService(IServiceProvider provider) => _provider = provider;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_provider.CreateScope()|];
                    while (true)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_InfiniteForLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    for (;;)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_PeriodicTimerLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedServiceResolvedFromScope_OnlyServiceUsedInLoop_ReportsDiagnosticAtScopeCreation()
    {
        // The scope local itself never appears in the loop, but a service resolved from it does --
        // the scope is still effectively reused for every iteration.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScopedService_ResolvedFromInjectedProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceProvider _provider;

                public PollingService(IServiceProvider provider) => _provider = provider;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var worker = [|_provider.GetRequiredService<IWorker>()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_InHostedServiceStartAsync_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : IHostedService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public async Task StartAsync(CancellationToken cancellationToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(cancellationToken);
                    }
                }

                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingStatementScope_WrappingLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using (var scope = [|_scopeFactory.CreateScope()|])
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeCreatedInsideLoop_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeUsedOnlyBeforeLoop_NoDiagnostic()
    {
        // Startup-style scope (e.g. migrations) consumed entirely before the loop is fine.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeReassignedInsideLoop_NoDiagnostic()
    {
        // Dispose-and-recreate inside the loop means each iteration does get a fresh scope.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        scope.Dispose();
                        scope = _scopeFactory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_AllResolutionsProvenSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IClock { }
            public class Clock : IClock { }

            public static class SingletonRegistrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IClock, Clock>();
                }
            }

            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BoundedForLoop_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    for (var i = 0; i < 10; i++)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonHostedClass_SameShape_NoDiagnostic()
    {
        var source = Usings + """
            public class NotAHostedService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public NotAHostedService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StopAsync_HoistedScope_NoDiagnostic()
    {
        // StopAsync runs once at shutdown; its loops drain work and are not long-running
        // execution loops.
        var source = Usings + """
            public class PollingService : IHostedService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public async Task StopAsync(CancellationToken cancellationToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(cancellationToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedServiceWithUnknownRegistration_NoDiagnostic()
    {
        // No registration for IUnknown anywhere in the compilation: lifetime unprovable, stay silent.
        var source = Usings + """
            public interface IUnknown
            {
                Task RunAsync(CancellationToken token);
            }

            public class PollingService : BackgroundService
            {
                private readonly IServiceProvider _provider;

                public PollingService(IServiceProvider provider) => _provider = provider;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var unknown = _provider.GetRequiredService<IUnknown>();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await unknown.RunAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeCreatedInsideOuterLoop_UsedInInnerLoop_NoDiagnostic()
    {
        // The scope already lives per-iteration of the long-running loop; an inner batch loop
        // reusing it is fine.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        for (var i = 0; i < 5; i++)
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_AwaitForeachOverChannelReadAllAsync_ReportsDiagnostic()
    {
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_WhileAwaitWaitToReadAsync_ReportsDiagnostic()
    {
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (await _reader.WaitToReadAsync(stoppingToken))
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScopedService_UsedInsideAwaitForeachChannelLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceProvider _provider;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceProvider provider, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _provider = provider;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var worker = [|_provider.GetRequiredService<IWorker>()|];
                    await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                    {
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeInsideOuterLoop_HoistedAboveNestedChannelLoop_ReportsDiagnostic()
    {
        // The scope is fresh per outer iteration, but ReadAllAsync only completes when the
        // channel completes — the scope still spans an unbounded drain.
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        using var scope = [|_scopeFactory.CreateScope()|];
                        await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeBeforeOuterLoop_UsedOnlyInNestedChannelLoop_ReportsSingleDiagnostic()
    {
        // The scope is hoisted above both loops; the outer-loop analysis owns the report and
        // the nested channel loop must not duplicate it.
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeBeforeOuterLoop_ServiceResolvedInOuterBody_UsedInNestedChannelLoop_ReportsSingleDiagnostic()
    {
        // The hoisted scope above the outer loop is the one report; the service resolved from it
        // inside the outer body must not produce a second scoped-service diagnostic from the
        // nested channel-loop analysis.
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                        {
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingStatementAsOuterLoopBody_WrappingNestedChannelLoop_ReportsDiagnostic()
    {
        // The outer loop's body is the using statement itself (no block): the scope it declares
        // still spans the unbounded inner channel drain.
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                        using (var scope = [|_scopeFactory.CreateScope()|])
                        {
                            await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                            {
                                var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                                await worker.DoWorkAsync(stoppingToken);
                            }
                        }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeCreatedInsideAwaitForeachChannelLoop_NoDiagnostic()
    {
        var source = Usings + """
            public class QueueWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly System.Threading.Channels.ChannelReader<string> _reader;

                public QueueWorker(IServiceScopeFactory scopeFactory, System.Threading.Channels.ChannelReader<string> reader)
                {
                    _scopeFactory = scopeFactory;
                    _reader = reader;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    await foreach (var item in _reader.ReadAllAsync(stoppingToken))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_BoundedForeachOverCollection_NoDiagnostic()
    {
        // A plain foreach over a materialized collection is a bounded batch, like a bounded
        // for loop: reusing one scope across it is legitimate.
        var source = Usings + """
            public class BatchWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public BatchWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    foreach (var item in new[] { "a", "b", "c" })
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_AwaitForeachOverNonChannelReadAllAsync_NoDiagnostic()
    {
        // ReadAllAsync on a repository-style type is a bounded enumeration (read all rows once),
        // not a long-running channel consumer; only ChannelReader<T> qualifies.
        var source = Usings + """
            public interface IRowSource
            {
                System.Collections.Generic.IAsyncEnumerable<string> ReadAllAsync(CancellationToken token);
            }

            public class MigrationWorker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IRowSource _rows;

                public MigrationWorker(IServiceScopeFactory scopeFactory, IRowSource rows)
                {
                    _scopeFactory = scopeFactory;
                    _rows = rows;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = _scopeFactory.CreateScope();
                    await foreach (var row in _rows.ReadAllAsync(stoppingToken))
                    {
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedSingletonService_ResolvedFromInjectedProvider_NoDiagnostic()
    {
        var source = Usings + """
            public interface IClock { }
            public class Clock : IClock { }

            public static class SingletonRegistrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IClock, Clock>();
                }
            }

            public class PollingService : BackgroundService
            {
                private readonly IServiceProvider _provider;

                public PollingService(IServiceProvider provider) => _provider = provider;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var clock = _provider.GetRequiredService<IClock>();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        _ = clock;
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
