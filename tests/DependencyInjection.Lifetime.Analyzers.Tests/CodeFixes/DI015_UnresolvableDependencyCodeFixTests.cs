using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI015_UnresolvableDependencyCodeFixTests
{
    private const string AddMissingRegistrationEquivalenceKey = "DI015_AddMissingRegistration";

    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task CodeFix_AddsMissingSelfBinding_ForDirectConstructorDependency()
    {
        var source = Usings + """
            public sealed class MissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(MissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public sealed class MissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(MissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<global::MissingDependency>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithLocation(15, 9)
            .WithArguments("IMyService", "MissingDependency");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, AddMissingRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForFactoryAnchoredDependency()
    {
        var source = Usings + """
            public sealed class MissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(MissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService>(
                        sp => new MyService(sp.GetRequiredService<MissingDependency>()));
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithLocation(16, 33)
            .WithArguments("IMyService", "MissingDependency");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, AddMissingRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForInterfaceDependency()
    {
        var source = Usings + """
            public interface IMissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(IMissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithArguments("IMyService", "IMissingDependency");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, AddMissingRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForMultipleMissingDependencies()
    {
        var source = Usings + """
            public sealed class MissingDependencyOne { }
            public sealed class MissingDependencyTwo { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(MissingDependencyOne one, MissingDependencyTwo two) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithArguments("IMyService", "MissingDependencyOne");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, AddMissingRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForKeyedDependency()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                [AttributeUsage(AttributeTargets.Parameter)]
                public sealed class FromKeyedServicesAttribute : Attribute
                {
                    public FromKeyedServicesAttribute(object? key) { }
                }
            }

            public sealed class MissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService([FromKeyedServices("blue")] MissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithArguments("IMyService", "MissingDependency (key: blue)");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, AddMissingRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForServiceDescriptorRegistration()
    {
        var source = Usings + """
            public sealed class MissingDependency { }

            public interface IMyService { }
            public sealed class MyService : IMyService
            {
                public MyService(MissingDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton<IMyService, MyService>());
                }
            }
            """;

        var expected = CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.UnresolvableDependency)
            .WithArguments("IMyService", "MissingDependency");

        await CodeFixVerifier<DI015_UnresolvableDependencyAnalyzer, DI015_UnresolvableDependencyCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, expected, AddMissingRegistrationEquivalenceKey);
    }
}
