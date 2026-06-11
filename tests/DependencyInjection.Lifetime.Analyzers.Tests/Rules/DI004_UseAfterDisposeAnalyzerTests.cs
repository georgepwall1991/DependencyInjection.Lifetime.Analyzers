using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI004_UseAfterDisposeAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task ServiceUsedAfterOwnBranchDispose_BothBranchesDispose_ReportsDiagnostic()
    {
        // The else branch has its own dispose before the use — exclusivity with the then-branch
        // dispose must not hide it.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool fast)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (fast)
                    {
                        scope.Dispose();
                    }
                    else
                    {
                        scope.Dispose();
                        {|DI004:service.DoWork()|};
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceUsedInSwitchSectionReachedByGotoFromDispose_ReportsDiagnostic()
    {
        // goto case chains the sections onto one execution path — they are not mutually
        // exclusive.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(int mode)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    switch (mode)
                    {
                        case 0:
                            scope.Dispose();
                            goto case 1;
                        case 1:
                            {|DI004:service.DoWork()|};
                            break;
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceUsedAfterConditionalDispose_StillReportsDiagnostic()
    {
        // The dispose may have run on the taken branch — a use on the shared path after the
        // conditional dispose is a possible use-after-dispose.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool done)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (done)
                    {
                        scope.Dispose();
                    }

                    {|DI004:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Using service after scope disposed!
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(26, 9, 26, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task UsingVarInNestedBlock_ServiceUsedAfterBlockEnds_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool condition)
                {
                    IMyService service = null;
                    if (condition)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Using service after scope disposed (block ended)!
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(27, 9, 27, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task MultipleScopes_ServiceFromFirstUsedAfterSecond_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService firstService;
                    using (var scope1 = _scopeFactory.CreateScope())
                    {
                        firstService = scope1.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    using (var scope2 = _scopeFactory.CreateScope())
                    {
                        var secondService = scope2.ServiceProvider.GetRequiredService<IMyService>();
                        secondService.DoWork();
                    }

                    // Using firstService after its scope disposed!
                    firstService.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(33, 9, 33, 30)
                .WithArguments("firstService"));
    }

    [Fact]
    public async Task ProviderAlias_ServiceUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var provider = scope.ServiceProvider;
                        service = provider.GetRequiredService<IMyService>();
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(27, 9)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ServiceUsedAfterScopeDisposedInConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    IMyService service;
                    using (var scope = scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(19, 9, 19, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ServiceUsedAfterScopeDisposedInPropertyGetter_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService Service
                {
                    get
                    {
                        IMyService service;
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        }

                        service.DoWork();
                        return service;
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(28, 13, 28, 29)
                .WithArguments("service"),
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(29, 20, 29, 27)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ServiceUsedAfterScopeDisposedInLocalFunction_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    void Run()
                    {
                        IMyService service;
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        }

                        service.DoWork();
                    }

                    Run();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(28, 13, 28, 29)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ServiceUsedAfterScopeDisposedInAnonymousMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    Action run = delegate
                    {
                        IMyService service;
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        }

                        service.DoWork();
                    };

                    run();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(28, 13, 28, 29)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ServiceUsedAfterScopeDisposedInLambda_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    Action run = () =>
                    {
                        IMyService service;
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        }

                        service.DoWork();
                    };

                    run();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(28, 13, 28, 29)
                .WithArguments("service"));
    }

    [Fact]
    public async Task AliasedServiceUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService escaped;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        escaped = service;
                    }

                    escaped.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(27, 9)
                .WithArguments("escaped"));
    }

    [Fact]
    public async Task ReturnedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(26, 16)
                .WithArguments("service"));
    }

    [Fact]
    public async Task PassedAsArgumentAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    Use(service);
                }

                private static void Use(IMyService service)
                {
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(26, 13)
                .WithArguments("service"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task ServiceUsedInOtherSwitchSection_UnrelatedGotoChain_NoDiagnostic()
    {
        // The goto chain links two sections unrelated to the dispose — control cannot flow from
        // the dispose section to the use section, so they stay mutually exclusive.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(int mode)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    switch (mode)
                    {
                        case 0:
                            scope.Dispose();
                            break;
                        case 1:
                            service.DoWork();
                            break;
                        case 2:
                            goto case 3;
                        case 3:
                            break;
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceReassignedInExclusiveBranchBeforeOwnDispose_NoDiagnostic()
    {
        // The local is reassigned to a caller-provided instance before this branch's dispose —
        // tracking must observe the reassignment even in a branch exclusive with the first
        // dispose.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool fast, IMyService existing)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (fast)
                    {
                        scope.Dispose();
                    }
                    else
                    {
                        service = existing;
                        scope.Dispose();
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceUsedInElseBranchOfConditionalDispose_NoDiagnostic()
    {
        // The dispose and the use sit in mutually exclusive branches — they cannot both run.
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool done)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (done)
                    {
                        scope.Dispose();
                    }
                    else
                    {
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceUsedInThenBranch_DisposeInElse_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool keepWorking)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (keepWorking)
                    {
                        service.DoWork();
                    }
                    else
                    {
                        scope.Dispose();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceUsedInDifferentSwitchSectionThanDispose_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(int mode)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    switch (mode)
                    {
                        case 0:
                            scope.Dispose();
                            break;
                        case 1:
                            service.DoWork();
                            break;
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ServiceUsedWithinScope_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceUsedWithinUsingStatement_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingVarInNestedBlock_ServiceUsedWithinBlock_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool condition)
                {
                    if (condition)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork(); // This is fine - within the block
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NestedScopes_ServiceFromOuterUsedAfterInnerDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var outerScope = _scopeFactory.CreateScope())
                    {
                        var outerService = outerScope.ServiceProvider.GetRequiredService<IMyService>();

                        using (var innerScope = _scopeFactory.CreateScope())
                        {
                            var innerService = innerScope.ServiceProvider.GetRequiredService<IMyService>();
                            innerService.DoWork();
                        }

                        // This is fine - outerService scope is still active
                        outerService.DoWork();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonResolvedFromScope_UsedAfterScopeDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ShadowedServiceVariableNameOutsideScope_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    {
                        var service = new LocalService();
                        service.DoWork();
                    }
                }

                private sealed class LocalService : IMyService
                {
                    public void DoWork() { }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderAlias_ReassignedBeforeResolution_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IServiceProvider _rootProvider;

                public MyClass(IServiceScopeFactory scopeFactory, IServiceProvider rootProvider)
                {
                    _scopeFactory = scopeFactory;
                    _rootProvider = rootProvider;
                }

                public void ProcessWork()
                {
                    IMyService? service = null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var provider = scope.ServiceProvider;
                        provider = _rootProvider;
                        service = provider.GetService<IMyService>();
                    }

                    service?.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AliasedService_OverwrittenBeforeScopeEnds_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService? escaped = null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        escaped = service;
                        escaped = null;
                    }

                    escaped?.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task CreateAsyncScope_ServiceUsedAfterDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                Task DoWorkAsync();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task ProcessWorkAsync()
                {
                    IMyService service;
                    await using (var scope = _scopeFactory.CreateAsyncScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Using service after async scope disposed!
                    await service.DoWorkAsync();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, AsyncScopedMyService>();
                }
            }

            public class AsyncScopedMyService : IMyService
            {
                public Task DoWorkAsync() => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(26, 15, 26, 36)
                .WithArguments("service"));
    }

    [Fact]
    public async Task PropertyAccess_AfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                string Name { get; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public string GetServiceName()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Accessing property after scope disposed!
                    return service.Name;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, NamedScopedMyService>();
                }
            }

            public class NamedScopedMyService : IMyService
            {
                public string Name => "name";
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(26, 16, 26, 28)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ExistingScopeVariable_DisposedViaUsingStatement_ServiceUsedAfterwards_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    var scope = _scopeFactory.CreateScope();
                    using (scope)
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(27, 9, 27, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task UsingStatementEmptyBody_NoDiagnostic()
    {
        // Using statement with empty body should not cause issues
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Empty body - do nothing
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetServices_EnumeratedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;
            using System.Linq;
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IEnumerable<IMyService> services;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        services = scope.ServiceProvider.GetServices<IMyService>();
                    }
                    // Using services after scope disposed.
                    foreach (var s in services)
                    {
                        s.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(28, 27, 28, 35)
                .WithArguments("services"));
    }

    [Fact]
    public async Task MultipleServicesFromSameScope_AllUsedAfterDisposed_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IService1
            {
                void DoWork();
            }

            public interface IService2
            {
                void DoOtherWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IService1 svc1;
                    IService2 svc2;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        svc1 = scope.ServiceProvider.GetRequiredService<IService1>();
                        svc2 = scope.ServiceProvider.GetRequiredService<IService2>();
                    }
                    // Both used after scope disposed!
                    svc1.DoWork();
                    svc2.DoOtherWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IService1, ScopedService1>();
                    services.AddScoped<IService2, ScopedService2>();
                }
            }

            public class ScopedService1 : IService1
            {
                public void DoWork() { }
            }

            public class ScopedService2 : IService2
            {
                public void DoOtherWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(33, 9, 33, 22)
                .WithArguments("svc1"),
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(34, 9, 34, 27)
                .WithArguments("svc2"));
    }

    [Fact]
    public async Task UnknownLifetime_UsedAfterScopeDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceAssignedOnlyInsideNestedLambda_ThenUsedAfterScopeDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service = null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        Action capture = () =>
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        };
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceAssignedOnlyInsideNestedLocalFunction_ThenUsedAfterScopeDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service = null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        void Capture()
                        {
                            service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        }
                    }

                    service?.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExplicitDispose_ServiceUsedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    scope.Dispose();
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ExplicitDisposeAsync_ServiceUsedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                Task DoWorkAsync();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task ProcessWorkAsync()
                {
                    var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    await scope.DisposeAsync();
                    await {|#0:service.DoWorkAsync()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public Task DoWorkAsync() => Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalAccessAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService? service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    {|#0:service?.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task FieldAssignmentAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    _captured = {|#0:service|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task MixedGetServices_EnumeratedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IEnumerable<IMyService> services;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        services = scope.ServiceProvider.GetServices<IMyService>();
                    }

                    foreach (var service in {|#0:services|})
                    {
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, SingletonService>();
                    services.AddScoped<IMyService, ScopedService>();
                }
            }

            public class SingletonService : IMyService
            {
                public void DoWork() { }
            }

            public class ScopedService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("services"));
    }

    [Fact]
    public async Task AwaitForeachAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;
            using System.Threading;

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async Task ProcessWorkAsync()
                {
                    IAsyncEnumerable<int> items;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        items = scope.ServiceProvider.GetRequiredService<IAsyncEnumerable<int>>();
                    }

                    await foreach (var item in {|#0:items|})
                    {
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IAsyncEnumerable<int>, AsyncNumbers>();
                }
            }

            public class AsyncNumbers : IAsyncEnumerable<int>, IAsyncEnumerator<int>
            {
                public int Current => 0;
                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
                public ValueTask<bool> MoveNextAsync() => new(false);
                public ValueTask DisposeAsync() => default;
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("items"));
    }

    [Fact]
    public async Task DeconstructionAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void Deconstruct(out int first, out int second);
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    (var first, var second) = {|#0:service|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void Deconstruct(out int first, out int second)
                {
                    first = 1;
                    second = 2;
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task KeyedServiceWithConstantKey_UsedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredKeyedService<IMyService>("primary");
                    }

                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyService, ScopedMyService>("primary");
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.ReferenceAssembliesWithLatestKeyedDi,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task DeferredDelegateCapture_InvokedAfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    Action run;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        run = () => service.DoWork();
                    }

                    {|#0:run()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ReassignedAfterScopeDisposedBeforeUse_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    service = new LocalService();
                    service.DoWork();
                }

                private sealed class LocalService : IMyService
                {
                    public void DoWork() { }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedServiceWithDynamicKey_UsedAfterDispose_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(string key)
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredKeyedService<IMyService>(key);
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyService, ScopedMyService>("primary");
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.ReferenceAssembliesWithLatestKeyedDi);
    }

    [Fact]
    public async Task DeferredDelegateCapture_ReassignedAfterDisposeBeforeInvocation_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    Action run;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        run = () => service.DoWork();
                    }

                    run = () => { };
                    run();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DeferredDelegateCapture_AliasedAfterDisposeThenInvoked_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    Action run;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        run = () => service.DoWork();
                    }

                    var alias = run;
                    {|#0:alias()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalResolutionUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService? service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope?.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    {|#0:service?.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ChainedConditionalResolutionUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService? service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope?.ServiceProvider?.GetRequiredService<IMyService>();
                    }
                    {|#0:service?.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalScopeCreationServiceUsedAfterUsingStatement_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory? _scopeFactory;

                public MyClass(IServiceScopeFactory? scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService? service;
                    using (var scope = _scopeFactory?.CreateScope())
                    {
                        service = scope?.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    {|#0:service?.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalResolutionUsedInsideScope_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope?.ServiceProvider.GetRequiredService<IMyService>();
                        service?.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #region Using-Path Mutual Exclusivity And Out Arguments

    [Fact]
    public async Task UseInBranchOppositeUsingDeclaration_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService? TryGetCached() => null;

                public void ProcessWork()
                {
                    var service = TryGetCached();
                    if (service is null)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork();
                    }
                    else
                    {
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The else branch never sees the scope's instance — the using's dispose cannot
        // have run on that path.
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UseAfterIfContainingUsingDeclaration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool resolve)
                {
                    IMyService? service = null;
                    if (resolve)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    {|#0:service?.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The shared path after the if can hold the disposed instance — keep reporting.
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task OutArgumentRewrite_AfterDispose_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    TryReplace(out service);
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // An out argument writes the local — it is not a use of the disposed instance,
        // and the rewritten local refers to a fresh instance afterwards.
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RefArgument_AfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Mutate(ref IMyService service) { }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    Mutate(ref {|#0:service|});
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // A ref argument reads the current (disposed) instance — still a use.
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task OutArgumentWithSameCallRead_AfterDispose_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement, IMyService current)
                {
                    replacement = current;
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    TryReplace(out service, {|#0:service|});
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The out-write lands only after the call: the second argument still reads the
        // disposed instance (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalOutRewrite_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork(bool refresh)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    if (refresh)
                    {
                        TryReplace(out service);
                    }

                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The out-rewrite only runs on one branch; the shared-path use can still see the
        // disposed instance (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task OutRewriteInIfCondition_SharedPathUse_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    if (TryReplace(out service))
                    {
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The out-call sits in the condition: it runs before any branching, so the local
        // is rewritten on every path (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OutRewriteShortCircuitedInCondition_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork(bool refresh)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    if (refresh && TryReplace(out service))
                    {
                    }

                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The short-circuited out-call may never run — the disposed instance can still
        // reach the shared-path use (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task OutRewriteInDoWhileCondition_BreakSkipsIt_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    do
                    {
                        break;
                    }
                    while (TryReplace(out service));

                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // A break reaches the next statement without ever evaluating the do-while
        // condition (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task CapturedDelegateInvokedAfterDominatingOutRewrite_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    Action handler = () => service.DoWork();
                    scope.Dispose();
                    TryReplace(out service);
                    handler();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The closure observes the reassigned local: after the dominating out-rewrite the
        // delegate sees the fresh instance (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OutRewriteInsideTryWithCatch_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }

                public void ProcessWork()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    try
                    {
                        TryReplace(out service);
                    }
                    catch (Exception)
                    {
                    }

                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // The out-call may throw before assigning while the catch swallows it — the later
        // use can still see the disposed instance (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ConditionalAccessOutRewrite_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class Rewriter
            {
                public bool TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return true;
                }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(Rewriter? maybeRewriter)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    maybeRewriter?.TryReplace(out service);
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // A null receiver skips the out-assignment entirely — the disposed instance can
        // still reach the later use (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task CoalescedOutRewrite_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public object TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return new object();
                }

                public void ProcessWork(object? maybeResult)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    _ = maybeResult ?? TryReplace(out service);
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // A non-null left operand skips the coalesced rewrite (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    [Fact]
    public async Task CoalesceAssignedOutRewrite_SharedPathUse_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public object TryReplace(out IMyService replacement)
                {
                    replacement = new MyService();
                    return new object();
                }

                public void ProcessWork(object? maybe)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    scope.Dispose();
                    maybe ??= TryReplace(out service);
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }

            public class MyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        // A non-null target skips the ??= right-hand side (Codex review regression).
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithLocation(0)
                .WithArguments("service"));
    }

    #endregion

    #endregion
}
