using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI002_ScopeEscapeAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task ScopedService_ReturnedFromUsingScope_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 16, 18, 70)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_AssignedToField_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _service = scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(19, 20, 19, 74)
                .WithArguments("_service"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task ScopedService_UsedWithinScope_NoDiagnostic()
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedFromMethodReturningScope_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                // Method returns both scope and service - caller manages lifetime
                public (IServiceScope, IMyService) GetScopedService()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    return (scope, service);
                }
            }
            """;

        // This pattern is intentional - caller manages the scope
        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedToLocalVariable_NoDiagnostic()
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
                    var localRef = service; // Alias within scope is fine
                    localRef.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
