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

    #region Edge Case Tests

    [Fact]
    public async Task UsingStatement_WithoutDeclaration_NoDiagnostic()
    {
        // Using statement without variable declaration (just expression)
        var source = Usings + """
            public interface IMyService { void DoWork(); }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    // Using without declaration - no scope variable to track
                    using (_scopeFactory.CreateScope())
                    {
                        // Can't access scope here, nothing to track
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateAsyncScope_ServiceEscapes_ReportsDiagnostic()
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

                public async Task<IMyService> GetServiceAsync()
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 16, 18, 70)
                .WithArguments("return"));
    }

    [Fact]
    public async Task FieldAssignment_ThisQualified_ReportsDiagnostic()
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
                    this._service = scope.ServiceProvider.GetRequiredService<IMyService>();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithLocation(19, 25)
                .WithArguments("_service"));
    }

    [Fact]
    public async Task NonScopeUsingVariable_NoDiagnostic()
    {
        // Using a non-scope disposable should not trigger scope escape analysis
        var source = Usings + """
            using System.IO;
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private StreamReader _reader;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessFile(string path)
                {
                    using var stream = new FileStream(path, FileMode.Open);
                    _reader = new StreamReader(stream); // Not a scope - no diagnostic
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultipleScopes_OnlyReportsCorrectOne()
    {
        var source = Usings + """
            public interface IMyService { void DoWork(); }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using var scope1 = _scopeFactory.CreateScope();
                    var service1 = scope1.ServiceProvider.GetRequiredService<IMyService>();
                    service1.DoWork(); // Used within scope - OK

                    using var scope2 = _scopeFactory.CreateScope();
                    _service = scope2.ServiceProvider.GetRequiredService<IMyService>(); // Escapes!
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(23, 20, 23, 75)
                .WithArguments("_service"));
    }

    [Fact]
    public async Task ServiceVariable_ReturnedAfterScopeEnds_ReportsDiagnostic()
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
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    return service; // Service escapes after scope ends
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithLocation(20, 23)
                .WithArguments("return"));
    }

    [Fact]
    public async Task SingletonResolvedFromScope_Returned_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

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

                public IMyService GetService()
                {
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientResolvedFromScope_Returned_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyService>();
                }
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
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnknownLifetimeResolvedFromScope_Returned_NoDiagnostic()
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
