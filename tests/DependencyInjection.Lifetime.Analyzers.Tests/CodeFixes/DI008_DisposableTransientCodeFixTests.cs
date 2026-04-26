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

    [Fact]
    public async Task CodeFix_UseFactoryNotOffered_WhenImplementationRequiresDependencies()
    {
        var source = Usings + """
            public interface IDependency { }
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public DisposableService(IDependency dependency) { }
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

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(14, 9, 14, 63)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_UseFactoryNotOffered_ForTypeofRegistration()
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
                    services.AddTransient(typeof(IMyService), typeof(DisposableService));
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(13, 9, 13, 77)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_ChangesToScoped_TypeofNamedArgumentsOutOfOrder()
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
                    services.AddTransient(
                        implementationType: typeof(DisposableService),
                        serviceType: typeof(IMyService));
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
                    services.AddScoped(
                        implementationType: typeof(DisposableService),
                        serviceType: typeof(IMyService));
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(13, 9, 15, 45)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI008_ChangeToScoped");
    }

    #region Keyed Services (DI 8.0.0)

    [Fact]
    public async Task CodeFix_Keyed_ChangesToKeyedScoped()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(14, 9, 14, 75)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(source, expected, fixedSource, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_Keyed_ChangesToKeyedScoped_PreservesNamedKeyArgument()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var key = "myKey";
                    services.AddKeyedTransient<IMyService, DisposableService>(serviceKey: key);
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var key = "myKey";
                    services.AddKeyedScoped<IMyService, DisposableService>(serviceKey: key);
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(15, 9, 15, 83)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(source, expected, fixedSource, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_ChangeToScoped");
    }

    [Fact]
    public async Task CodeFix_Keyed_ChangesToKeyedSingleton()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(14, 9, 14, 75)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(source, expected, fixedSource, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_ChangeToSingleton");
    }

    [Fact]
    public async Task CodeFix_Keyed_UsesFactory()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService>("myKey", (sp, _) => new DisposableService());
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(14, 9, 14, 75)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(source, expected, fixedSource, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_Keyed_UsesFactory_PreservesNamedKeyArgument()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var key = "myKey";
                    services.AddKeyedTransient<IMyService, DisposableService>(serviceKey: key);
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var key = "myKey";
                    services.AddKeyedTransient<IMyService>(serviceKey: key, (sp, _) => new DisposableService());
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(15, 9, 15, 83)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(source, expected, fixedSource, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_Keyed_UseFactoryNotOffered_WhenImplementationRequiresDependencies()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IDependency { }
            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public DisposableService(IDependency dependency) { }
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<IMyService, DisposableService>("myKey");
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(15, 9, 15, 75)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixNotOfferedWithReferencesAsync(source, expected, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_UseFactory");
    }

    [Fact]
    public async Task CodeFix_Keyed_UseFactoryNotOffered_ForTypeofRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class DisposableService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient(typeof(IMyService), "myKey", typeof(DisposableService));
                }
            }
            """;

        var expected = CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DisposableTransient)
            .WithSpan(14, 9, 14, 91)
            .WithArguments("DisposableService", "IDisposable");

        await CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>
            .VerifyCodeFixNotOfferedWithReferencesAsync(source, expected, CodeFixVerifier<DI008_DisposableTransientAnalyzer, DI008_DisposableTransientCodeFixProvider>.ReferenceAssembliesWithKeyedDi, "DI008_UseFactory");
    }

    #endregion
}
