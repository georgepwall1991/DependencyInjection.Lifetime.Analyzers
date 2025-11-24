using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI009 code fix: Open generic singleton captures scoped/transient dependency.
/// </summary>
public class DI009_OpenGenericLifetimeMismatchCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_OpenGenericSingletonCapturingScoped_ChangesToScoped()
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

        var fixedSource = Usings + """
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

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 75)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonCapturingTransient_ChangesToScoped()
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

        var fixedSource = Usings + """
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
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class TransientService : ITransientService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 75)
            .WithArguments("Repository", "transient", "ITransientService");

        // Note: DI009 only checks singletons, so changing to Scoped produces no DI009 diagnostic
        // (even though Scoped capturing Transient is technically also a captive dependency - DI003 handles that case)
        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonCapturingTransient_ChangesToTransient()
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

        var fixedSource = Usings + """
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

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 75)
            .WithArguments("Repository", "transient", "ITransientService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToTransient");
    }
}
