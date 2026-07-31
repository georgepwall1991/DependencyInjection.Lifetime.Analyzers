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
                public class Worker : IWorker, IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                    }

                    public Task<int> CountAsync() => Task.FromResult(0);

                    public ValueTask SaveAsync() => default;

                    public void Fire() { }

                    public void Dispose() { }
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
                public sealed class RealWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                    }

                    public void Dispose() { }
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

    [Fact]
    public async Task AsyncMethodWithNothingToAwait_NoDiagnostic()
    {
        // Codex round 3: an async body with no await runs straight through and hands back a task
        // that has already finished.
        var source =
            Usings
            + """
                public sealed class EmptyWorker
                {
                    public async Task RunAsync()
                    {
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<EmptyWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CompletionPolledBeforeScopeEnds_NoDiagnostic()
    {
        // Codex round 3: polling `IsCompleted` before leaving the scope is waiting for the work,
        // not forgetting it.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        Task pending;

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            pending = worker.RunAsync();

                            while (!pending.IsCompleted)
                            {
                                Thread.Yield();
                            }
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WorkerBuiltFromScopedValue_NoDiagnostic()
    {
        // Codex round 4: an id copied out of a scoped service is a string, and the worker built
        // from that string belongs to nobody but itself.
        var source =
            Usings
            + """
                public sealed class RequestContext
                {
                    public string Id => "request";
                }

                public sealed class DetachedWorker
                {
                    public DetachedWorker(string id) { }

                    public async Task RunAsync() => await Task.Delay(10);
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var id = scope.ServiceProvider.GetRequiredService<RequestContext>().Id;
                        var detached = new DetachedWorker(id);
                        detached.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TaskHandedToArbitraryMethod_NoDiagnostic()
    {
        // Codex round 4: handing a task to a method proves nothing — that method may well wait
        // on it, as this one does.
        var source =
            Usings
            + """
                public sealed class Joiner
                {
                    public void Join(Task task) => task.GetAwaiter().GetResult();
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        var joiner = new Joiner();

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            joiner.Join(worker.RunAsync());
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task StoredTaskDrainedByHelperInsideScope_NoDiagnostic()
    {
        // Codex round 5: the stored task is handed to a helper of the codebase's own, which
        // blocks on it before the scope ends.
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
                        _pending = scope.ServiceProvider.GetRequiredService<IWorker>().RunAsync();
                        Drain(_pending);
                    }

                    private static void Drain(Task task) => task.GetAwaiter().GetResult();
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceWithNothingToDispose_NoDiagnostic()
    {
        // Codex round 6: neither the service nor anything it is built from is disposable, so the
        // scope's disposal tears nothing down that the work could still be using.
        var source =
            Usings
            + """
                public sealed class PlainWorker
                {
                    public Task RunAsync() => Task.Delay(10);
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<PlainWorker>();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            scope.ServiceProvider.GetRequiredService<PlainWorker>().RunAsync();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceWithDisposableDependency_ReportsDiagnostic()
    {
        // The service itself is not disposable, but what it was built from is, and that is what
        // the scope disposes underneath the running work.
        var source =
            Usings
            + """
                public sealed class Connection : IDisposable
                {
                    public void Dispose() { }
                }

                public sealed class PlainWorker
                {
                    private readonly Connection _connection;

                    public PlainWorker(Connection connection) => _connection = connection;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _connection.ToString();
                    }
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<Connection>();
                        services.AddScoped<PlainWorker>();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            {|DI037:scope.ServiceProvider.GetRequiredService<PlainWorker>().RunAsync()|};
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InterfaceImplementationReturnsCompletedTask_NoDiagnostic()
    {
        // Codex round 6: the interface says nothing about what runs; the registered
        // implementation hands back a task that has already finished.
        var source =
            Usings
            + """
                public sealed class InstantWorker : IWorker, IDisposable
                {
                    public Task RunAsync() => Task.CompletedTask;

                    public Task<int> CountAsync() => Task.FromResult(0);

                    public ValueTask SaveAsync() => default;

                    public void Fire() { }

                    public void Dispose() { }
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<IWorker, InstantWorker>();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            scope.ServiceProvider.GetRequiredService<IWorker>().RunAsync();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task StoredTaskAwaitedThroughAlias_NoDiagnostic()
    {
        // Codex round 7: the stored task is read into an alias and awaited through that, so the
        // scope waits for the work after all.
        var source =
            Usings
            + """
                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    private Task _pending = Task.CompletedTask;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            _pending = worker.RunAsync();
                            var alias = _pending;
                            await alias;
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MethodReturningEmptyWhenAll_NoDiagnostic()
    {
        // Codex round 7: `Task.WhenAll()` over nothing is finished before it is handed back.
        var source =
            Usings
            + """
                public sealed class EmptyFanOutWorker : IDisposable
                {
                    public Task RunAsync() => Task.WhenAll();

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<EmptyFanOutWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WorkIndependentOfTheService_NoDiagnostic()
    {
        // Codex round 8: the task hands back a timer rather than a piece of this instance, so
        // disposing the service cannot touch it.
        var source =
            Usings
            + """
                public sealed class PausingWorker : IDisposable
                {
                    public Task PauseAsync() => Task.Delay(10);

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        return scope.ServiceProvider.GetRequiredService<PausingWorker>().PauseAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceKeepsTheHandleItHandsBack_NoDiagnostic()
    {
        // Codex round 8: the service stores the task it starts, so it has a join of its own in
        // mind and the caller discarding the task is not the end of the story.
        var source =
            Usings
            + """
                public sealed class JoiningWorker : IDisposable
                {
                    private Task? _running;

                    private int _count;

                    public Task RunAsync() => _running = Task.Run(() => _count++);

                    public void Join() => _running!.GetAwaiter().GetResult();

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<JoiningWorker>();
                        worker.RunAsync();
                        worker.Join();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WorkUsingTheServiceItself_ReportsDiagnostic()
    {
        // Work that reads the service's own state through the delegate it starts is exactly what
        // the scope tears down.
        var source =
            Usings
            + """
                public sealed class StatefulWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public Task Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        return {|DI037:scope.ServiceProvider.GetRequiredService<StatefulWorker>().RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceKeepsTheHandleThroughAHelper_NoDiagnostic()
    {
        // Codex round 9: the keeping happens one helper down, and the service joins what it kept
        // before the scope ends.
        var source =
            Usings
            + """
                public sealed class RememberingWorker : IDisposable
                {
                    private Task _pending = Task.CompletedTask;

                    public Task RunAsync() => Remember(RunCoreAsync());

                    public void Join() => _pending.GetAwaiter().GetResult();

                    public void Dispose() { }

                    private Task Remember(Task task) => _pending = task;

                    private async Task RunCoreAsync() => await Task.Delay(10);
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<RememberingWorker>();
                        worker.RunAsync();
                        worker.Join();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InstanceStateReadBeforeTheTaskStarts_NoDiagnostic()
    {
        // Codex round 10: the field is read while the method still runs, and what it hands back
        // is an independent timer.
        var source =
            Usings
            + """
                public sealed class DelayWorker : IDisposable
                {
                    private readonly int _delayMs = 10;

                    public Task RunAsync() => Task.Delay(_delayMs);

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<DelayWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DisposalWaitsForTheWork_NoDiagnostic()
    {
        // Codex round 10: the service blocks in Dispose until its work finishes, so the scope
        // tearing it down is when the work is collected rather than orphaned.
        var source =
            Usings
            + """
                public sealed class LatchedWorker : IDisposable
                {
                    private readonly CountdownEvent _done = new CountdownEvent(1);

                    private int _count;

                    public async Task RunAsync()
                    {
                        _done.AddCount();

                        try
                        {
                            await Task.Delay(10);
                            _count++;
                        }
                        finally
                        {
                            _done.Signal();
                        }
                    }

                    public void Dispose()
                    {
                        _done.Signal();
                        _done.Wait();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<LatchedWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DelegateCapturingTheServiceRunsLater_ReportsDiagnostic()
    {
        // A non-async method still reports when the delegate it leaves behind reaches back into
        // the instance.
        var source =
            Usings
            + """
                public sealed class DeferredWorker : IDisposable
                {
                    private int _count;

                    public Task RunAsync() => Task.Run(() => _count++);

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = {|DI037:scope.ServiceProvider.GetRequiredService<DeferredWorker>().RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ValueReadOffAScopedServiceInline_NoDiagnostic()
    {
        // Codex round 11: reaching through a scoped service for a string hands back a string, and
        // the worker built from it carries nothing of the scope.
        var source =
            Usings
            + """
                public sealed class ScopedData : IDisposable
                {
                    public string Id => "id";

                    public void Dispose() { }
                }

                public sealed class IndependentWorker
                {
                    private readonly string _id;

                    public IndependentWorker(string id) => _id = id;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _id.ToString();
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var data = scope.ServiceProvider.GetRequiredService<ScopedData>();
                        var worker = new IndependentWorker(data.Id);
                        worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InstanceStateSnapshotBeforeFirstAwait_NoDiagnostic()
    {
        // Codex round 11: the field is copied while the method still runs, and only the copy is
        // used after it suspends.
        var source =
            Usings
            + """
                public sealed class SnapshotWorker : IDisposable
                {
                    private readonly int _value = 1;

                    public async Task RunAsync()
                    {
                        var snapshot = _value;
                        await Task.Delay(10);
                        Console.WriteLine(snapshot);
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<SnapshotWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InstanceStateTouchedAcrossAnAwaitingLoop_ReportsDiagnostic()
    {
        // A loop brings the instance back round after a suspension, so the work is still using
        // the service when the scope ends.
        var source =
            Usings
            + """
                public sealed class LoopingWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        for (var index = 0; index < 3; index++)
                        {
                            _count++;
                            await Task.Delay(10);
                        }
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = {|DI037:scope.ServiceProvider.GetRequiredService<LoopingWorker>().RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AsyncBodyAwaitingOnlyCompletedTasks_NoDiagnostic()
    {
        // Codex round 12: awaiting a task that has already finished never gives up control, so
        // the body runs straight through.
        var source =
            Usings
            + """
                public sealed class InstantAsyncWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.CompletedTask;
                        _count++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<InstantAsyncWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task LaterCallOnTheServiceJoinsTheWork_NoDiagnostic()
    {
        // Codex round 12: the service is used as a pair of calls — start, then join — and the
        // join happens before the scope ends.
        var source =
            Usings
            + """
                public sealed class SignallingWorker : IDisposable
                {
                    private readonly ManualResetEventSlim _done = new ManualResetEventSlim();

                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                        _done.Set();
                    }

                    public void Join() => _done.Wait();

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<SignallingWorker>();
                        worker.RunAsync();
                        worker.Join();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AwaitOfACompletedTaskBehindALocal_NoDiagnostic()
    {
        // Codex round 13: the same nothing, one name later — awaiting it still never gives up
        // control.
        var source =
            Usings
            + """
                public sealed class NamedInstantWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        var done = Task.CompletedTask;
                        await done;
                        _count++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<NamedInstantWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DisposalAwaitingThePendingWork_NoDiagnostic()
    {
        // Codex round 13: the disposal awaits the very task that escaped, so it cannot complete
        // before the work does.
        var source =
            Usings
            + """
                public static class Tracker
                {
                    public static Task Pending = Task.CompletedTask;
                }

                public sealed class TrackedWorker : IAsyncDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                    }

                    public async ValueTask DisposeAsync() => await Tracker.Pending;
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public async Task Dispatch()
                    {
                        await using var scope = _factory.CreateAsyncScope();
                        Tracker.Pending = scope.ServiceProvider
                            .GetRequiredService<TrackedWorker>()
                            .RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AwaitOfACompletedTaskThroughConfigureAwait_NoDiagnostic()
    {
        // Codex round 14: the wrapper hands back the same finished work, so awaiting it still
        // never gives up control.
        var source =
            Usings
            + """
                public sealed class ConfiguredInstantWorker : IDisposable
                {
                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        _count++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider
                            .GetRequiredService<ConfiguredInstantWorker>()
                            .RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DisposalJoiningThroughAHelper_NoDiagnostic()
    {
        // Codex round 14: the wait is a helper down from Dispose, and it still cannot finish
        // before the work does.
        var source =
            Usings
            + """
                public sealed class DrainingWorker : IDisposable
                {
                    private readonly ManualResetEventSlim _done = new ManualResetEventSlim();

                    private int _count;

                    public async Task RunAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                        _done.Set();
                    }

                    public void Dispose() => Drain();

                    private void Drain() => _done.Wait();
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<DrainingWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectorThatWaitsRatherThanStores_NoDiagnostic()
    {
        // Codex round 15: a call named Add can turn out to wait once its body is visible.
        var source =
            Usings
            + """
                public sealed class JoiningBag
                {
                    public void Add(Task task) => task.GetAwaiter().GetResult();
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        var joiner = new JoiningBag();

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            joiner.Add(worker.RunAsync());
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PropertySetterThatWaits_NoDiagnostic()
    {
        // Codex round 15: a setter is a method like any other, and this one blocks before the
        // assignment returns.
        var source =
            Usings
            + """
                public sealed class JoiningSlot
                {
                    private Task _pending = Task.CompletedTask;

                    public Task Pending
                    {
                        get => _pending;
                        set
                        {
                            _pending = value;
                            value.GetAwaiter().GetResult();
                        }
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        var holder = new JoiningSlot();

                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                            holder.Pending = worker.RunAsync();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AwaitOfWhenAllOverFinishedWork_NoDiagnostic()
    {
        // Codex round 16: WhenAll over work that has already finished is finished work.
        var source =
            Usings
            + """
                public sealed class FanInWorker : IDisposable
                {
                    private int _state;

                    public async Task RunAsync()
                    {
                        await Task.WhenAll(Task.CompletedTask);
                        _state++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var worker = scope.ServiceProvider.GetRequiredService<FanInWorker>();
                        _ = worker.RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task LocalFunctionCalledInPlace_NoDiagnostic()
    {
        // Codex round 16: a local function nobody keeps a delegate to runs where it is called,
        // which is before the task is handed back.
        var source =
            Usings
            + """
                public sealed class InlineWorker : IDisposable
                {
                    private int _state;

                    public Task RunAsync()
                    {
                        void Read() => Console.WriteLine(_state);

                        Read();
                        return Task.Delay(10);
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<InlineWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task LocalFunctionHandedOffAsADelegate_ReportsDiagnostic()
    {
        // Once something takes the local function as a value, it can run whenever that value is
        // called — including after the scope has gone.
        var source =
            Usings
            + """
                public sealed class DeferredInlineWorker : IDisposable
                {
                    private int _state;

                    public Task RunAsync()
                    {
                        void Bump() => _state++;

                        return Task.Run(Bump);
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = {|DI037:scope.ServiceProvider.GetRequiredService<DeferredInlineWorker>().RunAsync()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InstanceStateHandedToTheFirstAwait_NoDiagnostic()
    {
        // Codex round 17: the fields are read to build the call's arguments, which happens before
        // the await gives up control.
        var source =
            Usings
            + """
                public sealed class Exporter : IDisposable
                {
                    private readonly string _path = "out.txt";

                    private readonly string _payload = "{}";

                    public async Task SaveAsync()
                    {
                        await System.IO.File.WriteAllTextAsync(_path, _payload);
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        _ = scope.ServiceProvider.GetRequiredService<Exporter>().SaveAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FreshObjectBuiltByAScopedService_NoDiagnostic()
    {
        // Codex round 17: the snapshot is a new object the exporter built, not the exporter, so
        // the scope has no claim on it.
        var source =
            Usings
            + """
                public sealed class Snapshot
                {
                    private readonly string _json = "{}";

                    public async Task WriteToAsync(string path)
                    {
                        await Task.Delay(10);
                        await System.IO.File.WriteAllTextAsync(path, _json);
                    }
                }

                public sealed class ReportExporter : IDisposable
                {
                    public Snapshot CreateSnapshot() => new Snapshot();

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        var snapshot = scope.ServiceProvider
                            .GetRequiredService<ReportExporter>()
                            .CreateSnapshot();
                        _ = snapshot.WriteToAsync("out.txt");
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FactoryRegistrationNamesTheImplementation_NoDiagnostic()
    {
        // Codex round 18: a factory that does nothing but construct names its implementation as
        // plainly as a type argument would, and this one has nothing to dispose.
        var source =
            Usings
            + """
                public interface IReportQueue
                {
                    Task FlushAsync();
                }

                public sealed class ReportQueue : IReportQueue
                {
                    private int _count;

                    public async Task FlushAsync()
                    {
                        await Task.Delay(10);
                        _count++;
                    }
                }

                public class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<IReportQueue>(provider => new ReportQueue());
                    }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<IReportQueue>().FlushAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ParameterShadowingAField_NoDiagnostic()
    {
        // Codex round 18: the symbol decides, not the spelling — the name read after the await is
        // the parameter, not the field the service disposes.
        var source =
            Usings
            + """
                public sealed class ShadowWorker : IDisposable
                {
                    private readonly System.IO.Stream output = System.IO.Stream.Null;

                    public async Task RunAsync(string output)
                    {
                        await Task.Delay(10);
                        Console.WriteLine(output);
                    }

                    public void Dispose() => output.Dispose();
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<ShadowWorker>().RunAsync("done");
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AwaitOfAZeroLengthDelay_NoDiagnostic()
    {
        // Codex round 19: `Task.Delay(0)` is finished before it is handed back.
        var source =
            Usings
            + """
                public sealed class NoDelayWorker : IDisposable
                {
                    private int _processed;

                    public async Task RunAsync()
                    {
                        await Task.Delay(0);
                        _processed++;
                    }

                    public void Dispose() { }
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using var scope = _factory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<NoDelayWorker>().RunAsync();
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task JoinThroughACompletionProperty_NoDiagnostic()
    {
        // Codex round 19: the join is written through a property of the service rather than a
        // method of its own, and it still happens before the scope ends.
        var source =
            Usings
            + """
                public interface IJobWorker
                {
                    Task Completion { get; }

                    Task RunAsync();
                }

                public class Dispatcher
                {
                    private readonly IServiceScopeFactory _factory;

                    public Dispatcher(IServiceScopeFactory factory) => _factory = factory;

                    public void Dispatch()
                    {
                        using (var scope = _factory.CreateScope())
                        {
                            var worker = scope.ServiceProvider.GetRequiredService<IJobWorker>();
                            worker.RunAsync();
                            worker.Completion.GetAwaiter().GetResult();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI037_UnawaitedTaskEscapesScopeAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }
}
