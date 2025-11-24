using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI008 code fix: Transient service implements IDisposable.
/// </summary>
public class DI008_DisposableTransientCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_ChangesToScoped()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, DisposableService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(13, 9, 13, 63)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI008_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_ChangesToSingleton()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, DisposableService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(13, 9, 13, 63)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI008_ChangeToSingleton");
    }

    [Fact]
    public async Task CodeFix_UsesFactory()
    {
        var source = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, DisposableService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService>(sp => new DisposableService());
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(13, 9, 13, 63)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_ChangesToScoped_SingleTypeArg()
    {
        var source = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<DisposableService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public class DisposableService : IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<DisposableService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(12, 9, 12, 51)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI008_ChangeToScoped");
    }
}
