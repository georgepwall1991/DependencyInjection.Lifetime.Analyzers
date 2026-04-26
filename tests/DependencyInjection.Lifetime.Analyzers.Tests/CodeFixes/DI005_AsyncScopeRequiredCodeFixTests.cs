using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
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

    [Fact]
    public async Task CodeFix_NotOffered_ForPlainAssignmentOutsideUsing()
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

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 21, 15, 48)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI005_UseCreateAsyncScope");
    }

    [Fact]
    public async Task CodeFix_ReplacesProviderCreateScopeWithCreateAsyncScope_UsingVarDeclaration()
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

        var fixedSource = Usings + """
            public class MyService
            {
                private readonly IServiceProvider _provider;

                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public async Task DoWorkAsync()
                {
                    await using var scope = _provider.CreateAsyncScope();
                    await Task.Delay(100);
                }
            }
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 27, 15, 50)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_ReplacesProviderCreateScopeWithCreateAsyncScope_UsingStatement()
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
                    using (var scope = _provider.CreateScope())
                    {
                        await Task.Delay(100);
                    }
                }
            }
            """;

        var fixedSource = Usings + """
            public class MyService
            {
                private readonly IServiceProvider _provider;

                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public async Task DoWorkAsync()
                {
                    await using (var scope = _provider.CreateAsyncScope())
                    {
                        await Task.Delay(100);
                    }
                }
            }
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 28, 15, 51)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task CodeFix_ReplacesCreateScopeWithCreateAsyncScope_TopLevelUsingVarDeclaration()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            using var scope = {|#0:provider.CreateScope()|};
            await Task.Delay(100);
            """;

        var fixedSource = Usings + """
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await Task.Delay(100);
            """;

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithLocation(0)
            .WithArguments("top-level statements");

        var test = new CSharpCodeFixTest<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = AnalyzerVerifier<DI005_AsyncDisposalAnalyzer>.ReferenceAssembliesWithDi60
        };

        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.FixedState.OutputKind = OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForReturnedScope()
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

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(16, 16, 16, 43)
            .WithArguments("CreateScopeAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI005_UseCreateAsyncScope");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForPassedToMethod()
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

        var expected = CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.AsyncScopeRequired)
            .WithSpan(15, 18, 15, 45)
            .WithArguments("DoWorkAsync");

        await CodeFixVerifier<DI005_AsyncDisposalAnalyzer, DI005_AsyncScopeRequiredCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI005_UseCreateAsyncScope");
    }
}
