using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI005 code fix: Use CreateAsyncScope() in async methods.
/// </summary>
public class DI005_AsyncScopeRequiredCodeFixTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_ReplacesCreateScopeWithCreateAsyncScope_UsingVarDeclaration()
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

        var fixedSource = Usings + """
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

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 27, 15, 54)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_ReplacesCreateScopeWithCreateAsyncScope_InAsyncLambda()
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

        var fixedSource = Usings + """
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
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        await Task.Delay(100);
                    };
                }
            }
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(17, 31, 17, 58)
            .WithArguments("");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_ReplacesCreateScopeWithCreateAsyncScope_UsingStatement()
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
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        await Task.Delay(100);
                    }
                }
            }
            """;

        var fixedSource = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task DoWorkAsync()
                {
                    await using (var scope = _scopeFactory.CreateAsyncScope())
                    {
                        await Task.Delay(100);
                    }
                }
            }
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 28, 15, 55)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_ReplacesCreateScopeWithCreateAsyncScope_MethodReturnsGenericTask()
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

        var fixedSource = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task<string> GetDataAsync()
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await Task.Delay(100);
                    return "data";
                }
            }
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 27, 15, 54)
            .WithArguments("GetDataAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
