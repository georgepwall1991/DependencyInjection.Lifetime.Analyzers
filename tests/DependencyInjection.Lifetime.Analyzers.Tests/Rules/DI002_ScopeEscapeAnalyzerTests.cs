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

    [Fact]
    public async Task ScopedService_AssignedToProperty_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                public IMyService? Service { get; private set; }

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
                    Service = scope.ServiceProvider.GetRequiredService<IMyService>();
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
                .WithLocation(19, 19)
                .WithArguments("Service"));
    }

    [Fact]
    public async Task ScopedService_ResolvedViaProviderAlias_ThenReturned_ReportsDiagnostic()
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
                        var provider = scope.ServiceProvider;
                        service = provider.GetRequiredService<IMyService>();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithLocation(21, 23)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_AliasedThenReturned_ReportsDiagnostic()
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
                    IMyService escaped;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        escaped = service;
                    }

                    return escaped;
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
                .WithLocation(20, 27)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_AssignedToFieldInConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IMyService _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    using var scope = scopeFactory.CreateScope();
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

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(13, 20, 13, 74)
                .WithArguments("_service"));
    }

    [Fact]
    public async Task ScopedService_ReturnedFromPropertyGetter_ReportsDiagnostic()
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

                public IMyService Service
                {
                    get
                    {
                        using var scope = _scopeFactory.CreateScope();
                        return scope.ServiceProvider.GetRequiredService<IMyService>();
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

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(20, 20, 20, 74)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedFromLocalFunction_ReportsDiagnostic()
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

                public IMyService ResolveFromLocalFunction()
                {
                    IMyService Resolve()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        return scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return Resolve();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(20, 20, 20, 74)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_AssignedToFieldInAnonymousMethod_ReportsDiagnostic()
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
                    Action assign = delegate
                    {
                        using var scope = _scopeFactory.CreateScope();
                        _service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    };

                    assign();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(21, 24, 21, 78)
                .WithArguments("_service"));
    }

    [Fact]
    public async Task ScopedService_AssignedToFieldInLambda_ReportsDiagnostic()
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
                    Action assign = () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        _service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    };

                    assign();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(21, 24, 21, 78)
                .WithArguments("_service"));
    }

    [Fact]
    public async Task ScopedService_AssignedDirectlyToOutParameter_ReportsDiagnostic()
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

                public void Resolve(out IMyService service)
                {
                    using var scope = _scopeFactory.CreateScope();
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 19, 18, 73)
                .WithArguments("service"));
    }

    [Fact]
    public async Task ScopedService_AliasedThenAssignedToRefParameter_ReportsDiagnostic()
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

                public void Resolve(ref IMyService escaped)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    escaped = service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 23, 18, 77)
                .WithArguments("escaped"));
    }

    [Fact]
    public async Task ScopedService_CapturedDelegateReturned_ReportsDiagnostic()
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

                public Action GetWork()
                {
                    Action work;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                        work = () => service.DoWork();
                    }

                    return work;
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
                .WithLocation(0)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_CapturedDelegateAssignedToField_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Action? _work;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    Action work;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                        work = () => service.DoWork();
                    }

                    _work = work;
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
                .WithLocation(0)
                .WithArguments("_work"));
    }

    [Fact]
    public async Task ScopedService_CapturedDelegateAssignedToRefParameter_ReportsDiagnostic()
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

                public void Initialize(ref Action escaped)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    escaped = () => service.DoWork();
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
                .WithLocation(0)
                .WithArguments("escaped"));
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

    [Fact]
    public async Task ProviderAlias_ReassignedBeforeResolution_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly IServiceProvider _rootProvider;

                public MyClass(IServiceScopeFactory scopeFactory, IServiceProvider rootProvider)
                {
                    _scopeFactory = scopeFactory;
                    _rootProvider = rootProvider;
                }

                public IMyService? GetService()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var provider = scope.ServiceProvider;
                    provider = _rootProvider;
                    return provider.GetService<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AliasedScopedService_OverwrittenBeforeReturn_NoDiagnostic()
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

                public IMyService? GetService()
                {
                    IMyService? escaped = null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        escaped = service;
                        escaped = null;
                    }

                    return escaped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
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
    public async Task ExistingScopeVariable_DisposedViaUsingStatement_ReturnedService_ReportsDiagnostic()
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
                    var scope = _scopeFactory.CreateScope();
                    using (scope)
                    {
                        return scope.ServiceProvider.GetRequiredService<IMyService>();
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

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithLocation(20, 20)
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

    [Fact]
    public async Task ScopedService_AssignedOnlyInsideNestedLambda_ThenReturned_NoDiagnostic()
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
                    IMyService escaped = null;
                    using var scope = _scopeFactory.CreateScope();
                    Action capture = () =>
                    {
                        escaped = scope.ServiceProvider.GetRequiredService<IMyService>();
                    };

                    return escaped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedOnlyInsideNestedLocalFunction_ThenReturned_NoDiagnostic()
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
                    IMyService escaped = null;
                    using var scope = _scopeFactory.CreateScope();

                    void Capture()
                    {
                        escaped = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return escaped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CapturedDelegate_ReassignedBeforeEscaping_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Action? _work;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    Action work;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        work = () => service.DoWork();
                    }

                    work = () => { };
                    _work = work;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
