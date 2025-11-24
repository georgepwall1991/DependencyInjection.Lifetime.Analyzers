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

    #endregion
}
