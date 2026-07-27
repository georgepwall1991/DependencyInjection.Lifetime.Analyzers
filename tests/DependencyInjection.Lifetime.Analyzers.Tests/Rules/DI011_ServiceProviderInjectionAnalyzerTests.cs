using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI011_ServiceProviderInjectionAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task Constructor_WithIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                private readonly IServiceProvider _provider;
                public MyService(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(17, 9, 17, 52)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceScopeFactory_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                private readonly IServiceScopeFactory _scopeFactory;
                public MyService(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(17, 9, 17, 52)
                .WithArguments("MyService", "IServiceScopeFactory"));
    }

    [Fact]
    public async Task Constructor_WithBothTypes_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider, IServiceScopeFactory scopeFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 52)
                .WithArguments("MyService", "IServiceProvider"),
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 52)
                .WithArguments("MyService", "IServiceScopeFactory"));
    }

    [Fact]
    public async Task Singleton_WithIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(13, 9, 13, 55)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithUnresolvableGreedyConstructor_AndServiceProviderFallback_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IUnregisteredDependency { }
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IUnregisteredDependency missing, IDependency dependency) { }

                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(20, 9)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIKeyedServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IKeyedServiceProvider { }
            }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IKeyedServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithSpan(18, 9, 18, 52)
                .WithArguments("MyService", "IKeyedServiceProvider"));
    }

    [Fact]
    public async Task OpenGenericConstructor_WithIServiceProvider_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("Repository", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InNonMiddlewareInvokeClass_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class MyCommand
            {
                private readonly IServiceProvider _provider;

                public MyCommand(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyCommand>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(21, 9)
                .WithArguments("MyCommand", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InGenericTaskInvokeClass_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public sealed class Order { }

            public class MyCommand
            {
                private readonly IServiceProvider _provider;

                public MyCommand(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task<Order> InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) =>
                    Task.FromResult(new Order());
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<MyCommand>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("MyCommand", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithoutFactoryMember_ReportsDiagnostic()
    {
        var source = Usings + """
            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithVoidCreateMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public void CreateCache() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InFactoryNamedClassWithPlainTaskCreateMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public class CacheFactory
            {
                private readonly IServiceProvider _provider;

                public CacheFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task CreateCacheAsync() => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<CacheFactory>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("CacheFactory", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionallyRemovedByRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    {|#0:services.AddScoped<IMyService, MyService>()|};
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionallyRemovedByClear_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    {|#0:services.AddScoped<IMyService, MyService>()|};
                    if (remove)
                    {
                        services.Clear();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task UnrelatedDescriptorListClear_DoesNotRemoveRegisteredService()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(
                    IServiceCollection services,
                    List<ServiceDescriptor> descriptors)
                {
                    {|#0:services.AddScoped<IMyService, MyService>()|};
                    descriptors.Clear();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenTryAddProviderService_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, SafeService>();
                    services.RemoveAll<IMyService>();
                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllThenTryAddProviderService_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenTryAddInSameBranch_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool replace)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (replace)
                    {
                        services.RemoveAll<IMyService>();
                        {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_TryRemoveAllThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        services.RemoveAll<IMyService>();
                    }
                    catch
                    {
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ShortCircuitedRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(
                    IServiceCollection services,
                    IServiceCollection? fallback)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};
                    _ = fallback ?? services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_LogicalOrRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool skip)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};
                    _ = skip ||
                        services.RemoveAll<IMyService>() is not null;
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_LogicalAndReplace_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool replace)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};
                    _ = replace &&
                        services.Replace(
                            ServiceDescriptor.Scoped<IMyService, SafeService>()) is not null;
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ShortCircuitedAliasBeforeRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection? fallback)
                {
                    var original = new ServiceCollection();
                    {|#0:original.AddScoped<IMyService, ProviderService>()|};

                    IServiceCollection services = new ServiceCollection();
                    _ = fallback ?? (services = original);
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_LogicalAndAliasBeforeRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(
                    IServiceCollection services,
                    bool reassign)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};

                    IServiceCollection alias = new ServiceCollection();
                    _ = reassign &&
                        (alias = services).Count >= 0;
                    alias.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenCaughtThrowThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                            throw new InvalidOperationException();
                        }
                    }
                    catch
                    {
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenMatchingTypedCatchThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                            throw new InvalidOperationException();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenBaseTypedCatchThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                            throw new InvalidOperationException();
                        }
                    }
                    catch (Exception)
                    {
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllThenUncaughtTypedThrowThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                            throw new InvalidOperationException();
                        }
                    }
                    catch (ArgumentException)
                    {
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_LogicalAndLeftAliasBeforeRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(
                    IServiceCollection services,
                    bool continueEvaluation)
                {
                    services.AddScoped<IMyService, ProviderService>();

                    IServiceCollection alias = new ServiceCollection();
                    _ = (alias = services).Count >= 0 &&
                        continueEvaluation;
                    alias.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_RemovedConstructorDependencyUsesProviderFallback_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public interface IDependency { }
            public interface IOther { }

            public sealed class SafeService : IMyService { }
            public sealed class Dependency : IDependency { }
            public sealed class Other : IOther { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IDependency dependency, IOther other) { }
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IOther, Other>();
                    services.AddScoped<IMyService, SafeService>();
                    services.RemoveAll<IDependency>();
                    services.RemoveAll<IMyService>();
                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_RemoveAllAfterReceiverReassignment_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};
                    services = new ServiceCollection();
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionalReceiverAssignment_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(bool useOriginal)
                {
                    var original = new ServiceCollection();
                    IServiceCollection services = new ServiceCollection();
                    if (useOriginal)
                    {
                        services = original;
                    }

                    {|#0:original.AddScoped<IMyService, ProviderService>()|};
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionalReceiverReassignmentAfterAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool replace)
                {
                    var original = new ServiceCollection();
                    services = original;
                    if (replace)
                    {
                        services = new ServiceCollection();
                    }

                    {|#0:original.AddScoped<IMyService, ProviderService>()|};
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllGotoTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        goto fallback;
                    }

                    return;

                fallback:
                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_SwitchRemoveAllThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    services.AddScoped<IMyService, SafeService>();
                    switch (mode)
                    {
                        case 1:
                            services.RemoveAll<IMyService>();
                            break;
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_NestedSwitchAfterRemoveAllThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove, int mode)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        switch (mode)
                        {
                            case 1:
                                break;
                            default:
                                break;
                        }
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_AlternateBranchRegistrationDoesNotClearPendingRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }
            public sealed class AlternateSafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                    }
                    else
                    {
                        services.AddScoped<IMyService, AlternateSafeService>();
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredService_LoopRemoveAllThenTryAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    while (remove)
                    {
                        services.RemoveAll<IMyService>();
                        break;
                    }

                    {|#0:services.TryAddScoped<IMyService, ProviderService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    [Fact]
    public async Task RegisteredTypedHttpClient_CompanionRemoved_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Net.Http;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public sealed class ProviderClient
            {
                public ProviderClient(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddHttpClient<ProviderClient>()|};
                    services.RemoveAll<IHttpClientFactory>();
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            DI011_ServiceProviderInjectionAnalyzer,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                    .ReferenceAssembliesWithFrameworkExtensions,
            MarkupOptions = Microsoft.CodeAnalysis.Testing.MarkupOptions.UseFirstDescriptor,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderClient", "IServiceProvider"));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisteredService_InsertZeroThenReplace_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class SafeService : IMyService { }
            public sealed class ReplacementService : IMyService { }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddScoped<IMyService, ProviderService>()|};
                    services.Insert(
                        0,
                        ServiceDescriptor.Scoped<IMyService, SafeService>());
                    services.Replace(
                        ServiceDescriptor.Scoped<IMyService, ReplacementService>());
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(0)
                .WithArguments("ProviderService", "IServiceProvider"));
    }

    #endregion

    #region Should Not Report Diagnostic (Allowed Cases)

    [Fact]
    public async Task RegisteredService_RemovedByRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_RemovedByClear_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                    services.Clear();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_RemovedByConcreteServiceCollectionClear_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<IMyService, MyService>();
                    services.Clear();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultipleRegisteredServices_RemovedByClear_NoDiagnostics()
    {
        var source = Usings + """
            public interface IFirstService { }
            public interface ISecondService { }

            public sealed class FirstService : IFirstService
            {
                public FirstService(IServiceProvider provider) { }
            }

            public sealed class SecondService : ISecondService
            {
                public SecondService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IFirstService, FirstService>();
                    services.AddScoped<ISecondService, SecondService>();
                    services.Clear();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_TryAddEnumerableThenRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, SafeService>();
                    services.TryAddEnumerable(
                        ServiceDescriptor.Scoped<IMyService, ProviderService>());
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_FluentRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ProviderService>().RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_TopLevelRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.AddScoped<ProviderService>();
            services.RemoveAll<ProviderService>();

            public sealed class ProviderService
            {
                public ProviderService(IServiceProvider provider) { }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsConsoleApplicationAsync(source);
    }

    [Fact]
    public async Task RegisteredService_AliasSnapshotRemoveAllAfterReceiverReassignment_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var original = services;
                    original.AddScoped<IMyService, ProviderService>();
                    services = new ServiceCollection();
                    original.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_AliasRoundTripThenRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ProviderService>();
                    var same = services;
                    services = same;
                    services.RemoveAll<IMyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllExitThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        return;
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllInfiniteLoopThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        while (true)
                        {
                        }
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_RemoveAllAndBreakInOppositeLoopBranchesThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    while (true)
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                        }
                        else
                        {
                            break;
                        }
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_RemoveAllAndTryAddInOppositeBranches_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                    }
                    else
                    {
                        services.TryAddScoped<IMyService, ProviderService>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllNestedExitThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(
                    IServiceCollection services,
                    bool remove,
                    bool firstExit)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        if (firstExit)
                        {
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllNestedThrowExpressionThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        var value = (string?)null ??
                            throw new InvalidOperationException();
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllThenUnconditionalRegistrationThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }
            public sealed class ReplacementService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                    }

                    services.AddScoped<IMyService, ReplacementService>();
                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllThenFinallyRegistrationThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                        }
                    }
                    finally
                    {
                        services.AddScoped<IMyService, SafeService>();
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ConditionalRemoveAllThenConstantTrueExitThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    if (remove)
                    {
                        services.RemoveAll<IMyService>();
                        if (true)
                        {
                            return;
                        }
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NestedTryConditionalRemoveAllThenExitThenTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public sealed class SafeService : IMyService { }

            public sealed class ProviderService : IMyService
            {
                public ProviderService(IServiceProvider provider) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool remove)
                {
                    services.AddScoped<IMyService, SafeService>();
                    try
                    {
                        if (remove)
                        {
                            services.RemoveAll<IMyService>();
                            return;
                        }
                    }
                    finally
                    {
                    }

                    services.TryAddScoped<IMyService, ProviderService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryClass_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IMyServiceFactory
            {
                IMyService CreateService();
            }

            public class MyServiceFactory : IMyServiceFactory
            {
                private readonly IServiceProvider _provider;
                public MyServiceFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public IMyService CreateService() => _provider.GetRequiredService<IMyService>();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyServiceFactory, MyServiceFactory>();
                }
            }
            """;

        // Factory classes are allowed to inject IServiceProvider
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClassWithFactorySuffix_NoDiagnostic()
    {
        var source = Usings + """
            public sealed class Order { }
            public interface IOrderFactory
            {
                Order CreateOrder();
            }

            public class OrderFactory : IOrderFactory
            {
                private readonly IServiceProvider _provider;
                public OrderFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Order CreateOrder() => _provider.GetRequiredService<Order>();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IOrderFactory, OrderFactory>();
                }
            }
            """;

        // Factory classes are allowed
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryInterface_WithAsyncFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public sealed class Order { }
            public interface IOrderFactory
            {
                Task<Order> CreateOrderAsync();
            }

            public class OrderResolver : IOrderFactory
            {
                private readonly IServiceProvider _provider;
                public OrderResolver(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task<Order> CreateOrderAsync() =>
                    Task.FromResult(_provider.GetRequiredService<Order>());
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IOrderFactory, OrderResolver>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryClass_WithInheritedFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public abstract class FactoryBase
            {
                public IMyService CreateService() => throw new NotImplementedException();
            }

            public class MyServiceFactory : FactoryBase
            {
                private readonly IServiceProvider _provider;
                public MyServiceFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyServiceFactory>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task Invoke(Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed when they match the ASP.NET Core middleware shape.
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MiddlewareClass_WithInvokeAsyncMethod_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext { }
            }

            public class MyMiddleware
            {
                private readonly IServiceProvider _provider;
                public MyMiddleware(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyMiddleware>();
                }
            }
            """;

        // Middleware classes are allowed when they match the ASP.NET Core middleware shape.
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HostedService_NoDiagnostic()
    {
        var source = Usings + """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly IServiceProvider _provider;

                public Worker(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Worker>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Singleton_WithIServiceScopeFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class ScopedWorker
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public ScopedWorker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ScopedWorker>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIServiceProvider_InProtectedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public class MyService
            {
                public MyService() { }

                protected MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task EndpointFilterFactory_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.AspNetCore.Http
            {
                public delegate object EndpointFilterFactoryContext();
                public delegate object EndpointFilterDelegate();

                public interface IEndpointFilterFactory
                {
                    object CreateInstance(IServiceProvider serviceProvider, EndpointFilterFactoryContext context, EndpointFilterDelegate next);
                }
            }

            public class MyFilterFactory : Microsoft.AspNetCore.Http.IEndpointFilterFactory
            {
                private readonly IServiceProvider _provider;

                public MyFilterFactory(IServiceProvider provider)
                {
                    _provider = provider;
                }

                public object CreateInstance(IServiceProvider serviceProvider, Microsoft.AspNetCore.Http.EndpointFilterFactoryContext context, Microsoft.AspNetCore.Http.EndpointFilterDelegate next)
                    => new object();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<MyFilterFactory>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithOtherDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        // Normal dependencies don't trigger
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithResolvableCleanConstructor_AndServiceProviderFallback_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IAnotherDependency { }
            public class AnotherDependency : IAnotherDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency, IAnotherDependency anotherDependency) { }

                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IAnotherDependency, AnotherDependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                [ActivatorUtilitiesConstructor]
                public MyService(IDependency dependency) { }

                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithActivatorUtilitiesConstructor_OnServiceProviderConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }

                [ActivatorUtilitiesConstructor]
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, Dependency>();
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ServiceProviderInjection)
                .WithLocation(20, 9)
                .WithArguments("MyService", "IServiceProvider"));
    }

    [Fact]
    public async Task FactoryRegistration_WithImplementationMetadata_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class OptionsServiceCollectionExtensions
                {
                    public static IServiceCollection AddScopedWithFactory<TService, TImplementation>(
                        this IServiceCollection services,
                        Func<IServiceProvider, TImplementation> factory)
                        where TImplementation : class, TService
                    {
                        return services;
                    }
                }
            }

            public interface IMyService { }

            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScopedWithFactory<IMyService, MyService>(_ => new MyService(new EmptyProvider()));
                }
            }

            public sealed class EmptyProvider : IServiceProvider
            {
                public object? GetService(Type serviceType) => null;
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationInstance_WithIServiceProviderConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class FakeProvider : IServiceProvider
            {
                public object? GetService(Type serviceType) => null;
            }

            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), new MyService(new FakeProvider()));
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorImplementationInstance_WithIServiceScopeFactoryConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }

            public sealed class FakeScopeFactory : IServiceScopeFactory
            {
                public IServiceScope CreateScope() => throw new NotImplementedException();
            }

            public class MyService : IMyService
            {
                public MyService(IServiceScopeFactory scopeFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton(
                        typeof(IMyService),
                        new MyService(new FakeScopeFactory())));
                }
            }
            """;

        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredService_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    // MyService is not registered
                }
            }
            """;

        // Unregistered services are not analyzed
        await AnalyzerVerifier<DI011_ServiceProviderInjectionAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
