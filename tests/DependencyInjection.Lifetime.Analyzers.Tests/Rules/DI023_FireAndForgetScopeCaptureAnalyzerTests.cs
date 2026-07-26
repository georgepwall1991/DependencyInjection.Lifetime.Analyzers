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
}
