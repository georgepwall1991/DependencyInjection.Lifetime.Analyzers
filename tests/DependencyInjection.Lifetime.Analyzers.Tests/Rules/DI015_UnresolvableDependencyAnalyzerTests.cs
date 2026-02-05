using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI015_UnresolvableDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;

        namespace Microsoft.Extensions.DependencyInjection
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class FromKeyedServicesAttribute : Attribute
            {
                public FromKeyedServicesAttribute(object? key) { }
            }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedScoped<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;

                public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) => default!;
            }

            public static class ActivatorUtilities
            {
                public static T CreateInstance<T>(IServiceProvider provider, params object[] parameters) => default!;
                public static object CreateInstance(IServiceProvider provider, Type instanceType, params object[] parameters) => default!;
            }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger<T> { }
            public interface ILoggerFactory { }
        }

        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { }
            public interface IOptionsSnapshot<T> { }
            public interface IOptionsMonitor<T> { }
        }

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task RegisteredService_WithMissingConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(54, 9)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task RegisteredService_WithMissingKeyedConstructorDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService([FromKeyedServices("blue")] IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("green");
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(56, 9)
                .WithArguments("IMyService", "IMyDependency (key: blue)"));
    }

    [Fact]
    public async Task RegisteredService_WithActivatorUtilitiesConstructorOnMissingDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }

                [ActivatorUtilitiesConstructor]
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(60, 9)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredService<IMissingDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 33)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingNonGenericGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService((IMissingDependency)sp.GetRequiredService(typeof(IMissingDependency))));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 53)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithMissingGetRequiredKeyedServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("green");
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredKeyedService<IMyDependency>("blue")));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(57, 33)
                .WithArguments("IMyService", "IMyDependency (key: blue)"));
    }

    [Fact]
    public async Task FactoryMethodGroup_WithMissingGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(CreateMyService);
                }

                private static IMyService CreateMyService(IServiceProvider sp)
                {
                    return new MyService(sp.GetRequiredService<IMissingDependency>());
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(59, 30)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task FactoryOverload_WithImplementationTypeAndMissingGetRequiredServiceDependency_ReportsSingleDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>(
                        sp => new MyService(sp.GetRequiredService<IMissingDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 33)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithNamedImplementationFactoryAndMissingGetRequiredServiceDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        implementationFactory: sp => new MyService(sp.GetRequiredService<IMissingDependency>()));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 56)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithActivatorUtilitiesGenericCreateInstanceMissingDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => ActivatorUtilities.CreateInstance<MyService>(sp));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 19)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task Factory_WithActivatorUtilitiesNonGenericCreateInstanceMissingDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => (IMyService)ActivatorUtilities.CreateInstance(sp, typeof(MyService)));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(55, 31)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task FactoryMethodGroup_WithActivatorUtilitiesGenericCreateInstanceMissingDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(CreateMyService);
                }

                private static IMyService CreateMyService(IServiceProvider sp)
                {
                    return ActivatorUtilities.CreateInstance<MyService>(sp);
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(59, 16)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    [Fact]
    public async Task EarlierDuplicateRegistrationWithMissingDependency_StillReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }
            public interface IDependency { }
            public class Dependency : IDependency { }

            public interface IMyService { }
            public class MissingService : IMyService
            {
                public MissingService(IMissingDependency missing) { }
            }

            public class SafeService : IMyService
            {
                public SafeService(IDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<IMyService, MissingService>();
                    services.AddSingleton<IMyService, SafeService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(62, 9)
                .WithArguments("IMyService", "IMissingDependency"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task RegisteredService_WithRegisteredConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyDependency, MyDependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithResolvableAlternativeConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRegisteredDependency { }
            public class RegisteredDependency : IRegisteredDependency { }
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency missing) { }
                public MyService(IRegisteredDependency registered) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IRegisteredDependency, RegisteredDependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithOptionalConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency? dependency = null) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithIEnumerableDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IEnumerable<IMyDependency> dependencies) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithOpenGenericDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T> { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IRepository<string> repository) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithMatchingKeyedConstructorDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService([FromKeyedServices("blue")] IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyDependency, MyDependency>("blue");
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithActivatorUtilitiesConstructorOnResolvableDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IDependency { }
            public class Dependency : IDependency { }
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                [ActivatorUtilitiesConstructor]
                public MyService(IDependency dependency) { }

                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, Dependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithUnknownDynamicKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                private static object GetKey() => "blue";

                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredKeyedService<IMyDependency>(GetKey())));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryMethodGroup_WithRegisteredGetRequiredServiceDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyDependency { }
            public class MyDependency : IMyDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMyDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyDependency, MyDependency>();
                    services.AddSingleton<IMyService>(CreateMyService);
                }

                private static IMyService CreateMyService(IServiceProvider sp)
                {
                    return new MyService(sp.GetRequiredService<IMyDependency>());
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithActivatorUtilitiesCreateInstanceAndExplicitArguments_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => ActivatorUtilities.CreateInstance<MyService>(sp, new object()));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithFactoryProviderInvocation_DoesNotAnalyzeProviderMethodBody()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(CreateFactory());
                }

                private static Func<IServiceProvider, IMyService> CreateFactory()
                {
                    ServiceProviderServiceExtensions.GetRequiredService<IMissingDependency>(null!);
                    return _ => new MyService();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Factory_WithGetServiceMissingDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(IMissingDependency? dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService((IMissingDependency?)sp.GetService(typeof(IMissingDependency))));
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithLoggerAndOptionsDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public class MyOptions { }

            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(ILogger<MyService> logger, IOptions<MyOptions> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task RegisteredService_WithFrameworkDependencyAndStrictMode_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(ILoggerFactory loggerFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI015.assume_framework_services_registered = false
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            editorConfig,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(52, 9)
                .WithArguments("IMyService", "ILoggerFactory"));
    }

    [Fact]
    public async Task Factory_WithFrameworkDependencyAndStrictMode_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(ILoggerFactory loggerFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(
                        sp => new MyService(sp.GetRequiredService<ILoggerFactory>()));
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI015.assume_framework_services_registered = false
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            editorConfig,
            AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
                .WithLocation(53, 33)
                .WithArguments("IMyService", "ILoggerFactory"));
    }

    [Fact]
    public async Task RegisteredService_WithFrameworkDependencyAndTreeOverride_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService(ILoggerFactory loggerFactory) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var editorConfig = """
            root = true

            dotnet_code_quality.DI015.assume_framework_services_registered = false

            [*.cs]
            dotnet_code_quality.DI015.assume_framework_services_registered = true
            """;

        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source, editorConfig);
    }

    #endregion
}
