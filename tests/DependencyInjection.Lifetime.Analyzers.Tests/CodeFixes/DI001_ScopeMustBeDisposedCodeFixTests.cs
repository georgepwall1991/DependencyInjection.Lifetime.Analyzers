using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI001 code fix: Add using statement to ensure scope disposal.
/// </summary>
public class DI001_ScopeMustBeDisposedCodeFixTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_AddsUsingStatement_SyncMethod()
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
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 21, 15, 48)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_AddsAwaitUsingStatement_AsyncMethod()
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
                    var service = scope.ServiceProvider.GetService<object>();
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
                    var service = scope.ServiceProvider.GetService<object>();
                    await Task.Delay(100);
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 21, 15, 48)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddAwaitUsing");
    }

    [Fact]
    public async Task CodeFix_AddsUsingStatement_AsyncMethodWithUsingOption()
    {
        // Even in async methods, we should still offer the "using" option
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
                    var service = scope.ServiceProvider.GetService<object>();
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
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
                    await Task.Delay(100);
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 21, 15, 48)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_AddsAwaitUsing_WhenCreateAsyncScopeUsed()
    {
        // When CreateAsyncScope is already used, await using should not convert the method
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
                    var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetService<object>();
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
                    var service = scope.ServiceProvider.GetService<object>();
                    await Task.Delay(100);
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 21, 15, 53)
            .WithArguments("CreateAsyncScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddAwaitUsing");
    }

    [Fact]
    public async Task CodeFix_AddsAwaitUsing_InAsyncLocalFunction()
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
                        var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    async Task ProcessAsync()
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetService<object>();
                        await Task.Delay(100);
                    }
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(17, 25, 17, 52)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddAwaitUsing");
    }

    [Fact]
    public async Task CodeFix_AddsAwaitUsing_InAsyncLambda()
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
                    Func<Task> work = async () =>
                    {
                        var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    Func<Task> work = async () =>
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetService<object>();
                        await Task.Delay(100);
                    };
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(17, 25, 17, 52)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddAwaitUsing");
    }

    [Fact]
    public async Task CodeFix_AddsUsingStatement_NestedScopes()
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
                    using var outerScope = _scopeFactory.CreateScope();
                    var innerScope = _scopeFactory.CreateScope();
                    var service = innerScope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    using var outerScope = _scopeFactory.CreateScope();
                    using var innerScope = _scopeFactory.CreateScope();
                    var service = innerScope.ServiceProvider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(16, 26, 16, 53)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_AddsUsingStatement_ScopeCreatedViaServiceProvider()
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
                    var scope = _provider.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    using var scope = _provider.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 21, 15, 44)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_AddsUsingStatement_WithExplicitType()
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
                    IServiceScope scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(15, 31, 15, 58)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_PreservesTrivia_WithLeadingComment()
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
                    // Create a scope for this operation
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    // Create a scope for this operation
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetService<object>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(16, 21, 16, 48)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddUsing");
    }

    [Fact]
    public async Task CodeFix_AddsAwaitUsing_InAsyncAnonymousMethod()
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
                    Func<Task> work = async delegate
                    {
                        var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetService<object>();
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

                public void DoWork()
                {
                    Func<Task> work = async delegate
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetService<object>();
                        await Task.Delay(100);
                    };
                }
            }
            """;

        var expected = CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
            .WithSpan(17, 25, 17, 52)
            .WithArguments("CreateScope");

        await CodeFixVerifier<DI001_ScopeDisposalAnalyzer, DI001_ScopeMustBeDisposedCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI001_AddAwaitUsing");
    }
}
