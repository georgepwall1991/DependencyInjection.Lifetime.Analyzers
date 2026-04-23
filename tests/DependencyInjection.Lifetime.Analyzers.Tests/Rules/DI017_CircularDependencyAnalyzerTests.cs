using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI017_CircularDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task DirectCircularDependency_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        // Reported on the canonical (lexicographically smallest) registration in the cycle
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task TransitiveCircularDependency_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface IServiceC { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceC c) { }
            }

            public class ServiceC : IServiceC
            {
                public ServiceC(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    services.AddScoped<IServiceC, ServiceC>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(27, 9, 27, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceC -> IServiceA"));
    }

    [Fact]
    public async Task DirectCircularDependency_WithAdditionalInstanceRegistration_ReportsOnConstructedRegistration()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                private ServiceA() { }

                public ServiceA(IServiceB b) { }

                public static ServiceA Create() => new ServiceA();
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IServiceA), ServiceA.Create());
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(26, 9, 26, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task SelfReferentialDependency_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(15, 9, 15, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceA"));
    }

    [Fact]
    public async Task CircularDependency_OnLongestConstructor_WithMissingTransitiveDependency_StillReports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface IMissing { }

            public class ServiceA : IServiceA
            {
                public ServiceA() { }
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a, IMissing missing) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(23, 9, 23, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task KeyedCircularDependencies_WithDifferentKeys_ReportSeparateDiagnostics()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA_A : IServiceA
            {
                public ServiceA_A([FromKeyedServices("A")] IServiceB b) { }
            }

            public class ServiceB_A : IServiceB
            {
                public ServiceB_A([FromKeyedServices("A")] IServiceA a) { }
            }

            public class ServiceA_B : IServiceA
            {
                public ServiceA_B([FromKeyedServices("B")] IServiceB b) { }
            }

            public class ServiceB_B : IServiceB
            {
                public ServiceB_B([FromKeyedServices("B")] IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IServiceA, ServiceA_A>("A");
                    services.AddKeyedScoped<IServiceB, ServiceB_A>("A");
                    services.AddKeyedScoped<IServiceA, ServiceA_B>("B");
                    services.AddKeyedScoped<IServiceB, ServiceB_B>("B");
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(31, 9, 31, 60)
                .WithArguments("ServiceA_A", "IServiceA (key: A) -> IServiceB (key: A) -> IServiceA (key: A)"),
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(33, 9, 33, 60)
                .WithArguments("ServiceA_B", "IServiceA (key: B) -> IServiceB (key: B) -> IServiceA (key: B)"));
    }

    [Fact]
    public async Task CircularDependency_ThroughFactoryRequiredService_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA>(sp => new ServiceA(sp.GetRequiredService<IServiceB>()));
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 94)
                .WithArguments("IServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task CircularDependency_ThroughMethodGroupFactory_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA>(CreateA);
                    services.AddScoped<IServiceB, ServiceB>();
                }

                private static IServiceA CreateA(IServiceProvider sp) =>
                    new ServiceA(sp.GetRequiredService<IServiceB>());
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 47)
                .WithArguments("IServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task CircularDependency_ThroughActivatorUtilitiesFactory_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA>(sp => ActivatorUtilities.CreateInstance<ServiceA>(sp));
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 93)
                .WithArguments("IServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task KeyedCircularDependency_WithInheritedKey_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA([FromKeyedServices] IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB([FromKeyedServices] IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IServiceA, ServiceA>("tenant");
                    services.AddKeyedScoped<IServiceB, ServiceB>("tenant");
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.ReferenceAssembliesWithLatestKeyedDi,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 63)
                .WithArguments("ServiceA", "IServiceA (key: tenant) -> IServiceB (key: tenant) -> IServiceA (key: tenant)"));
    }

    [Fact]
    public async Task CircularDependency_ThroughEnumerableElement_Reports()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IEnumerable<IServiceB> b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task CircularDependency_ThroughOpenGeneric_Reports()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IValidator<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IValidator<T> validator) { }
            }

            public class Validator<T> : IValidator<T>
            {
                public Validator(IRepository<T> repository) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                    services.AddScoped(typeof(IValidator<>), typeof(Validator<>));
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(21, 9, 21, 72)
                .WithArguments("Repository", "IRepository<> -> IValidator<> -> IRepository<>"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task NoCycle_LinearChain_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface IServiceC { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceC c) { }
            }

            public class ServiceC : IServiceC { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    services.AddScoped<IServiceC, ServiceC>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_WithOptionalFactoryResolution_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA>(sp => new ServiceA(sp.GetService<IServiceB>()!));
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_OnlyOneNodeRegistered_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    // IServiceB is not registered — this is a DI015 concern, not DI017
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_WithOptionalParam_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB? b = null) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        // Optional parameter breaks the cycle since the container can use null
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_WithIEnumerable_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IEnumerable<IServiceB> bs) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                }
            }
            """;

        // The enumerable edge is now analyzed when elements are registered; this
        // case stays silent because the element service is absent.
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_OnNonSelectedShorterConstructor_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface ISafeA { }
            public interface ISafeB { }

            public class SafeA : ISafeA { }
            public class SafeB : ISafeB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
                public ServiceA(ISafeA safeA, ISafeB safeB) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<ISafeA, SafeA>();
                    services.AddScoped<ISafeB, SafeB>();
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        // The container chooses the longest resolvable constructor for ServiceA,
        // so the shorter cycle-shaped constructor should not trigger DI017.
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_ConstructorWithScalarParams_StillReportsCycle()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface ISafe { }

            public class Safe : ISafe { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
                public ServiceA(ISafe safe, string name) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<ISafe, Safe>();
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        // ServiceA(ISafe, string) is NOT resolvable because string isn't in the container.
        // The only resolvable constructor is ServiceA(IServiceB), which creates a cycle.
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(26, 9, 26, 50)
                .WithArguments("ServiceA", "IServiceA -> IServiceB -> IServiceA"));
    }

    [Fact]
    public async Task CircularDependency_OnAmbiguousEquallyGreedyConstructors_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface ISafeA { }
            public interface ISafeB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b, ISafeA safeA) { }
                public ServiceA(ISafeA safeA, ISafeB safeB) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class SafeA : ISafeA { }
            public class SafeB : ISafeB { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    services.AddScoped<ISafeA, SafeA>();
                    services.AddScoped<ISafeB, SafeB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NoRegistrations_DoesNotReport()
    {
        var source = Usings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationInstance_WithCycleShapedConstructors_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                private ServiceA() { }

                public ServiceA(IServiceB b) { }

                public static ServiceA Create() => new ServiceA();
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IServiceA), ServiceA.Create());
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_InUninvokedRegistrationHelper_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public static class ServiceRegistration
            {
                public static IServiceCollection AddCycle(this IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_InSeparateServiceCollection_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }
            public interface IOtherA { }
            public interface IOtherB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IOtherB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IOtherA a) { }
            }

            public class OtherA : IOtherA
            {
                public OtherA(IServiceB b) { }
            }

            public class OtherB : IOtherB
            {
                public OtherB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var other = new ServiceCollection();
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    other.AddScoped<IOtherA, OtherA>();
                    other.AddScoped<IOtherB, OtherB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_RemovedByRemoveAll_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class SafeA : IServiceA { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, ServiceA>();
                    services.AddScoped<IServiceB, ServiceB>();
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IServiceA>(services);
                    services.AddScoped<IServiceA, SafeA>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CircularDependency_ShadowedTryAdd_DoesNotReport()
    {
        var source = Usings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class SafeA : IServiceA { }

            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IServiceA, SafeA>();
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<IServiceA, ServiceA>(services);
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
