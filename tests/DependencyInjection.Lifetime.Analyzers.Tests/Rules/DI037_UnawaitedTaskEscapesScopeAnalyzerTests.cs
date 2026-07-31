using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI037_UnawaitedTaskEscapesScopeAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IWorker
        {
            Task RunAsync();

            Task<int> CountAsync();

            ValueTask SaveAsync();

            void Fire();
        }

        """;

    [Fact]
    public async Task TaskReturnedFromUsingScope_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        return {|DI037:worker.RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskDiscardedInsideUsingBlock_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            _ = {|DI037:worker.RunAsync()|};
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskCollectedAndAwaitedAfterScopeEnds_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        var pending = new List<Task>();

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            pending.Add({|DI037:worker.RunAsync()|});
                        }

                        await Task.WhenAll(pending);
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskStoredInFieldFromUsingScope_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    private Task _pending = Task.CompletedTask;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        _pending = {|DI037:worker.RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ValueTaskReturnedFromUsingScope_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public ValueTask Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        return {|DI037:worker.SaveAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskFromChainedResolutionReturned_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        return {|DI037:scope.ServiceProvider.GetRequiredService<IWorker>().RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskAwaitedInsideScope_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskAwaitedThroughConfigureAwait_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.RunAsync().ConfigureAwait(false);
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskAwaitedInsideScopeThroughLocal_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        var pending = worker.RunAsync();
                        await pending;
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskBlockedOnInsideScope_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        worker.RunAsync().GetAwaiter().GetResult();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceNotResolvedFromScope_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    private readonly IWorker _worker;

                    public Dispatcher(IServiceScopeFactory factory, IWorker worker)
                    {
                        _factory = factory;
                        _worker = worker;
                    }

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        return _worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ScopeNotDisposedByThisBody_NoDiagnostic()
    {
        // A scope with no `using` has no proven disposal point here — that is DI001's finding,
        // and the task may well outlive this method legitimately.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        return worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SynchronousCallOnScopedService_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        worker.Fire();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskStartedInsideNestedLambda_NoDiagnostic()
    {
        // A delegate runs when its consumer chooses, and background work started with Task.Run is
        // DI023's finding rather than this rule's.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        Action start = () => worker.RunAsync();
                        start();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskAwaitedAfterAsyncScopeInSameUsingRegion_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        await using var scope = _factory.CreateAsyncScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await worker.CountAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskAwaitedThroughWhenAllInsideScope_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        await Task.WhenAll(worker.RunAsync(), worker.RunAsync());
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskCollectedAndAwaitedInsideSameScope_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            var pending = new List<Task> { worker.RunAsync() };
                            await Task.WhenAll(pending);
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskStoredInOuterLocalButAwaitedInsideScope_NoDiagnostic()
    {
        // Codex round 1: the local is declared outside the using block, but the await happens
        // inside it, so the scope outlives the work.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        Task pending;

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            pending = worker.RunAsync();
                            await pending;
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SingletonResolvedThroughScope_NoDiagnostic()
    {
        // Codex round 1: a singleton comes from the root provider, which this scope's disposal
        // never touches.
        var source =
            Usings
            + """
                public class Worker : IWorker
                {
                    public Task RunAsync() => Task.CompletedTask;

                    public Task<int> CountAsync() => Task.FromResult(0);

                    public ValueTask SaveAsync() => default;

                    public void Fire() { }
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, Worker>();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        return worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ScopedServiceRegisteredAsScoped_ReportsDiagnostic()
    {
        // The same source with a scoped registration still reports: that instance dies with the
        // scope.
        var source =
            Usings
            + """
                public class Worker : IWorker
                {
                    public Task RunAsync() => Task.CompletedTask;

                    public Task<int> CountAsync() => Task.FromResult(0);

                    public ValueTask SaveAsync() => default;

                    public void Fire() { }
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<IWorker, Worker>();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        return {|DI037:worker.RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskStoredThenWaitedOnInsideScope_NoDiagnostic()
    {
        // Codex round 2: `Task.WaitAll` waits on what it is handed rather than on a receiver, so
        // the work is finished before the scope ends.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    private Task _pending = Task.CompletedTask;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                        _pending = worker.RunAsync();
                        Task.WaitAll(_pending);
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MethodReturningCompletedTask_NoDiagnostic()
    {
        // Codex round 2: a body made of nothing but completed-task returns starts no work that
        // could outlive the scope.
        var source =
            Usings
            + """
                public sealed class SyncWorker
                {
                    public Task RunAsync() => Task.CompletedTask;
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<SyncWorker>();
                        worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MethodWithRealAsyncBody_ReportsDiagnostic()
    {
        // A body that actually awaits keeps running past the scope, so the concrete receiver is
        // still reported.
        var source =
            Usings
            + """
                public sealed class RealWorker
                {
                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<RealWorker>();
                        {|DI037:worker.RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }
}
