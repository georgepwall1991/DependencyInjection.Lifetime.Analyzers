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
    public async Task ScopedService_ReturnedAfterManualScopeDispose_ReportsDiagnostic()
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
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    scope.Dispose();
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
                .WithSpan(18, 23, 18, 77)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedAfterManualAsyncScopeDispose_ReportsDiagnostic()
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
                    var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    await scope.DisposeAsync();
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
                .WithSpan(18, 23, 18, 77)
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


    [Fact]
    public async Task ScopedService_AddedToFieldCollection_ReportsDiagnostic()
    {
        // The field-held list outlives the scope: adding the scoped service hands it to a
        // container that survives disposal.
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly List<IMyService> _cache = new List<IMyService>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _cache.Add(service);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AddedToCallerOwnedCollectionParameter_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache(ICollection<IMyService> destination)
                {
                    using var scope = _scopeFactory.CreateScope();
                    destination.Add({|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|});
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_TrackedLocalAddedToCallerOwnedCollectionParameter_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache(ICollection<IMyService> destination)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    destination.Add(service);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_FieldDictionaryIndexerAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Dictionary<string, IMyService> _byTenant = new Dictionary<string, IMyService>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache(string tenant)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _byTenant[tenant] = service;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_DirectResolutionAddedToFieldCollection_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly List<IMyService> _cache = new List<IMyService>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _cache.Add({|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|});
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_SubscribedToFieldEventViaMethodGroup_ReportsDiagnostic()
    {
        // The publisher lives in a field: the subscription keeps the scoped service reachable
        // (and invocable) after the scope is disposed.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public class Publisher
            {
                public event EventHandler Changed;
                public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _publisher.Changed += service.Handle;
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_CapturedDelegateSubscribedToFieldEvent_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    EventHandler handler = (sender, args) => service.DoWork();
                    _publisher.Changed += handler;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_MethodGroupDelegateLocalSubscribed_ReportsDiagnostic()
    {
        // The delegate local is bound to the scoped service via a method group — subscribing it
        // is the same escape as subscribing service.Handle directly.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    EventHandler handler = service.Handle;
                    _publisher.Changed += handler;
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_SubscribedToStaticEvent_ReportsDiagnostic()
    {
        // A static event outlives every scope.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public static class GlobalPublisher
            {
                public static event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    GlobalPublisher.Changed += service.Handle;
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_ConditionalAccessFieldCollectionAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly List<IMyService>? _cache = new List<IMyService>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _cache?.Add(service);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_InlineResolutionMethodGroupSubscribed_ReportsDiagnostic()
    {
        // The method group is taken directly on the resolution — same escape as binding a
        // tracked local's Handle.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _publisher.Changed += {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|}.Handle;
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_ReturnedInsideTuple_ReportsDiagnostic()
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

                public (IMyService Service, int Count) GetServiceWithCount()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return (service, 1);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedInsideAnonymousObject_ReportsDiagnostic()
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

                public object GetHolder()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return new { Service = service };
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_DirectResolutionInsideTupleReturn_ReportsDiagnostic()
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

                public (IMyService Service, int Count) GetServiceWithCount()
                {
                    using var scope = _scopeFactory.CreateScope();
                    return ({|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|}, 1);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_NonGenericResolutionReturnedThroughCastAndNullForgiving_ReportsDiagnostic()
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
                    return (IMyService){|DI002:scope.ServiceProvider.GetService(typeof(IMyService))|}!;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_NonGenericResolutionReturnedThroughAsCast_ReportsDiagnostic()
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
                    using var scope = _scopeFactory.CreateScope();
                    return {|DI002:scope.ServiceProvider.GetService(typeof(IMyService))|} as IMyService;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_WrappedAssignmentResolutionReturned_ReportsDiagnostic()
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
                    IMyService service;
                    service = (IMyService){|DI002:scope.ServiceProvider.GetService(typeof(IMyService))|}!;
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

            public class ScopedMyService : IMyService { }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_TernaryResolutionReturned_ReportsDiagnostic()
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

                public IMyService GetService(bool fromScope, IMyService fallback)
                {
                    using var scope = _scopeFactory.CreateScope();
                    return fromScope ? {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|} : fallback;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_CoalescedResolutionReturned_ReportsDiagnostic()
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

                public IMyService GetService(IMyService fallback)
                {
                    using var scope = _scopeFactory.CreateScope();
                    return {|DI002:scope.ServiceProvider.GetService<IMyService>()|} ?? fallback;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedInsideInitializerOfNewObject_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class Holder
            {
                public IMyService Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Holder GetHolder()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return new Holder { Service = service };
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedToLocalHolderThenReturned_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Holder Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return holder;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderSlotReturnedThroughConditionalAccess_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService? Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return holder?.Service;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderCapturedByEscapingDelegate_ReportsDiagnostic()
    {
        var source = Usings + """
            using System;

            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Func<IMyService?>? _factory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _factory = () => holder.Service;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderEscapesThroughReturnedLocalCollection_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public List<Holder> Capture()
                {
                    var holders = new List<Holder>();
                    var holder = new Holder();
                    holders.Add(holder);

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return holders;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderEscapesThroughReturnedLocalContainerElementAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Holder[] Capture()
                {
                    var holders = new Holder[1];
                    var holder = new Holder();
                    holders[0] = holder;

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return holders;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderEscapesThroughReturnedCollectionAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public List<Holder> Capture()
                {
                    var holders = new List<Holder>();
                    var alias = holders;
                    var holder = new Holder();
                    alias.Add(holder);

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return holders;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderAddedToAlreadyEscapedCollection_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private List<Holder>? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holders = new List<Holder>();
                    _captured = holders;
                    var holder = new Holder();

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    holders.Add(holder);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedFromLaterLoopIteration_ReportsDiagnostic()
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

                public IMyService? Capture(bool keepGoing)
                {
                    IMyService? service = null;
                    while (keepGoing)
                    {
                        if (service is not null)
                        {
                            return service;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    }

                    return null;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedFromLaterLoopIterationWithConditionalStop_ReportsDiagnostic()
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

                public IMyService? Capture(bool keepGoing, bool stop)
                {
                    IMyService? service = null;
                    while (keepGoing)
                    {
                        if (service is not null)
                        {
                            return service;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                        if (stop)
                        {
                            keepGoing = false;
                        }
                    }

                    return null;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LoopAliasReturnedFromLaterIteration_ReportsDiagnostic()
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

                public IMyService? Capture(bool keepGoing)
                {
                    IMyService? service = null;
                    while (keepGoing)
                    {
                        var alias = service;
                        if (alias is not null)
                        {
                            return alias;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    }

                    return null;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderReturnedFromLaterLoopIteration_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Holder? Capture(bool keepGoing)
                {
                    var holder = new Holder();
                    while (keepGoing)
                    {
                        if (holder.Service is not null)
                        {
                            return holder;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    }

                    return null;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedToPreviouslyEscapedLocalHolder_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Holder? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    _captured = holder;

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderEscapesThroughAliasAfterAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Holder? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    var alias = holder;
                    _captured = alias;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderReturnedThroughAliasAfterAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public Holder Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    var alias = holder;
                    return alias;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalHolderAddedToFieldCollectionAfterAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly List<Holder> _holders = new List<Holder>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _holders.Add(holder);
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughFieldAliasLocal_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = _holder;
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughExplicitCastFieldReceiver_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private object _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    using var scope = _scopeFactory.CreateScope();
                    ((Holder)_holder).Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughConditionallyFreshHolder_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture(bool useLocal)
                {
                    Holder holder = _holder;
                    if (useLocal)
                    {
                        holder = new Holder();
                    }

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughNestedFieldHolderReceiver_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public sealed class Wrapper
            {
                public Wrapper(Holder holder)
                {
                    Holder = holder;
                }

                public Holder Holder { get; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var wrapper = new Wrapper(_holder);
                    using var scope = _scopeFactory.CreateScope();
                    wrapper.Holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughNullCoalescingFieldReceiver_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder? _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    Holder? holder = _holder;
                    holder ??= new Holder();

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughConditionalAccessFieldReceiver_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder? _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _holder?.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_LocalAssignedThroughConditionalAccessFieldReceiver_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder? _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    _holder?.Service = service;
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedThroughAliasCreatedBeforeFreshReassignment_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Holder _holder = new Holder();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = _holder;
                    var alias = holder;
                    holder = new Holder();

                    using var scope = _scopeFactory.CreateScope();
                    alias.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedToBaseProperty_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public abstract class BaseClass
            {
                protected IMyService? Service { get; set; }
            }

            public class MyClass : BaseClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    using var scope = _scopeFactory.CreateScope();
                    base.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_AssignedToFullyQualifiedStaticProperty_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace MyApp
            {
                public interface IMyService { }

                public static class Globals
                {
                    public static IMyService? Service { get; set; }
                }

                public class MyClass
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public MyClass(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Capture()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        MyApp.Globals.Service = {|DI002:scope.ServiceProvider.GetRequiredService<IMyService>()|};
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
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(source);
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
    public async Task ScopedService_ReturnedWithUsingScope_ReportsDiagnostic()
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

                public (IServiceScope Scope, IMyService Service) GetScopedService()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    return (scope, service);
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
    public async Task ScopedService_ReturnedWithScopeOnTransferPathBeforeManualDispose_NoDiagnostic()
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

                public (IServiceScope? Scope, IMyService? Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (transfer)
                    {
                        return (scope, service);
                    }

                    scope.Dispose();
                    return (null, null);
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedFromConditionalBranchWithoutScopeTransfer_ReportsDiagnostic()
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

                public (IServiceScope? Scope, IMyService? Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    var otherService = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return transfer ? (scope, service) : (null, otherService);
                    }
                    finally
                    {
                        if (!transfer)
                        {
                            scope.Dispose();
                        }
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(19, 28, 19, 82)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ResolvedInlineFromConditionalBranchWithoutScopeTransfer_ReportsDiagnostic()
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

                public (IServiceScope? Scope, IMyService? Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return transfer
                            ? (scope, service)
                            : (null, scope.ServiceProvider.GetRequiredService<IMyService>());
                    }
                    finally
                    {
                        if (!transfer)
                        {
                            scope.Dispose();
                        }
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(23, 26, 23, 80)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedFromNestedConditionalCompositeBranchWithoutScopeTransfer_ReportsDiagnostic()
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

                public ((IServiceScope? Scope, IMyService? Service) Lease, string Tag) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    var otherService = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return (transfer ? (scope, service) : (null, otherService), "tag");
                    }
                    finally
                    {
                        if (!transfer)
                        {
                            scope.Dispose();
                        }
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(19, 28, 19, 82)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedWithScopeAfterExitingManualDisposeBranch_NoDiagnostic()
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

                public (IServiceScope? Scope, IMyService? Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (!transfer)
                    {
                        scope.Dispose();
                        return (null, null);
                    }

                    return (scope, service);
                }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedWithScopeAfterConditionalManualDispose_ReportsDiagnostic()
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

                public (IServiceScope Scope, IMyService Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    if (!transfer)
                    {
                        scope.Dispose();
                    }

                    return (scope, service);
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
    public async Task ScopedService_ReturnedWithScopeFromTryFinallyConditionalDispose_NoDiagnostic()
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

                public (IServiceScope Scope, IMyService Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return (scope, service);
                    }
                    finally
                    {
                        if (!transfer)
                        {
                            scope.Dispose();
                        }
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_ReturnedWithScopeReferenceButNoScopeTransfer_ReportsDiagnostic()
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

                public (bool Missing, IMyService? Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    if (transfer)
                    {
                        return (scope == null, service);
                    }

                    scope.Dispose();
                    return (false, null);
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
                .WithSpan(18, 23, 18, 77)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedWithConditionallyTransferredScope_ReportsDiagnostic()
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

                public (IServiceScope? Scope, IMyService Service) GetScopedService(bool transfer)
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return (transfer ? scope : null, service);
                    }
                    finally
                    {
                        if (!transfer)
                        {
                            scope.Dispose();
                        }
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 23, 18, 77)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedWithScopeAfterManualDispose_ReportsDiagnostic()
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

                public (IServiceScope Scope, IMyService Service) GetScopedService()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    scope.Dispose();
                    return (scope, service);
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
                .WithSpan(18, 23, 18, 77)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ScopedService_ReturnedWithScopeFromTryFinallyDispose_ReportsDiagnostic()
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

                public (IServiceScope Scope, IMyService Service) GetScopedService()
                {
                    var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    try
                    {
                        return (scope, service);
                    }
                    finally
                    {
                        scope.Dispose();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
                .WithSpan(18, 23, 18, 77)
                .WithArguments("return"));
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
    public async Task ScopedService_ReturnBeforeResolutionAssignment_NoDiagnostic()
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

                public IMyService? GetService(bool useExisting, IMyService existing)
                {
                    IMyService service = existing;
                    if (useExisting)
                    {
                        return service;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.ToString();
                    return null;
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
    public async Task ScopedService_ReturnBeforeResolutionAssignmentInLoopThatStops_NoDiagnostic()
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

                public IMyService? GetService(bool keepGoing)
                {
                    IMyService? service = null;
                    while (keepGoing)
                    {
                        if (service is not null)
                        {
                            return service;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        keepGoing = false;
                    }

                    return null;
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
    public async Task ScopedService_ReturnBeforeResolutionAssignmentInLoopThatBreaks_NoDiagnostic()
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

                public IMyService? GetService(bool keepGoing)
                {
                    IMyService? service = null;
                    while (keepGoing)
                    {
                        if (service is not null)
                        {
                            return service;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        break;
                    }

                    return null;
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
    public async Task ScopedService_ReturnBeforeResolutionAssignmentWithLoopLocalReset_NoDiagnostic()
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

                public IMyService? GetService(bool keepGoing)
                {
                    while (keepGoing)
                    {
                        IMyService? service = null;
                        if (service is not null)
                        {
                            return service;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return null;
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
    public async Task ScopedService_FieldAssignmentBeforeResolutionAssignment_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture(IMyService existing)
                {
                    IMyService service = existing;
                    _captured = service;

                    using var scope = _scopeFactory.CreateScope();
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.ToString();
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
    public async Task ScopedService_RefAssignmentBeforeResolutionAssignment_NoDiagnostic()
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

                public void Capture(ref IMyService escaped, IMyService existing)
                {
                    IMyService service = existing;
                    escaped = service;

                    using var scope = _scopeFactory.CreateScope();
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.ToString();
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
    public async Task ScopedService_AssignedToLocalHolderProperty_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    holder.Service?.ToString();
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
    public async Task ScopedService_CapturedDelegateAssignedThroughConditionalAccessLocalHolder_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public sealed class Holder
            {
                public Action? Work { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    holder?.Work = () => service.DoWork();
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

    [Fact]
    public async Task ScopedService_LocalHolderPropertyReadAssignedToField_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private bool _hasService;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _hasService = holder.Service is not null;
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
    public async Task ScopedService_LocalHolderPropertyReadStoredBeforeAssignment_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    _captured = holder.Service;

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    holder.Service?.ToString();
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
    public async Task ScopedService_LocalHolderPropertyReadReturned_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public string? Capture()
                {
                    var holder = new Holder();
                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    return holder.Service?.ToString();
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
    public async Task ScopedService_AssignedToFreshHolderAfterPreviousHolderEscaped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Holder? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    _captured = holder;
                    holder = new Holder();

                    using var scope = _scopeFactory.CreateScope();
                    holder.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    holder.Service?.ToString();
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
    public async Task ScopedService_AssignedThroughLocalAliasOfFreshHolder_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class Holder
            {
                public IMyService? Service { get; set; }
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    var holder = new Holder();
                    var alias = holder;
                    using var scope = _scopeFactory.CreateScope();
                    alias.Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    holder.Service?.ToString();
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


    [Fact]
    public async Task ScopedService_AddedToLocalCollection_NoDiagnostic()
    {
        // A local list consumed inside the scope does not outlive it.
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { void DoWork(); }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var batch = new List<IMyService>();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    batch.Add(service);
                    foreach (var item in batch)
                    {
                        item.DoWork();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_SubscribedToLocalPublisherEvent_NoDiagnostic()
    {
        // The publisher itself is a scope-lived local: the subscription dies with it.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public class Publisher
            {
                public event EventHandler Changed;
                public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var publisher = new Publisher();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    publisher.Changed += service.Handle;
                    publisher.Raise();
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_FluentNonMutatingAdd_NoDiagnostic()
    {
        // Add returns a new value instead of storing into the receiver (immutable/fluent shape):
        // the discarded result does not retain the service.
        var source = Usings + """
            public interface IMyService { }

            public class FluentCache
            {
                public FluentCache Add(IMyService service) => new FluentCache();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly FluentCache _cache = new FluentCache();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Cache()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _cache.Add(service);
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
    public async Task ScopedService_NonCollectionInsertMethod_NoDiagnostic()
    {
        // The receiver is not a collection — Insert here persists data, it is an ordinary
        // method argument, which DI002 documents as out of scope.
        var source = Usings + """
            public interface IMyService { }

            public class Repository
            {
                public int Insert(IMyService entity) => 1;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Repository _repository = new Repository();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Save()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _repository.Insert(service);
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
    public async Task ScopedService_DelegateValuedPropertySubscribed_NoDiagnostic()
    {
        // service.Handler is a delegate-valued property returning a static handler — the
        // subscribed delegate does not retain the scoped service instance.
        var source = Usings + """
            public interface IMyService
            {
                EventHandler Handler { get; }
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Subscribe()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    _publisher.Changed += service.Handler;
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
                private static void Handle(object sender, EventArgs args) { }
                public EventHandler Handler => Handle;
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_CollectionAddBeforeResolutionAssignment_NoDiagnostic()
    {
        // The object added to the field collection is the pre-existing instance; the local is
        // only reassigned to a scoped resolution afterwards.
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { void DoWork(); }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly List<IMyService> _cache = new List<IMyService>();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work(IMyService existing)
                {
                    using var scope = _scopeFactory.CreateScope();
                    IMyService service = existing;
                    _cache.Add(service);
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_SubscribedThroughLocalWrapperProperty_NoDiagnostic()
    {
        // The event is reached through a property, but the chain's root is a scope-local
        // wrapper — the publisher dies with the scope.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class Wrapper
            {
                public Publisher Publisher { get; } = new Publisher();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var wrapper = new Wrapper();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    wrapper.Publisher.Changed += service.Handle;
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
                public void Handle(object sender, EventArgs args) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedService_MethodGroupConvertedBeforeResolution_NoDiagnostic()
    {
        // Method groups bind their receiver at conversion time: the handler is bound to the
        // pre-existing instance, not the scoped resolution assigned afterwards.
        var source = Usings + """
            public interface IMyService
            {
                void Handle(object sender, EventArgs args);
                void DoWork();
            }

            public class Publisher
            {
                public event EventHandler Changed;
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly Publisher _publisher = new Publisher();

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work(IMyService existing)
                {
                    using var scope = _scopeFactory.CreateScope();
                    IMyService service = existing;
                    EventHandler handler = service.Handle;
                    service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                    _publisher.Changed += handler;
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
                public void Handle(object sender, EventArgs args) { }
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }


    [Fact]
    public async Task ScopedService_AddedToLocalWrapperCollectionProperty_NoDiagnostic()
    {
        // The container is reached through a property, but the chain's root is a scope-local
        // wrapper — the container dies with the scope.
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public class Wrapper
            {
                public List<IMyService> Items { get; } = new List<IMyService>();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var wrapper = new Wrapper();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    wrapper.Items.Add(service);
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
    public async Task ScopedService_TupleConsumedInsideScope_NoDiagnostic()
    {
        // The tuple lives and dies inside the scope — nothing escapes.
        var source = Usings + """
            public interface IMyService { void DoWork(); }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Work()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    var pair = (service, 1);
                    pair.service.DoWork();
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

    [Fact]
    public async Task ConditionalResolutionReturnedFromUsingScope_ReportsDiagnostic()
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
                    using var scope = _scopeFactory.CreateScope();
                    return scope?{|#0:.ServiceProvider.GetRequiredService<IMyService>()|};
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
    public async Task ChainedConditionalResolutionReturnedFromUsingScope_ReportsDiagnostic()
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
                    using var scope = _scopeFactory.CreateScope();
                    return scope?.ServiceProvider?{|#0:.GetRequiredService<IMyService>()|};
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
    public async Task ConditionalScopeCreationLocalServiceReturned_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory? _scopeFactory;

                public MyClass(IServiceScopeFactory? scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService? GetService()
                {
                    using var scope = _scopeFactory?.CreateScope();
                    var service = scope?{|#0:.ServiceProvider.GetRequiredService<IMyService>()|};
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
                .WithLocation(0)
                .WithArguments("return"));
    }

    [Fact]
    public async Task ConditionalResolutionAssignedToField_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService? _captured;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Capture()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _captured = scope?{|#0:.ServiceProvider.GetRequiredService<IMyService>()|};
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
                .WithArguments("_captured"));
    }

    [Fact]
    public async Task ConditionalResolutionUsedLocally_NoDiagnostic()
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

                public void RunOnce()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope?.ServiceProvider.GetRequiredService<IMyService>();
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

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalTransientResolutionReturned_NoDiagnostic()
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
                    using var scope = _scopeFactory.CreateScope();
                    return scope?.ServiceProvider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, TransientMyService>();
                }
            }

            public class TransientMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
    #region DI025 boundary

    [Fact]
    public async Task ThisBoundSubscriptionInRegisteredType_IsDi025Territory_NoDi002Diagnostic()
    {
        // A registered service subscribing its own instance handler to an injected
        // singleton's event is DI025's line; no scope block or scope-resolved service
        // is involved, so DI002 stays silent.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IBus
            {
                event EventHandler MessageReceived;
            }

            public class Bus : IBus
            {
                public event EventHandler MessageReceived;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI002_ScopeEscapeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
