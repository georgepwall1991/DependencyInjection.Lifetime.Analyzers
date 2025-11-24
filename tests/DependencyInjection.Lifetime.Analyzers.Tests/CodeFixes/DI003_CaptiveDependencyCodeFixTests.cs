using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI003 code fix: Captive dependency lifetime mismatch.
/// </summary>
public class DI003_CaptiveDependencyCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_SingletonCapturingScoped_ChangesToScoped()
    {
        var source = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IScopedService _scoped;
                public SingletonService(IScopedService scoped)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IScopedService { }
            public class ScopedService : IScopedService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly IScopedService _scoped;
                public SingletonService(IScopedService scoped)
                {
                    _scoped = scoped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_SingletonCapturingTransient_ChangesToScoped()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddScoped<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "transient", "ITransientService");

        // After changing to Scoped, there's still a captive dependency (Scoped capturing Transient)
        var remainingDiagnostic = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 66)
            .WithArguments("SingletonService", "transient", "ITransientService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToScoped", remainingDiagnostic);
    }

    [Fact]
    public async Task CodeFix_SingletonCapturingTransient_ChangesToTransient()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton<ISingletonService, SingletonService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface ISingletonService { }
            public class SingletonService : ISingletonService
            {
                private readonly ITransientService _transient;
                public SingletonService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddTransient<ISingletonService, SingletonService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 69)
            .WithArguments("SingletonService", "transient", "ITransientService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToTransient");
    }

    [Fact]
    public async Task CodeFix_ScopedCapturingTransient_ChangesToTransient()
    {
        var source = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly ITransientService _transient;
                public ScopedService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddScoped<IScopedService, ScopedService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface ITransientService { }
            public class TransientService : ITransientService { }

            public interface IScopedService { }
            public class ScopedService : IScopedService
            {
                private readonly ITransientService _transient;
                public ScopedService(ITransientService transient)
                {
                    _transient = transient;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddTransient<IScopedService, ScopedService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithSpan(21, 9, 21, 60)
            .WithArguments("ScopedService", "transient", "ITransientService");

        await CodeFixVerifier<DI003_CaptiveDependencyAnalyzer, DI003_CaptiveDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI003_ChangeToTransient");
    }
}
