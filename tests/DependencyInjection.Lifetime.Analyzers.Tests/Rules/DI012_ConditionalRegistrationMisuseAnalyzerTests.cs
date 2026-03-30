using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI012_ConditionalRegistrationMisuseAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        """;

    private const string KeyedSupport = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;

                public static IServiceCollection TryAddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;
            }
        }

        """;

    #region Should Report Diagnostic - TryAdd After Add

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddScoped_AfterAddScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                    services.TryAddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddTransient_AfterAddTransient_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyService>();
                    services.TryAddTransient<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddScoped_AfterAddSingleton_SameServiceType_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.TryAddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_FactoryShape_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(_ => new MyService1());
                    services.TryAddSingleton<IMyService>(_ => new MyService2());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_InstanceShape_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(new MyService1());
                    services.TryAddSingleton<IMyService>(new MyService2());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddServiceDescriptor_AfterAddServiceDescriptor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(new ServiceDescriptor(typeof(IMyService), typeof(MyService1), ServiceLifetime.Singleton));
                    services.TryAdd(new ServiceDescriptor(typeof(IMyService), typeof(MyService2), ServiceLifetime.Singleton));
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    #endregion

    #region Should Report Diagnostic - Duplicate Add

    [Fact]
    public async Task DuplicateAddSingleton_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task DuplicateAddScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService1>();
                    services.AddScoped<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TripleAddSingleton_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddSingleton<IMyService, MyService2>();
                    services.AddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(14, 9)
                .WithArguments("IMyService", "line 13"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(15, 9)
                .WithArguments("IMyService", "line 13"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task TryAddSingleton_BeforeAddSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        // TryAdd before Add is valid - TryAdd registers first, then Add would override
        // but we don't report TryAdd in this case since it wasn't ignored
        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingleAddSingleton_NoDiagnostic()
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
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingleTryAddSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DifferentServiceTypes_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService1 { }
            public interface IService2 { }
            public class Service1 : IService1 { }
            public class Service2 : IService2 { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IService1, Service1>();
                    services.AddSingleton<IService2, Service2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultipleTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService1>();
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        // Multiple TryAdd calls are fine - only the first takes effect
        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DuplicateRegistrations_OnDifferentServiceCollectionInstances_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices()
                {
                    IServiceCollection primary = new ServiceCollection();
                    IServiceCollection fallback = new ServiceCollection();

                    primary.AddSingleton<IMyService, MyService1>();
                    fallback.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DuplicateRegistrations_InDifferentMethods_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigurePrimary(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                }

                public void ConfigureFallback(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedRegistrations_WithDifferentKeys_NoDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>("A");
                    services.AddKeyedSingleton<IMyService, MyService2>("B");
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedRegistrations_DuplicateSameKey_ReportsDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>("A");
                    services.AddKeyedSingleton<IMyService, MyService2>("A");
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task KeyedNullAndUnkeyedRegistrations_DoNotCrossTrigger()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddKeyedSingleton<IMyService, MyService2>(null);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedNullRegistrations_DuplicateSameNullKey_ReportsDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>(null);
                    services.AddKeyedSingleton<IMyService, MyService2>(null);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task DuplicateRegistrations_AcrossFiles_UsesStableSourceOrdering()
    {
        var sharedTypes = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class ServiceA : IMyService { }
            public class ServiceB : IMyService { }

            public static class ServiceCollectionHolder
            {
                public static IServiceCollection Services { get; } = new ServiceCollection();
            }
            """;

        var laterAlphabetically = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationB
            {
                public static void Configure()
                {
                    ServiceCollectionHolder.Services.AddSingleton<IMyService, ServiceB>();
                }
            }
            """;

        var earlierAlphabetically = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationA
            {
                public static void Configure()
                {
                    ServiceCollectionHolder.Services.AddSingleton<IMyService, ServiceA>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            [
                ("Common.cs", sharedTypes),
                ("B.cs", laterAlphabetically),
                ("A.cs", earlierAlphabetically)
            ],
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithSpan("B.cs", 7, 9, 7, 78)
                .WithArguments("IMyService", "line 7"));
    }

    #endregion
}
