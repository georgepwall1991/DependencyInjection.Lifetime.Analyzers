using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
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

    [Fact]
    public async Task IServiceProvider_CreateScope_InAsyncMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceProvider _provider;

                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public async Task DoWorkAsync()
                {
                    using var scope = _provider.CreateScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 27, 15, 50)
                .WithArguments("DoWorkAsync"));
    }

    [Fact]
    public async Task CreateScope_PassedToMethod_InAsyncMethod_ReportsDiagnostic()
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
                    UseScope(_scopeFactory.CreateScope());
                    await Task.Delay(100);
                }

                private void UseScope(IServiceScope scope) { }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 18, 15, 45)
                .WithArguments("DoWorkAsync"));
    }

    [Fact]
    public async Task CreateScope_WithExplicitDispose_InAsyncMethod_ReportsDiagnostic()
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
                    var scope = _scopeFactory.CreateScope();
                    await Task.Delay(100);
                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(15, 21, 15, 48)
                .WithArguments("DoWorkAsync"));
    }

    [Fact]
    public async Task CreateScope_ReturnedFromAsyncMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task<IServiceScope> CreateScopeAsync()
                {
                    await Task.Delay(100);
                    return _scopeFactory.CreateScope();
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithSpan(16, 16, 16, 43)
                .WithArguments("CreateScopeAsync"));
    }

    [Fact]
    public async Task CreateScope_InAsyncAnonymousMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Setup()
                {
                    Func<Task> f = async delegate
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
    public async Task CreateScope_InTopLevelAsyncStatements_ReportsDiagnostic()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = {|#0:provider.CreateScope()|};
            await Task.Delay(100);
            """;

        var test = CreateTopLevelTest(source);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithLocation(0)
                .WithArguments("top-level statements"));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreateScope_InTopLevelAwaitUsingStatements_ReportsDiagnostic()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = {|#0:provider.CreateScope()|};
            await using var resource = new AsyncResource();

            public sealed class AsyncResource : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }
            """;

        var test = CreateTopLevelTest(source);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
                .WithLocation(0)
                .WithArguments("top-level statements"));

        await test.RunAsync();
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

    [Fact]
    public async Task IServiceProvider_CreateScope_InSyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceProvider _provider;

                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void DoWork()
                {
                    using var scope = _provider.CreateScope();
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InSyncLambdaInsideAsyncMethod_NoDiagnostic()
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
                    Func<IServiceScope> f = () => _scopeFactory.CreateScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InSyncLocalFunctionInsideAsyncMethod_NoDiagnostic()
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
                    IServiceScope MakeScope() => _scopeFactory.CreateScope();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InTopLevelSyncStatements_NoDiagnostic()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            Console.WriteLine(scope.ServiceProvider);
            """;

        await CreateTopLevelTest(source).RunAsync();
    }

    [Fact]
    public async Task CreateScope_InTopLevelStatementsWithNestedAsyncLocalFunction_NoDiagnostic()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            async Task LaterAsync()
            {
                await Task.Delay(100);
            }
            """;

        await CreateTopLevelTest(source).RunAsync();
    }

    [Fact]
    public async Task CreateScope_InTopLevelStatementsWithNestedAsyncLambda_NoDiagnostic()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            Func<Task> laterAsync = async () => await Task.Delay(100);
            """;

        await CreateTopLevelTest(source).RunAsync();
    }

    #endregion

    private static CSharpAnalyzerTest<DI005_AsyncDisposalAnalyzer, DefaultVerifier> CreateTopLevelTest(string source)
    {
        var test = new CSharpAnalyzerTest<DI005_AsyncDisposalAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.ReferenceAssembliesWithDi60
        };

        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        return test;
    }
}
