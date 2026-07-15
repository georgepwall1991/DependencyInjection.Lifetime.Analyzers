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
    public async Task HoistedScope_CompoundCancellationLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var secondaryToken = CancellationToken.None;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested && !secondaryToken.IsCancellationRequested)
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
    public async Task HoistedScope_ParenthesizedCompoundCancellationLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var secondaryToken = CancellationToken.None;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while ((!stoppingToken.IsCancellationRequested) && (!secondaryToken.IsCancellationRequested))
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
    public async Task HoistedScope_DisjunctiveCancellationLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var secondaryToken = CancellationToken.None;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested || !secondaryToken.IsCancellationRequested)
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
    public async Task HoistedScope_ParenthesizedNegatedCancellationLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!(stoppingToken.IsCancellationRequested))
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
    public async Task HoistedScope_NegatedDisjunctiveCancellationLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var secondaryToken = CancellationToken.None;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!(stoppingToken.IsCancellationRequested || secondaryToken.IsCancellationRequested))
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
    public async Task HoistedScope_NegatedDisjunctionWithBoundedOperand_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var remaining = 10;
                    using var scope = _scopeFactory.CreateScope();
                    while (!(stoppingToken.IsCancellationRequested || remaining-- > 0))
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
    public async Task HoistedScope_NegatedConjunctionWithCancellationFirst_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var gate = false;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!(stoppingToken.IsCancellationRequested && gate))
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
    public async Task HoistedScope_NegatedConjunctionWithCancellationSecond_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var gate = false;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!(gate && stoppingToken.IsCancellationRequested))
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
    public async Task HoistedScope_NegatedConjunctionWithoutCancellation_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var firstGate = false;
                    var secondGate = false;
                    using var scope = _scopeFactory.CreateScope();
                    while (!(firstGate && secondGate))
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
    public async Task HoistedScope_CancellationAndBoundedCondition_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var remaining = 10;
                    using var scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested && remaining-- > 0)
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
    public async Task HoistedScope_BoundedAndCancellationCondition_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var remaining = 10;
                    using var scope = _scopeFactory.CreateScope();
                    while (remaining-- > 0 && !stoppingToken.IsCancellationRequested)
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
    public async Task HoistedScope_BoundedOrCancellationCondition_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var remaining = 10;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (remaining-- > 0 || !stoppingToken.IsCancellationRequested)
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
    public async Task HoistedScope_CancellationOrBoundedCondition_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var remaining = 10;
                    using var scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested || remaining-- > 0)
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
    public async Task ScopeField_AssignedInStartAsync_UsedInExecuteLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public override Task StartAsync(CancellationToken cancellationToken)
                {
                    _scope = [|_scopeFactory.CreateScope()|];
                    return base.StartAsync(cancellationToken);
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AssignedInConstructor_UsedInExecuteLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AssignedInExecuteAsyncBeforeLoop_UsedInLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _scope = [|_scopeFactory.CreateScope()|];
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedServiceField_ResolvedInConstructor_UsedInExecuteLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IWorker _worker;

                public PollingService(IServiceProvider provider) => _worker = [|provider.GetRequiredService<IWorker>()|];

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await _worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_LazyCoalesceAssignedInStartAsync_UsedInExecuteLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public override Task StartAsync(CancellationToken cancellationToken)
                {
                    _scope ??= [|_scopeFactory.CreateScope()|];
                    return base.StartAsync(cancellationToken);
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_LazyCoalesceAssignedInsideLoop_ReportsDiagnostic()
    {
        // ??= inside the loop creates the scope on the first iteration and reuses it forever —
        // it is lazy hoisting, not dispose-and-recreate.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        _scope ??= [|_scopeFactory.CreateScope()|];
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_PartialClass_FieldInOtherPart_ReportsDiagnostic()
    {
        var source = Usings + """
            public partial class PollingService
            {
                private readonly IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];
            }

            public partial class PollingService : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_PartialClass_AcrossFiles_ReportsDiagnostic()
    {
        var executionPart = Usings + """
            public partial class PollingService : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        var fieldPart = """
            using Microsoft.Extensions.DependencyInjection;

            public partial class PollingService
            {
                private readonly IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(
            new[] { ("/0/Test0.cs", executionPart), ("/0/Test1.cs", fieldPart) });
    }

    [Fact]
    public async Task ScopeField_PartialClass_HelperAssignmentInOtherPart_NoDiagnostic()
    {
        // The helper in the other part may reassign per iteration; cross-part sites must
        // disqualify exactly like same-part sites.
        var source = Usings + """
            public partial class PollingService
            {
                private IServiceScope _scope;
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                private void RecreateScope()
                {
                    _scope?.Dispose();
                    _scope = _scopeFactory.CreateScope();
                }
            }

            public partial class PollingService : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        RecreateScope();
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AssignedOnlyAfterLoop_NoDiagnostic()
    {
        // The only creation runs after the loop exits; nothing hoisted feeds the loop.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope?.ServiceProvider.GetRequiredService<IWorker>();
                        await Task.Delay(1000, stoppingToken);
                    }

                    _scope = _scopeFactory.CreateScope();
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_NullClearedInStopAsync_StillReportsDiagnostic()
    {
        // A teardown `_scope = null` is a clear, not a competing creation; the hoisted scope
        // still feeds every loop iteration.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }

                public override Task StopAsync(CancellationToken cancellationToken)
                {
                    _scope?.Dispose();
                    _scope = null;
                    return base.StopAsync(cancellationToken);
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_NullForgivingClearInStopAsync_StillReportsDiagnostic()
    {
        // `_scope = null!;` and `= default(T)` are teardown clears too.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }

                public override Task StopAsync(CancellationToken cancellationToken)
                {
                    _scope?.Dispose();
                    _scope = null!;
                    return base.StopAsync(cancellationToken);
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AssignedOnlyInStartedAsync_LoopInStartAsync_NoDiagnostic()
    {
        // StartedAsync runs after StartAsync completes; its creation cannot feed the
        // StartAsync loop.
        var source = Usings + """
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedLifecycleService : IHostedService
                {
                    Task StartingAsync(CancellationToken cancellationToken);
                    Task StartedAsync(CancellationToken cancellationToken);
                    Task StoppingAsync(CancellationToken cancellationToken);
                    Task StoppedAsync(CancellationToken cancellationToken);
                }
            }

            public class PollingService : IHostedLifecycleService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public async Task StartAsync(CancellationToken cancellationToken)
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var worker = _scope?.ServiceProvider.GetRequiredService<IWorker>();
                        await Task.Delay(1000, cancellationToken);
                    }
                }

                public Task StartedAsync(CancellationToken cancellationToken)
                {
                    _scope = _scopeFactory.CreateScope();
                    return Task.CompletedTask;
                }

                public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_MultipleAssignments_ReportsAtTheDominatingSite()
    {
        // The StartedAsync assignment appears first in the file but runs after the StartAsync
        // loop starts; the report must land on the assignment that actually feeds the loop.
        var source = Usings + """
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedLifecycleService : IHostedService
                {
                    Task StartingAsync(CancellationToken cancellationToken);
                    Task StartedAsync(CancellationToken cancellationToken);
                    Task StoppingAsync(CancellationToken cancellationToken);
                    Task StoppedAsync(CancellationToken cancellationToken);
                }
            }

            public class PollingService : IHostedLifecycleService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task StartedAsync(CancellationToken cancellationToken)
                {
                    _scope = _scopeFactory.CreateScope();
                    return Task.CompletedTask;
                }

                public async Task StartAsync(CancellationToken cancellationToken)
                {
                    _scope = [|_scopeFactory.CreateScope()|];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(cancellationToken);
                    }
                }

                public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_NullClearedInLoopErrorBranch_StillReportsDiagnostic()
    {
        // An error-branch `_scope = null` is a clear, not a per-iteration recreate; the
        // constructor-created scope is still hoisted.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = [|scopeFactory.CreateScope()|];

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                        catch (InvalidOperationException)
                        {
                            _scope = null;
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_NullClearedBeforeLoop_NoDiagnostic()
    {
        // The clear runs after the creation and before the loop: the created scope instance
        // never feeds the loop, so there is nothing hoisted to report.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = scopeFactory.CreateScope();

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _scope.Dispose();
                    _scope = null;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = _scope?.ServiceProvider.GetRequiredService<IWorker>();
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_ReassignedInsideLoop_NoDiagnostic()
    {
        // Dispose-and-recreate through a field still gives each iteration a fresh scope.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        _scope.Dispose();
                        _scope = _scopeFactory.CreateScope();
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }

                    _scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AssignedInHelperMethod_NoDiagnostic()
    {
        // A helper that reassigns the field may be invoked per iteration; the assignment site
        // is not provably hoisted, so stay silent.
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                private void RecreateScope()
                {
                    _scope?.Dispose();
                    _scope = _scopeFactory.CreateScope();
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        RecreateScope();
                        var worker = _scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceFieldWithUnknownRegistration_NoDiagnostic()
    {
        var source = Usings + """
            public interface IUnknownThing
            {
                Task RunAsync(CancellationToken token);
            }

            public class PollingService : BackgroundService
            {
                private readonly IUnknownThing _thing;

                public PollingService(IServiceProvider provider) => _thing = provider.GetRequiredService<IUnknownThing>();

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await _thing.RunAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeField_AllResolutionsProvenSingleton_NoDiagnostic()
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
                private readonly IServiceScope _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scope = scopeFactory.CreateScope();

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var clock = _scope.ServiceProvider.GetRequiredService<IClock>();
                        await Task.Delay(1000, stoppingToken);
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

    [Fact]
    public async Task HoistedScope_PeriodicTimerLoopWithConfigureAwait_ReportsDiagnostic()
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
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
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
    public async Task HoistedScope_WhileAwaitWaitToReadAsyncWithConfigureAwait_ReportsDiagnostic()
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
                    while (await _reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
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
    public async Task HoistedScope_AwaitForeachChannelReadAllAsyncWithConfigureAwait_ReportsDiagnostic()
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
                    await foreach (var item in _reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
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
    public async Task HoistedScope_AwaitForeachChannelReadAllAsyncWithCancellation_ReportsDiagnostic()
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
                    await foreach (var item in _reader.ReadAllAsync().WithCancellation(stoppingToken))
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
    public async Task HoistedScope_ProviderAliasLocal_UsedInsideLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    var sp = scope.ServiceProvider;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScopeField_ProviderAliasLocal_UsedInsideLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope? _scope;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _scope = [|_scopeFactory.CreateScope()|];
                    var sp = _scope.ServiceProvider;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // The alias is taken from a hoisted scope *field*: alias resolution must run after
        // field scope symbols are known (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderAlias_TakenBeforeScopeReassignment_ReportsOriginalCreation()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var sp = scope.ServiceProvider;
                    scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // The alias was taken before the local was repointed: it pins the first creation,
        // which is the provider actually used inside the loop (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderAlias_TakenBeforeScopeClear_StillReportsOriginalCreation()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var sp = scope.ServiceProvider;
                    scope = null;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // Clearing the local does not release the provider the alias still holds across
        // every iteration; the alias pins the original creation.
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderAlias_TakenBeforeFieldCreation_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope = null!;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var sp = _scope.ServiceProvider;
                    _scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // The alias was taken before the same-method field creation: it cannot hold that
        // scope's provider, so the creation must not be reported through the alias
        // (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PreLoopServiceResolution_ThroughAlias_ScopeClearedAfter_ReportsOriginalCreation()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var sp = scope.ServiceProvider;
                    var worker = sp.GetRequiredService<IWorker>();
                    scope = null;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // The pre-loop resolution pinned the creation that backed it: clearing the scope
        // local afterwards does not release the scope the hoisted worker still came from
        // (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PreLoopServiceResolution_Direct_ScopeClearedAfter_ReportsOriginalCreation()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                    scope = null;
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
    public async Task PreLoopServiceResolution_Direct_ScopeReassignedAfter_ReportsOriginalCreation()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                    scope = _scopeFactory.CreateScope();
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // The service was resolved from the first creation; the diagnostic must land there,
        // not on the later reassignment (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_NullForgivingProviderAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = [|_scopeFactory.CreateScope()|];
                    var sp = scope!.ServiceProvider;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        // `scope!.ServiceProvider` aliases the same scope local (Codex review regression).
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_ParenthesizedProviderAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var scope = [|_scopeFactory.CreateScope()|];
                    var sp = (scope).ServiceProvider;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeInsideLoop_ProviderAliasInsideLoop_NoDiagnostic()
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
                        var sp = scope.ServiceProvider;
                        var worker = sp.GetRequiredService<IWorker>();
                        await worker.DoWorkAsync(stoppingToken);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HoistedScope_DeclareThenAssignInTryFinally_ReportsDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = null;
                    try
                    {
                        scope = [|_scopeFactory.CreateScope()|];
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                    finally
                    {
                        scope?.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DeclareThenAssign_ClearedBeforeLoop_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = _scopeFactory.CreateScope();
                    scope.Dispose();
                    scope = null;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var worker = scope?.ServiceProvider.GetRequiredService<IWorker>();
                        if (worker is not null)
                        {
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                }
            }
            """;

        // The closest pre-loop write is a null clear: the created scope never feeds the loop.
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DeclareThenAssign_RecreatedInsideLoop_NoDiagnostic()
    {
        var source = Usings + """
            public class PollingService : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    IServiceScope? scope = null;
                    try
                    {
                        scope = _scopeFactory.CreateScope();
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            scope.Dispose();
                            scope = _scopeFactory.CreateScope();
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            await worker.DoWorkAsync(stoppingToken);
                        }
                    }
                    finally
                    {
                        scope?.Dispose();
                    }
                }
            }
            """;

        // Dispose-and-recreate inside the loop: each iteration gets a fresh scope.
        await AnalyzerVerifier<DI024_HostedServiceScopePerIterationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
