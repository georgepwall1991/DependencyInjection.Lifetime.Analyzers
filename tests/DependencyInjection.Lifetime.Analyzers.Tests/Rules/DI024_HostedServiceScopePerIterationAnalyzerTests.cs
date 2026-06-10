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
