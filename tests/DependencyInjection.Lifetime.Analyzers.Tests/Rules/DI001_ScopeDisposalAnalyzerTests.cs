using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI001_ScopeDisposalAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task CreateScope_NotDisposed_ReportsDiagnostic()
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
                    // scope is not disposed!
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(15, 21, 15, 48)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_AssignedToField_NotDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    _scope = _scopeFactory.CreateScope();
                    // storing in field without disposal pattern
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(16, 18, 16, 45)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task MultipleScopes_BothUndisposed_ReportsMultipleDiagnostics()
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
                    var scope1 = _scopeFactory.CreateScope();
                    var scope2 = _scopeFactory.CreateScope();
                    // neither scope is disposed!
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(15, 22, 15, 49)
                .WithArguments("CreateScope"),
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(16, 22, 16, 49)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task ConditionalScopeCreation_NotDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool condition)
                {
                    if (condition)
                    {
                        var scope = _scopeFactory.CreateScope();
                        // scope is not disposed!
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(17, 25, 17, 52)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task NestedScopes_InnerNotDisposed_ReportsDiagnostic()
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
                    using (var outerScope = _scopeFactory.CreateScope())
                    {
                        var innerScope = _scopeFactory.CreateScope();
                        // innerScope is not disposed!
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(17, 30)
                .WithArguments("CreateScope"));
    }

    #endregion

    #region Should Not Report Diagnostic (Proper Disposal)

    [Fact]
    public async Task CreateScope_WithUsingStatement_NoDiagnostic()
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
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetService<object>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_WithUsingDeclaration_NoDiagnostic()
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
                    var service = scope.ServiceProvider.GetService<object>();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateAsyncScope_WithAwaitUsing_NoDiagnostic()
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
                    var service = scope.ServiceProvider.GetService<object>();
                    await Task.Delay(100);
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_WithExplicitDispose_NoDiagnostic()
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
                    try
                    {
                        var service = scope.ServiceProvider.GetService<object>();
                    }
                    finally
                    {
                        scope.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ReturnedFromMethod_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IServiceScope CreateNewScope()
                {
                    // Returning the scope - caller is responsible for disposal
                    return _scopeFactory.CreateScope();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DisposeCallBeforeScopeCreation_ReportsDiagnostic()
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
                    IServiceScope? scope = null;
                    scope?.Dispose();

                    scope = _scopeFactory.CreateScope();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(18, 17)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task ShadowedScopeVariableDisposed_OuterScopeStillReported()
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

                    {
                        var scope2 = _scopeFactory.CreateScope();
                        scope2.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(15, 21)
                .WithArguments("CreateScope"));
    }

    #endregion
}
