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
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(33, 9, 33, 30)
                .WithArguments("firstService"));
    }

    #endregion

    #region Should Not Report Diagnostic

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
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(26, 16, 26, 28)
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
    public async Task GetServices_UsedAfterDispose_NotCurrentlyDetected()
    {
        // KNOWN LIMITATION: GetServices returns IEnumerable<T> which is iterated via foreach.
        // The analyzer doesn't track iteration over collections, only direct method/property access.
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
                    // Using services after scope disposed - not currently detected
                    foreach (var s in services)
                    {
                        s.DoWork();
                    }
                }
            }
            """;

        // Document current behavior - iteration not tracked
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
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

    #endregion
}
