using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI009_OpenGenericLifetimeMismatchAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task OpenGenericSingleton_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 75)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesTransientDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 75)
                .WithArguments("Repository", "transient", "ITransientService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_WithUnresolvableGreedyConstructor_UsesCaptiveFallback_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IUnregisteredDependency { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IUnregisteredDependency missing, ISingletonDependency singleton) { }

                public Repository(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(23, 9)
                .WithArguments("Repository", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesScopedEnumerableDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(18, 9)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ServiceDescriptorShape_CapturesTransientDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.Add(ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(16, 9)
                .WithArguments("Repository", "transient", "ITransientService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_TryAddShape_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.TryAddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(18, 9)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RegisteredViaTryAddSingleton_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.TryAddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(18, 9, 18, 78)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RegisteredViaServiceDescriptor_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.Add(ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 95)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_CapturesMatchingScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository([FromKeyedServices("primary")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("primary");
                    services.AddKeyedSingleton(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 91)
                .WithArguments("Repository", "scoped", "IScopedService"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_WithLongerResolvableConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedDependency scoped) { }

                public Repository(ISingletonDependency singleton, IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithOptionalParameterOnLongerSafeConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }
            public interface IOptionalDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedDependency scoped) { }

                public Repository(ISingletonDependency singleton, IServiceProvider provider, IOptionalDependency optional = null) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithAmbiguousEquallyGreedyConstructors_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonDependency singleton) { }

                public Repository(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task OpenGenericSingleton_CapturesSingletonDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonService singleton) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class SingletonService : ISingletonService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesSingletonEnumerableDependency_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface ISingletonService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<ISingletonService> singletons) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class SingletonService : ISingletonService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericScoped_CapturesScopedDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericTransient_CapturesTransientDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClosedGenericSingleton_CapturesScopedDependency_NoDiagnostic()
    {
        // This should be caught by DI003, not DI009
        // DI009 is specifically for open generics
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<IRepository<string>, Repository<string>>();
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_NoDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                [ActivatorUtilitiesConstructor]
                public Repository(ISingletonDependency dep) { }

                public Repository(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithActivatorUtilitiesConstructor_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonDependency dep) { }

                [ActivatorUtilitiesConstructor]
                public Repository(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(23, 9)
                .WithArguments("Repository", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_WithMismatchedScopedKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository([FromKeyedServices("secondary")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("primary");
                    services.AddKeyedSingleton(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_IneffectiveTryAddSingletonOnCaptiveImplementation_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class SafeRepository<T> : IRepository<T>
            {
            }

            public class CaptiveRepository<T> : IRepository<T>
            {
                public CaptiveRepository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(SafeRepository<>));
                    services.TryAddSingleton(typeof(IRepository<>), typeof(CaptiveRepository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
