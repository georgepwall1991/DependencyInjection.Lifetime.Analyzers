using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI012_TryAddIgnoredCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        """;

    [Fact]
    public async Task CodeFix_RemovesIgnoredTryAddSingletonStatement()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
            .WithLocation(12, 9)
            .WithArguments("IMyService", "line 11");

        await CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                DI012_TryAddIgnoredCodeFixProvider.RemoveIgnoredRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_RemovesIgnoredTryAddServiceDescriptorStatement()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(new ServiceDescriptor(typeof(IMyService), typeof(MyService1), ServiceLifetime.Singleton));
                    services.TryAdd(new ServiceDescriptor(typeof(IMyService), typeof(MyService2), ServiceLifetime.Singleton));
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(new ServiceDescriptor(typeof(IMyService), typeof(MyService1), ServiceLifetime.Singleton));
                }
            }
            """;

        var expected = CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
            .WithLocation(13, 9)
            .WithArguments("IMyService", "line 12");

        await CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                DI012_TryAddIgnoredCodeFixProvider.RemoveIgnoredRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task CodeFix_RemovesIgnoredTryAddInsideInvokedWrapper()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddFallback();
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddFallback();
                }
            }
            """;

        var expected = CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
            .WithLocation(12, 9)
            .WithArguments("IMyService", "line 21");

        await CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                DI012_TryAddIgnoredCodeFixProvider.RemoveIgnoredRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task DuplicateRegistration_DoesNotOfferTryAddRemovalFix()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        var expected = CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
            .WithLocation(13, 9)
            .WithArguments("IMyService", "line 12");

        await CodeFixVerifier<DI012_ConditionalRegistrationMisuseAnalyzer, DI012_TryAddIgnoredCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(source, expected);
    }
}
