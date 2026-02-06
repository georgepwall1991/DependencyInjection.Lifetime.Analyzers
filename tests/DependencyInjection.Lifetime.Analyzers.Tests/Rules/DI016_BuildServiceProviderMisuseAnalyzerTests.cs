using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI016_BuildServiceProviderMisuseAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task ConfigureServices_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(7, 24));
    }

    [Fact]
    public async Task ServiceCollectionExtensionMethod_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddFeature(this IServiceCollection services)
                {
                    var provider = services.BuildServiceProvider();
                    return services;
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(7, 24));
    }

    [Fact]
    public async Task RegistrationLambda_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                private static void Configure(Action<IServiceCollection> configure) { }

                public void Compose()
                {
                    Configure(services =>
                    {
                        services.BuildServiceProvider();
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(11, 13));
    }

    [Fact]
    public async Task RegistrationLambda_WithContextAndServicesParameters_ReportsDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                private static void Configure(Action<object, IServiceCollection> configure) { }

                public void Compose()
                {
                    Configure((context, services) =>
                    {
                        services.BuildServiceProvider();
                    });
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(11, 13));
    }

    [Fact]
    public async Task LocalFunctionWithServiceCollectionParameter_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                public void Compose()
                {
                    void Configure(IServiceCollection services)
                    {
                        services.BuildServiceProvider();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(9, 13));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task CompositionRootMethod_WithoutServiceCollectionParameter_NoDiagnostic()
    {
        var source = Usings + """
            public class Program
            {
                public IServiceProvider CreateProvider()
                {
                    var services = new ServiceCollection();
                    return services.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceCollectionMethod_ThatReturnsProvider_NoDiagnostic()
    {
        var source = Usings + """
            public class ProviderFactory
            {
                public IServiceProvider CreateProvider(IServiceCollection services)
                {
                    return services.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ProviderFactoryLambda_ThatReturnsProvider_NoDiagnostic()
    {
        var source = Usings + """
            public class ProviderFactory
            {
                private static IServiceProvider Create(
                    Func<IServiceCollection, IServiceProvider> factory,
                    IServiceCollection services)
                {
                    return factory(services);
                }

                public IServiceProvider Build(IServiceCollection services)
                {
                    return Create(s => s.BuildServiceProvider(), services);
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonDiBuildServiceProviderMethod_NoDiagnostic()
    {
        var source = Usings + """
            public sealed class CustomBuilder
            {
                public IServiceProvider BuildServiceProvider() => null!;
            }

            public class Program
            {
                public void Run()
                {
                    var builder = new CustomBuilder();
                    var provider = builder.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Constructor_WithIServiceCollectionBuildServiceProviderCall_NoDiagnostic()
    {
        var source = Usings + """
            public class ServiceCollectionConsumer
            {
                public ServiceCollectionConsumer(IServiceCollection services)
                {
                    services.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
