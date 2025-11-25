using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI005_AsyncDisposalAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task CreateScope_InAsyncMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task DoWorkAsync()
                {
                    using var scope = _scopeFactory.CreateScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 27, 15, 54)
                .WithArguments("DoWorkAsync"));
    }

    [Fact]
    public async Task CreateScope_InAsyncMethodReturningTask_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task<string> GetDataAsync()
                {
                    using var scope = _scopeFactory.CreateScope();
                    await Task.Delay(100);
                    return "data";
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 27, 15, 54)
                .WithArguments("GetDataAsync"));
    }

    [Fact]
    public async Task CreateScope_InAsyncLambda_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void SetupCallback()
                {
                    Func<Task> callback = async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await Task.Delay(100);
                    };
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(17, 31, 17, 58)
                .WithArguments(""));
    }

    [Fact]
    public async Task CreateScope_InAsyncMethodReturningValueTask_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async ValueTask DoWorkAsync()
                {
                    using var scope = _scopeFactory.CreateScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 27, 15, 54)
                .WithArguments("DoWorkAsync"));
    }

    [Fact]
    public async Task CreateScope_InAsyncMethodReturningValueTaskOfT_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async ValueTask<int> GetCountAsync()
                {
                    using var scope = _scopeFactory.CreateScope();
                    await Task.Delay(100);
                    return 42;
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 27, 15, 54)
                .WithArguments("GetCountAsync"));
    }

    [Fact]
    public async Task CreateScope_InAsyncLocalFunction_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork()
                {
                    async Task ProcessAsync()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await Task.Delay(100);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(17, 31, 17, 58)
                .WithArguments("ProcessAsync"));
    }

    #endregion

    #region Should Not Report Diagnostic (False Positives)

    [Fact]
    public async Task CreateAsyncScope_InAsyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task DoWorkAsync()
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InSyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork()
                {
                    using var scope = _scopeFactory.CreateScope();
                    // synchronous work
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InTaskReturningMethod_WithoutAsync_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Task DoWorkAsync()
                {
                    using var scope = _scopeFactory.CreateScope();
                    // synchronous work, returns completed task
                    return Task.CompletedTask;
                }
            }
            """;

        // This is a sync method that returns Task - no async keyword, so sync disposal is OK
        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
