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

    #endregion
}
