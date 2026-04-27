using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI008_DisposableTransientAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task TransientDisposable_GenericTwoTypeArgs_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 63)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_GenericSingleTypeArg_ReportsDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(12, 9, 12, 51)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientAsyncDisposable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }
            public class AsyncDisposableService : IMyService, IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, AsyncDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 15, 68)
                .WithArguments("AsyncDisposableService", "IAsyncDisposable"));
    }

    [Fact]
    public async Task TransientBothDisposable_ReportsAsyncDisposable()
    {
        var source = Usings + """
            using System.Threading.Tasks;

            public interface IMyService { }
            public class BothDisposableService : IMyService, IDisposable, IAsyncDisposable
            {
                public void Dispose() { }
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, BothDisposableService>();
                }
            }
            """;

        // When both are implemented, we report IAsyncDisposable (checked first)
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(16, 9, 16, 67)
                .WithArguments("BothDisposableService", "IAsyncDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_TypeofSyntax_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient(typeof(IMyService), typeof(DisposableService));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 77)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_TypeofNamedArgumentsOutOfOrder_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient(
                        implementationType: typeof(DisposableService),
                        serviceType: typeof(IMyService));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 15, 45)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TransientDisposable_AllowedByEditorConfigSimpleName_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI008.allowed_disposable_types = DisposableService
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source, editorConfig);
    }

    [Fact]
    public async Task TransientDisposable_AllowedByEditorConfigFullName_NoDiagnostic()
    {
        var source = Usings + """
            namespace App.Services
            {
                public interface IMyService { }
                public class DisposableService : IMyService, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddTransient<IMyService, DisposableService>();
                    }
                }
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI008.allowed_disposable_types = App.Services.DisposableService
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source, editorConfig);
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task TransientNonDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class NonDisposableService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, NonDisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonDisposable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryRegistration_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp => new DisposableService());
                }
            }
            """;

        // Factory registrations are OK - caller is responsible for disposal
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryRegistrationWithDep_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IDependency { }
            public class DisposableService : IMyService, IDisposable
            {
                public DisposableService(IDependency dep) { }
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp =>
                        new DisposableService(sp.GetRequiredService<IDependency>()));
                }
            }
            """;

        // Factory registrations with dependencies are OK
        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryMethodGroupMemberAccess_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public static class FactoryMethods
            {
                public static DisposableService Create(IServiceProvider sp) => new DisposableService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<DisposableService>(FactoryMethods.Create);
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_FactoryDelegateVariable_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    Func<IServiceProvider, DisposableService> factory = static _ => new DisposableService();
                    services.AddTransient<DisposableService>(factory);
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CustomLookalikeServiceCollection_NoDiagnostic()
    {
        var source = """
            using System;
            using Custom;

            namespace Custom
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddTransient<T>(this IServiceCollection services)
                        where T : class => services;
                }
            }

            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(Custom.IServiceCollection services)
                {
                    services.AddTransient<DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Keyed Services (DI 8.0.0)

    [Fact]
    public async Task KeyedTransientDisposable_TwoTypeArgs_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService, DisposableService>("myKey");
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(14, 9, 14, 75)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task KeyedTransientDisposable_SingleTypeArg_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<DisposableService>("myKey");
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 63)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task KeyedTransientDisposable_TypeofSyntax_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient(typeof(IMyService), "myKey", typeof(DisposableService));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(14, 9, 14, 91)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task KeyedTransientDisposable_TypeofNamedArgumentsOutOfOrder_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient(
                        implementationType: typeof(DisposableService),
                        serviceKey: "myKey",
                        serviceType: typeof(IMyService));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(14, 9, 17, 45)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task KeyedTransientDisposable_FactoryRegistration_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService>("myKey", (sp, _) => new DisposableService());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedTransientDisposable_FactoryMethodGroup_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public static class FactoryMethods
            {
                public static IMyService Create(IServiceProvider _, object? __) => new DisposableService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService>("myKey", FactoryMethods.Create);
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedTransientDisposable_FactoryDelegateVariable_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    Func<IServiceProvider, object?, IMyService> factory = static (_, _) => new DisposableService();
                    services.AddKeyedTransient<IMyService>("myKey", factory);
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedTransientDisposable_FactoryNamedArgumentsOutOfOrder_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<DisposableService>(
                        implementationFactory: static (_, _) => new DisposableService(),
                        serviceKey: "myKey");
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    #endregion

    #region ServiceDescriptor and TryAdd shapes

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientGeneric_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient<IMyService, DisposableService>());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 83)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientTypeOf_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient(typeof(IMyService), typeof(DisposableService)));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 97)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorDescribe_Transient_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(
                        typeof(IMyService),
                        typeof(DisposableService),
                        ServiceLifetime.Transient));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 16, 40)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task ServiceCollectionAdd_NewServiceDescriptor_TransientLifetime_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(new ServiceDescriptor(
                        typeof(IMyService),
                        typeof(DisposableService),
                        ServiceLifetime.Transient));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 16, 40)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorScoped_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Scoped<IMyService, DisposableService>());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientFactory_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient<IMyService>(sp => new DisposableService()));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientSingleGenericFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient<DisposableService>(sp => new DisposableService()));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientMethodGroupFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public static class FactoryMethods
            {
                public static DisposableService Create(IServiceProvider sp) => new DisposableService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient<DisposableService>(FactoryMethods.Create));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientDelegateVariableFactory_NoDiagnostic()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    Func<IServiceProvider, DisposableService> factory = static _ => new DisposableService();
                    services.Add(ServiceDescriptor.Transient<DisposableService>(factory));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientDisposable_OpenGenericTypeOf_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T>, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 75)
                .WithArguments("Repository", "IDisposable"));
    }

    [Fact]
    public async Task ServiceCollectionAdd_ServiceDescriptorTransientOpenGeneric_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T>, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Transient(typeof(IRepository<>), typeof(Repository<>)));
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(13, 9, 13, 95)
                .WithArguments("Repository", "IDisposable"));
    }

    [Fact]
    public async Task TryAddTransient_DisposableImpl_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddTransient<IMyService, DisposableService>();
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 15, 66)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TryAddTransient_FactoryRegistration_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddTransient<IMyService>(sp => new DisposableService());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddEnumerable_TransientDescriptor_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(ServiceDescriptor.Transient<IMyService, DisposableService>());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 15, 96)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TryAddEnumerable_TransientDescriptorArray_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(new[]
                    {
                        ServiceDescriptor.Transient<IMyService, DisposableService>()
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 18, 11)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TryAddEnumerable_TransientDescriptorList_ReportsDiagnostic()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(new List<ServiceDescriptor>
                    {
                        ServiceDescriptor.Transient<IMyService, DisposableService>()
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(16, 9, 19, 11)
                .WithArguments("DisposableService", "IDisposable"));
    }

    [Fact]
    public async Task TryAddEnumerable_ScopedDescriptor_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(ServiceDescriptor.Scoped<IMyService, DisposableService>());
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddEnumerable_ScopedDescriptorArray_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(new[]
                    {
                        ServiceDescriptor.Scoped<IMyService, DisposableService>()
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddEnumerable_TransientFactoryDescriptorArray_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddEnumerable(new[]
                    {
                        ServiceDescriptor.Transient<IMyService>(sp => new DisposableService())
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddKeyedTransient_DisposableImpl_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddKeyedTransient<IMyService, DisposableService>("k");
                }
            }
            """;

        await AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI008_DisposableTransientAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DisposableTransient)
                .WithSpan(15, 9, 15, 74)
                .WithArguments("DisposableService", "IDisposable"));
    }

    #endregion
}
