using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI023_FireAndForgetScopeCaptureAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IMyService
        {
            Task DoWorkAsync();
            void DoWork();
            int Id { get; }
        }

        """;

    [Fact]
    public async Task ResolvedService_CapturedByDiscardedTaskRun_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        _ = {|DI023:Task.Run(async () => await service.DoWorkAsync())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Scope_CapturedByStatementTaskRun_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        {|DI023:Task.Run(() => scope.ServiceProvider.GetRequiredService<IMyService>().DoWork())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ProviderAlias_CapturedByTaskFactoryStartNew_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var provider = scope.ServiceProvider;
                        {|DI023:Task.Factory.StartNew(() => provider.GetRequiredService<IMyService>().DoWork())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ResolvedService_CapturedInsideUsingStatement_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                            {|DI023:Task.Run(() => service.DoWork())|};
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AwaitedTaskRun_NoDiagnostic()
    {
        // Awaiting keeps the frame — and the scope — alive until the work completes.
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public async Task HandleAsync()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        await Task.Run(() => service.DoWork());
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task StoredTaskAwaitedLater_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public async Task HandleAsync()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        var work = Task.Run(() => service.DoWork());
                        await work;
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SynchronouslyWaitedTaskRun_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        Task.Run(() => service.DoWork()).Wait();
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BackgroundWorkCreatingItsOwnScope_NoDiagnostic()
    {
        // The correct pattern: the background work owns a scope of its own.
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<IMyService>().DoWork();

                        _ = Task.Run(() =>
                        {
                            using var backgroundScope = _scopeFactory.CreateScope();
                            backgroundScope.ServiceProvider.GetRequiredService<IMyService>().DoWork();
                        });
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UndisposedScopeCapture_NoDiagnostic()
    {
        // Without a using there is no proven disposal point in this method; that is DI001's
        // finding, not a use-after-dispose proof.
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle()
                    {
                        var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        _ = Task.Run(() => service.DoWork());
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BackgroundWorkCapturingUnrelatedLocal_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Handle(string message)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        scope.ServiceProvider.GetRequiredService<IMyService>().DoWork();

                        var copy = message;
                        _ = Task.Run(() => Console.WriteLine(copy));
                    }
                }
                """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ScopeDerivedValueSnapshot_NoDiagnostic()
    {
        // A value computed FROM the scope is not the scope. An int cannot keep a disposed scope
        // reachable, so capturing it is harmless.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var id = scope.GetHashCode();
                    _ = Task.Run(() => Console.WriteLine(id));
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NameOfScopedLocal_NoDiagnostic()
    {
        // nameof binds to the symbol but captures nothing at runtime.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    _ = Task.Run(() => Console.WriteLine(nameof(service)));
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedLocalReplacedBeforeCapture_NoDiagnostic()
    {
        // The captured local no longer holds anything from the scope.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    service = _singleton;
                    _ = Task.Run(() => service.DoWork());
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceResolvedThroughProviderAlias_ReportsDiagnostic()
    {
        // Two hops: scope -> provider -> service. Both hops die with the scope.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var provider = scope.ServiceProvider;
                    var service = provider.GetRequiredService<IMyService>();
                    _ = {|DI023:Task.Run(() => service.DoWork())|};
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MethodGroupOnScopedService_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _ = {|DI023:Task.Run(service.DoWork)|};
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetAwaiterWithoutGetResult_ReportsDiagnostic()
    {
        // Fetching the awaiter and throwing it away waits for nothing.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    {|DI023:Task.Run(() => service.DoWork())|}.GetAwaiter();
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetAwaiterGetResult_NoDiagnostic()
    {
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    Task.Run(() => service.DoWork()).GetAwaiter().GetResult();
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
    [Fact]
    public async Task ScopeDerivedValueWidenedToObject_NoDiagnostic()
    {
        // The declared type is object, but the value is a boxed int — it holds no scope graph.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    object hash = scope.GetHashCode();
                    _ = Task.Run(() => Console.WriteLine(hash));
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopeDerivedValueAsStateArgument_NoDiagnostic()
    {
        // The state argument is evaluated synchronously and carries only a boxed int.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    Task.Factory.StartNew(state => Console.WriteLine(state), scope.GetHashCode());
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedLocalReassignedAfterCapture_ReportsDiagnostic()
    {
        // The reassignment happens after the background work already captured the scoped value.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _ = {|DI023:Task.Run(() => service.DoWork())|};
                    service = _singleton;
                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalReplacementBeforeCapture_ReportsDiagnostic()
    {
        // The replacement only happens on one branch, so the scoped value can still reach the
        // background work. Suppressing here would hide a real defect.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle(bool useSingleton)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (useSingleton)
                    {
                        service = _singleton;
                    }

                    _ = {|DI023:Task.Run(() => service.DoWork())|};
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedServicePropertyAsStateArgument_NoDiagnostic()
    {
        // service.Id is an int read synchronously; the started work holds no scoped object.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    Task.Factory.StartNew(state => Console.WriteLine(state), service.Id);
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedLocalAssignedInsideArgumentList_NoDiagnostic()
    {
        // The identifier is the assignment TARGET; what the task receives is the replacement.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    Task.Factory.StartNew(state => Console.WriteLine(state), service = _singleton);
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ReplacementInsideConditionalAccess_ReportsDiagnostic()
    {
        // receiver?.Use(...) may never evaluate its argument, so the replacement is not definite.
        var source = Usings + """
            public class Helper
            {
                public void Use(IMyService service) { }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;
                private readonly Helper _helper;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton, Helper helper)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                    _helper = helper;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _helper?.Use(service = _singleton);
                    _ = {|DI023:Task.Run(() => service.DoWork())|};
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ReplacementDominatingCaptureInSameBranch_NoDiagnostic()
    {
        // Both statements live in the same branch, and the replacement runs first.
        var source = Usings + """
            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle(bool flag)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();

                    if (flag)
                    {
                        service = _singleton;
                        _ = Task.Run(() => service.DoWork());
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedServiceOnAssignmentTargetPath_NoDiagnostic()
    {
        // service is read synchronously to reach Child; only the replacement becomes task state.
        var source = Usings + """
            public class Node
            {
                public IMyService Child { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IMyService _singleton;

                public MyClass(IServiceScopeFactory scopeFactory, IMyService singleton)
                {
                    _scopeFactory = scopeFactory;
                    _singleton = singleton;
                }

                public void Handle()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var node = scope.ServiceProvider.GetRequiredService<Node>();
                    Task.Factory.StartNew(state => Console.WriteLine(state), node.Child = _singleton);
                }
            }
            """;

        await AnalyzerVerifier<DI023_FireAndForgetScopeCaptureAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
