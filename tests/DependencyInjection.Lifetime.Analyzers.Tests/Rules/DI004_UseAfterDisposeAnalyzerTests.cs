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

    #endregion
}
