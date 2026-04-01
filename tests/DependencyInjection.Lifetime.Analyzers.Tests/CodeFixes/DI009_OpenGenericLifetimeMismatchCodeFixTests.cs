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

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaTryAddSingleton_ChangesToTryAddScoped()
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

        var fixedSource = """
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
                    services.TryAddScoped(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(18, 9, 18, 78)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaServiceDescriptor_ChangesToScopedDescriptor()
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
                    services.Add(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 95)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaServiceDescriptor_ChangesToTransientDescriptor()
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
                    services.Add(ServiceDescriptor.Transient(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class TransientService : ITransientService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 95)
            .WithArguments("Repository", "transient", "ITransientService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToTransient");
    }

    [Fact]
    public async Task CodeFix_OpenGenericKeyedSingleton_ChangesToKeyedScoped()
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

        var fixedSource = Usings + """
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
                    services.AddKeyedScoped(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 91)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(
                source,
                expected,
                fixedSource,
                CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>.ReferenceAssembliesWithKeyedDi,
                "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericTryAddKeyedSingleton_ChangesToTryAddKeyedScoped()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

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
                    services.TryAddKeyedSingleton(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

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
                    services.TryAddKeyedScoped(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(18, 9, 18, 94)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(
                source,
                expected,
                fixedSource,
                CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>.ReferenceAssembliesWithKeyedDi,
                "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaQualifiedServiceDescriptor_ChangesToScopedDescriptor()
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
                    services.Add(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
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
                    services.Add(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(16, 9, 16, 136)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaTryAddServiceDescriptor_ChangesToScopedDescriptor()
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
                    services.TryAdd(ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var fixedSource = Usings + """
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
                    services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithSpan(18, 9, 18, 98)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaServiceDescriptorConstructor_ChangesToScopedLifetime()
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
                    services.Add(new ServiceDescriptor(typeof(IRepository<>), typeof(Repository<>), ServiceLifetime.Singleton));
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
                    services.Add(new ServiceDescriptor(typeof(IRepository<>), typeof(Repository<>), ServiceLifetime.Scoped));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithLocation(16, 9)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaServiceDescriptorDescribe_ChangesToScopedLifetime()
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
                    services.Add(ServiceDescriptor.Describe(typeof(IRepository<>), typeof(Repository<>), ServiceLifetime.Singleton));
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
                    services.Add(ServiceDescriptor.Describe(typeof(IRepository<>), typeof(Repository<>), ServiceLifetime.Scoped));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithLocation(16, 9)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaStaticTryAddWrapper_ChangesNestedDescriptor()
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
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(
                        services,
                        ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var fixedSource = Usings + """
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
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(
                        services,
                        ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithLocation(18, 9)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_OpenGenericSingletonRegisteredViaStaticImports_ChangesNestedDescriptor()
    {
        var source = Usings + """
            using static Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

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
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(
                        services,
                        Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var fixedSource = Usings + """
            using static Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

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
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(
                        services,
                        Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var expected = CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
            .WithLocation(18, 9)
            .WithArguments("Repository", "scoped", "IScopedService");

        await CodeFixVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer, DI009_OpenGenericLifetimeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI009_ChangeToScoped");
    }

}
