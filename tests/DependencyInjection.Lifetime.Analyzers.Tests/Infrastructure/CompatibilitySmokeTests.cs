using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class CompatibilitySmokeTests
{
    private const string NonKeyedUsings = """
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string KeyedUsings = """
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task DI016_Reports_WithDi60References()
    {
        var source = NonKeyedUsings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.{|#0:BuildServiceProvider|}();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.ReferenceAssembliesWithDi60,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(0));
    }

    [Fact]
    public async Task DI016_Reports_WithLatestDiReferences()
    {
        var source = NonKeyedUsings + """
            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.{|#0:BuildServiceProvider|}();
                }
            }
            """;

        await AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>.ReferenceAssembliesWithLatestDi,
            AnalyzerVerifier<DI016_BuildServiceProviderMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.BuildServiceProviderMisuse)
                .WithLocation(0));
    }

    [Fact]
    public async Task DI017_KeyedCycle_Reports_WithDi8KeyedReferences()
    {
        var source = KeyedUsings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA([FromKeyedServices("A")] IServiceB dependency) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB([FromKeyedServices("A")] IServiceA dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddKeyedScoped<IServiceA, ServiceA>("A")|};
                    services.AddKeyedScoped<IServiceB, ServiceB>("A");
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithLocation(0)
                .WithArguments("ServiceA", "IServiceA (key: A) -> IServiceB (key: A) -> IServiceA (key: A)"));
    }

    [Fact]
    public async Task DI017_KeyedCycle_Reports_WithLatestKeyedReferences()
    {
        var source = KeyedUsings + """
            public interface IServiceA { }
            public interface IServiceB { }

            public class ServiceA : IServiceA
            {
                public ServiceA([FromKeyedServices("A")] IServiceB dependency) { }
            }

            public class ServiceB : IServiceB
            {
                public ServiceB([FromKeyedServices("A")] IServiceA dependency) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddKeyedScoped<IServiceA, ServiceA>("A")|};
                    services.AddKeyedScoped<IServiceB, ServiceB>("A");
                }
            }
            """;

        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.ReferenceAssembliesWithLatestKeyedDi,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithLocation(0)
                .WithArguments("ServiceA", "IServiceA (key: A) -> IServiceB (key: A) -> IServiceA (key: A)"));
    }

    [Fact]
    public async Task DI018_Reports_WithDi60References()
    {
        var source = NonKeyedUsings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton<IMyService, AbstractService>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithDi60,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(0)
                .WithArguments("AbstractService", "IMyService", "type is abstract"));
    }

    [Fact]
    public async Task DI018_KeyedRegistration_Reports_WithLatestKeyedReferences()
    {
        var source = KeyedUsings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddKeyedSingleton(typeof(IMyService), "myKey", typeof(AbstractService))|};
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithLatestKeyedDi,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(0)
                .WithArguments("AbstractService", "IMyService", "type is abstract"));
    }
}
