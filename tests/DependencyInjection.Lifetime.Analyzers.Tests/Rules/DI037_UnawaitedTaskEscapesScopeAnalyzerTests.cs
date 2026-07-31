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
}
