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
    public async Task CreateScope_DisposedOnlyInsideLambda_ReportsDiagnostic()
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
                    Action action = () => scope.Dispose();
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
    public async Task CreateScope_DisposedOnlyConditionally_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool shouldDispose)
                {
                    var scope = _scopeFactory.CreateScope();
                    if (shouldDispose)
                    {
                        scope.Dispose();
                    }
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
    public async Task CreateScope_DisposedOnlyInCatch_ReportsDiagnostic()
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
                    catch
                    {
                        scope.Dispose();
                    }
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
    public async Task CreateScope_DisposedInsideLambda_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Action BuildAction()
                {
                    return () =>
                    {
                        var scope = _scopeFactory.CreateScope();
                        scope.Dispose();
                    };
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

    [Fact]
    public async Task CreateScope_ConditionalAccessDispose_NoDiagnostic()
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
                        scope?.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ReassignmentToLocalWithDispose_NoDiagnostic()
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
                    IServiceScope scope;
                    scope = _scopeFactory.CreateScope();
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
    public async Task CreateScope_ConditionalOuterAssignmentDisposedWithConditionalAccess_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope)
                {
                    IServiceScope? scope = null;
                    if (createScope)
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ConditionalOuterAssignmentDisposedUnderNonNullGuard_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope)
                {
                    IServiceScope? scope = null;
                    if (createScope)
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    if (scope is not null)
                    {
                        scope.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_TryOuterAssignmentDisposedInFinally_NoDiagnostic()
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
                    try
                    {
                        scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetService<object>();
                    }
                    finally
                    {
                        scope?.Dispose();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_IfElseOuterAssignmentsDisposedAfterBranch_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool usePrimary)
                {
                    IServiceScope? scope = null;
                    if (usePrimary)
                    {
                        scope = _scopeFactory.CreateScope();
                    }
                    else
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_IfBranchReturnsBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool usePrimary)
                {
                    IServiceScope? scope = null;
                    if (usePrimary)
                    {
                        scope = _scopeFactory.CreateScope();
                        return;
                    }
                    else
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(18, 21)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_IfBranchDisposesBeforeReturnAndSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool usePrimary)
                {
                    IServiceScope? scope = null;
                    if (usePrimary)
                    {
                        scope = _scopeFactory.CreateScope();
                        scope.Dispose();
                        return;
                    }
                    else
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ElseBranchDisposesBeforeReturnAndSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool usePrimary)
                {
                    IServiceScope? scope = null;
                    if (usePrimary)
                    {
                        scope = _scopeFactory.CreateScope();
                    }
                    else
                    {
                        scope = _scopeFactory.CreateScope();
                        scope.Dispose();
                        return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_DisposesBeforeReturnAndSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool stop)
                {
                    var scope = _scopeFactory.CreateScope();
                    if (stop)
                    {
                        scope.Dispose();
                        return;
                    }

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_GotoBetweenCreationAndSameBranchDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope, bool skip)
                {
                    if (createScope)
                    {
                        var scope = _scopeFactory.CreateScope();
                        if (skip)
                        {
                            goto Done;
                        }

                        scope.Dispose();
                    }

                Done:
                    return;
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(17, 25)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_BackwardGotoCanRepeatCreationBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool repeat)
                {
                Start:
                    var scope = _scopeFactory.CreateScope();
                    if (repeat)
                    {
                        repeat = false;
                        goto Start;
                    }

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(16, 21)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_OtherIfBranchReturnsBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope)
                {
                    IServiceScope? scope = null;
                    if (createScope)
                    {
                        scope = _scopeFactory.CreateScope();
                    }
                    else
                    {
                        return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_SwitchOtherSectionReturnsBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            break;
                        case 2:
                            return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_GotoCaseCanReachReturningSectionBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            goto case 2;
                        case 2:
                            return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(19, 25)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_GotoCaseBreakReachesSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            goto case 2;
                        case 2:
                            break;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_GotoCaseBeforeCreationDoesNotReachReturningSection_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            goto case 2;
                            scope = _scopeFactory.CreateScope();
                            break;
                        case 2:
                            return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_GotoCaseConstantCanReachReturningSectionBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private const int Exit = 2;
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            goto case Exit;
                        case 2:
                            return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(20, 25)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_DisposesBeforeGotoCaseReturn_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            scope.Dispose();
                            goto case 2;
                        case 2:
                            return;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_UnrelatedGotoCaseDoesNotConnectReturningSectionBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(int mode)
                {
                    IServiceScope? scope = null;
                    switch (mode)
                    {
                        case 1:
                            scope = _scopeFactory.CreateScope();
                            break;
                        case 2:
                            return;
                        case 3:
                            goto case 4;
                        case 4:
                            break;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_UnbracedNullGuardBeforeDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope)
                {
                    IServiceScope? scope = null;
                    if (createScope)
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    if (scope is null)
                        return;

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ReturnInsideTryBeforeFinallyDispose_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool stop)
                {
                    var scope = _scopeFactory.CreateScope();
                    try
                    {
                        if (stop)
                        {
                            return;
                        }
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
    public async Task CreateScope_CaughtThrowBeforeDispose_NoDiagnostic()
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
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    scope.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_CaughtThrowInsideCreationTryBeforeSharedDispose_NoDiagnostic()
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
                    try
                    {
                        scope = _scopeFactory.CreateScope();
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_UnmatchedCatchBeforeDispose_ReportsDiagnostic()
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
                        throw new OperationCanceledException();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    scope.Dispose();
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

    [Fact]
    public async Task CreateScope_TryDisposeDoesNotCoverCatchReturn_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool fail)
                {
                    var scope = _scopeFactory.CreateScope();
                    try
                    {
                        if (fail)
                        {
                            throw new InvalidOperationException();
                        }

                        scope.Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }

                    scope.Dispose();
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

    [Fact]
    public async Task CreateScope_ReturnBetweenCreationAndDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool stop)
                {
                    var scope = _scopeFactory.CreateScope();
                    if (stop)
                    {
                        return;
                    }

                    scope.Dispose();
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

    [Fact]
    public async Task CreateScope_ThrowExpressionBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool stop)
                {
                    var scope = _scopeFactory.CreateScope();
                    _ = stop ? throw new InvalidOperationException() : 0;
                    scope.Dispose();
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

    [Fact]
    public async Task CreateScope_InterveningReassignment_FirstScopeLeaked_ReportsDiagnostic()
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
                    scope = _scopeFactory.CreateScope();
                    scope.Dispose();
                }
            }
            """;

        // The first CreateScope is leaked (reassigned before dispose).
        // The second CreateScope is properly disposed.
        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(15, 31, 15, 58)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_InterveningNonScopeReassignment_FirstScopeLeaked_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(IServiceScope otherScope)
                {
                    var scope = _scopeFactory.CreateScope();
                    scope = otherScope;
                    scope.Dispose();
                }
            }
            """;

        // The CreateScope is leaked — reassigned to otherScope before dispose.
        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(15, 21, 15, 48)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_ReassignmentInsideLambda_DoesNotSuppressDiagnostic()
    {
        // Reassignment inside a lambda may never execute, so it should NOT
        // invalidate the dispose proof from the outer scope.
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(IServiceScope otherScope)
                {
                    var scope = _scopeFactory.CreateScope();
                    Action a = () => { scope = otherScope; };
                    scope.Dispose();
                }
            }
            """;

        // The lambda reassignment may never execute, so dispose still counts.
        // No diagnostic — the scope is properly disposed.
        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_ConditionalOuterAssignmentReassignedBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool createScope, IServiceScope otherScope)
                {
                    IServiceScope? scope = null;
                    if (createScope)
                    {
                        scope = _scopeFactory.CreateScope();
                    }

                    scope = otherScope;
                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(18, 21)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_LoopOuterAssignmentDisposedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void DoWork(bool keepCreating)
                {
                    IServiceScope? scope = null;
                    while (keepCreating)
                    {
                        scope = _scopeFactory.CreateScope();
                        keepCreating = false;
                    }

                    scope?.Dispose();
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithLocation(18, 21)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task CreateScope_UsingStatementNoVariable_NoDiagnostic()
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
                    using (_scopeFactory.CreateScope())
                    {
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateScope_InsideUsingButNotDisposed_ReportsDiagnostic()
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
                    using (_scopeFactory.CreateScope())
                    {
                        var scope = _scopeFactory.CreateScope();
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
    public async Task IServiceProvider_CreateScope_NotDisposed_ReportsDiagnostic()
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

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopeMustBeDisposed)
                .WithSpan(15, 21, 15, 44)
                .WithArguments("CreateScope"));
    }

    [Fact]
    public async Task IServiceProvider_CreateScope_WithUsing_NoDiagnostic()
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
                    using (var scope = _provider.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetService<object>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI001_ScopeDisposalAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
