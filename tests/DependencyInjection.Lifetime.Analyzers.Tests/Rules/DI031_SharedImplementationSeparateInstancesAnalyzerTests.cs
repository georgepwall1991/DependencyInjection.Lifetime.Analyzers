using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI031_SharedImplementationSeparateInstancesAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public interface IReader { }
        public interface IWriter { }
        public class Store : IReader, IWriter { }

        """;

    [Fact]
    public async Task Singleton_RegisteredUnderTwoInterfaces_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                        {|DI031:services.AddSingleton<IWriter, Store>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Scoped_RegisteredUnderTwoInterfaces_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddScoped<IReader, Store>();
                        {|DI031:services.AddScoped<IWriter, Store>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Singleton_SelfRegistrationPlusInterface_ReportsDiagnostic()
    {
        // AddSingleton<Store>() and AddSingleton<IReader, Store>() are two descriptors and
        // therefore two Store instances.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<Store>();
                        {|DI031:services.AddSingleton<IReader, Store>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Singleton_ForwardedThroughFactory_NoDiagnostic()
    {
        // The documented fix: register once, forward the rest.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<Store>();
                        services.AddSingleton<IReader>(sp => sp.GetRequiredService<Store>());
                        services.AddSingleton<IWriter>(sp => sp.GetRequiredService<Store>());
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Transient_RegisteredUnderTwoInterfaces_NoDiagnostic()
    {
        // Transient means a fresh instance per resolution anyway; there is no shared instance to
        // lose.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddTransient<IReader, Store>();
                        services.AddTransient<IWriter, Store>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DifferentLifetimes_NoDiagnostic()
    {
        // Different lifetimes are separate instances by definition, not by accident.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                        services.AddScoped<IWriter, Store>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task KeyedRegistrations_NoDiagnostic()
    {
        // Keyed registrations are asking for distinct instances.
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public static class ServiceCollectionServiceExtensions
                    {
                        public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(
                            this IServiceCollection services, object? serviceKey)
                            where TService : class where TImplementation : class, TService => services;
                    }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddKeyedSingleton<IReader, Store>("read");
                        services.AddKeyedSingleton<IWriter, Store>("write");
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BranchedRegistrations_NoDiagnostic()
    {
        // Only one branch runs, so the two descriptors never coexist.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services, bool readOnlyMode)
                    {
                        if (readOnlyMode)
                        {
                            services.AddSingleton<IReader, Store>();
                        }
                        else
                        {
                            services.AddSingleton<IWriter, Store>();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SameServiceTypeRegisteredTwice_NoDiagnostic()
    {
        // One service type registered twice is DI012's duplicate registration, not this rule's
        // two-instance claim.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                        services.AddSingleton<IReader, Store>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DifferentImplementations_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class OtherStore : IWriter { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                        services.AddSingleton<IWriter, OtherStore>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ThreeServiceTypes_ReportsEachAfterTheFirst()
    {
        var source =
            Usings
            + """
                public interface IAuditor { }
                public class AuditStore : IReader, IWriter, IAuditor { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, AuditStore>();
                        {|DI031:services.AddSingleton<IWriter, AuditStore>()|};
                        {|DI031:services.AddSingleton<IAuditor, AuditStore>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DifferentClosedGenerics_NoDiagnostic()
    {
        // Store<int> and Store<string> are different runtime types, so each registration builds
        // the instance it asked for.
        var source =
            Usings
            + """
                public interface IReader<T> { }
                public interface IWriter<T> { }
                public class GenericStore<T> : IReader<T>, IWriter<T> { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader<int>, GenericStore<int>>();
                        services.AddSingleton<IWriter<string>, GenericStore<string>>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationsInDifferentBodies_NoDiagnostic()
    {
        // Two bodies may not both run; only same-body pairs are claimed.
        var source =
            Usings
            + """
                public class Startup
                {
                    public void AddReading(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                    }

                    public void AddWriting(IServiceCollection services)
                    {
                        services.AddSingleton<IWriter, Store>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RemovedServiceType_NoDiagnostic()
    {
        // The first descriptor is removed before the second is added, so they never coexist.
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.DependencyInjection.Extensions
                {
                    public static class ServiceCollectionDescriptorExtensions
                    {
                        public static IServiceCollection RemoveAll<T>(this IServiceCollection services) => services;
                    }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IReader, Store>();
                        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IReader>(services);
                        services.AddSingleton<IWriter, Store>();
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TopLevelStatements_ReportsDiagnostic()
    {
        // Top-level statements are one program body even though each is its own syntax node.
        var source = """
            using Microsoft.Extensions.DependencyInjection;

            IServiceCollection services = null!;
            services.AddSingleton<IReader, Store>();
            {|DI031:services.AddSingleton<IWriter, Store>()|};

            public interface IReader { }
            public interface IWriter { }
            public class Store : IReader, IWriter { }
            """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsConsoleApplicationAsync(
            source
        );
    }

    [Fact]
    public async Task RemovalBeforeBothRegistrations_ReportsDiagnostic()
    {
        // The removal ran before either descriptor was added, so it took away something else and
        // both registrations still coexist.
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.DependencyInjection.Extensions
                {
                    public static class ServiceCollectionDescriptorExtensions
                    {
                        public static IServiceCollection RemoveAll<T>(this IServiceCollection services) => services;
                    }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IReader>(services);
                        services.AddSingleton<IReader, Store>();
                        {|DI031:services.AddSingleton<IWriter, Store>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI031_SharedImplementationSeparateInstancesAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }
}
