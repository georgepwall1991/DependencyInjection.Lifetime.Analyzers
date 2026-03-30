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

        // The cycle is reported once (deduplication). The exact entry point depends
        // on registration processing order, so we accept either entry point.
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI017_CircularDependencyAnalyzer>
                .Diagnostic(DiagnosticDescriptors.CircularDependency)
                .WithSpan(22, 9, 22, 50)
                .WithArguments("ServiceB", "IServiceB -> IServiceA -> IServiceB"));
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
                .WithSpan(29, 9, 29, 50)
                .WithArguments("ServiceC", "IServiceC -> IServiceA -> IServiceB -> IServiceC"));
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
    public async Task CircularDependency_WithFactory_DoesNotReport()
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

        // Factory registration makes the IServiceA node opaque,
        // so cycle detection should stay silent.
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
                    services.AddScoped<IServiceB, ServiceB>();
                }
            }
            """;

        // IEnumerable<T> is always resolvable (empty collection), no cycle
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

    #endregion
}
