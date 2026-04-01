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
                .WithLocation(7, 33));
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
                .WithLocation(7, 33));
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
                .WithLocation(11, 22));
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
                .WithLocation(11, 22));
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
                .WithLocation(9, 22));
    }

    [Fact]
    public async Task VariableIndirection_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var sc = services;
                    sc.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(8, 12));
    }

    [Fact]
    public async Task DerivedIServiceCollectionParameter_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface ICustomServices : IServiceCollection { }

            public class Startup
            {
                public void ConfigureServices(ICustomServices services)
                {
                    services.BuildServiceProvider();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(9, 18));
    }

    [Fact]
    public async Task HelperMethodReturningBuilderServices_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            public sealed class FakeBuilder
            {
                public IServiceCollection Services { get; } = new ServiceCollection();
            }

            public static class Composition
            {
                public static void Configure(FakeBuilder builder)
                {
                    GetServices(builder).BuildServiceProvider();
                }

                private static IServiceCollection GetServices(FakeBuilder builder) => builder.Services;
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(12, 30));
    }

    [Fact]
    public async Task TopLevelStatements_WithBuildServiceProviderCall_ReportsDiagnostic()
    {
        var source = Usings + """
            var builder = new FakeBuilder();
            var services = builder.Services;
            services.BuildServiceProvider();

            public sealed class FakeBuilder
            {
                public IServiceCollection Services { get; } = new ServiceCollection();
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI016_BuildServiceProviderMisuseAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0"),
                ]),
        };
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(5, 10));

        await test.RunAsync();
    }

    [Fact]
    public async Task TopLevelStatements_WithAssignedServicesAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            var builder = new FakeBuilder();
            IServiceCollection services;
            services = builder.Services;
            services.BuildServiceProvider();

            public sealed class FakeBuilder
            {
                public IServiceCollection Services { get; } = new ServiceCollection();
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI016_BuildServiceProviderMisuseAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0"),
                ]),
        };
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(6, 10));

        await test.RunAsync();
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
    public async Task LocalProviderFactoryFunction_ThatReturnsProvider_NoDiagnostic()
    {
        var source = Usings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    IServiceProvider CreateProvider(IServiceCollection registrations)
                    {
                        return registrations.BuildServiceProvider();
                    }

                    _ = CreateProvider(services);
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
    public async Task HelperMethodReturningBuilderServices_InProviderFactoryMethod_NoDiagnostic()
    {
        var source = Usings + """
            public sealed class FakeBuilder
            {
                public IServiceCollection Services { get; } = new ServiceCollection();
            }

            public static class ProviderFactory
            {
                public static IServiceProvider Create(FakeBuilder builder)
                {
                    return GetServices(builder).BuildServiceProvider();
                }

                private static IServiceCollection GetServices(FakeBuilder builder) => builder.Services;
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

    [Fact]
    public async Task TopLevelStatements_WithStandaloneServiceCollection_NoDiagnostic()
    {
        var source = Usings + """
            IServiceCollection services = new ServiceCollection();
            services.BuildServiceProvider();
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI016_BuildServiceProviderMisuseAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0"),
                ]),
        };
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;

        await test.RunAsync();
    }

    #endregion
}
