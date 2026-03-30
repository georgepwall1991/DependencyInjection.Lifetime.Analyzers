using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI018_NonInstantiableImplementationAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task AbstractClass_RegisteredAsImplementation_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, AbstractService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(10, 9)
                .WithArguments("AbstractService", "IMyService", "type is abstract"));
    }

    [Fact]
    public async Task Interface_RegisteredAsImplementation_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public interface IAnotherService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), typeof(IAnotherService));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(10, 9)
                .WithArguments("IAnotherService", "IMyService", "type is an interface"));
    }

    [Fact]
    public async Task ClassWithOnlyPrivateConstructors_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public class PrivateCtorService : IMyService
            {
                private PrivateCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, PrivateCtorService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(13, 9)
                .WithArguments("PrivateCtorService", "IMyService", "type has no accessible constructors"));
    }

    [Fact]
    public async Task StaticClass_RegisteredViaTypeof_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public static class StaticService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), typeof(StaticService));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(10, 9)
                .WithArguments("StaticService", "IMyService", "type is static"));
    }

    [Fact]
    public async Task OpenGenericPrivateConstructor_DoesNotReport()
    {
        // Unbound generics are excluded from the constructor-accessibility check
        // because Roslyn reports their constructors differently when type parameters
        // are involved, leading to false positives on valid registrations.
        var source = Usings + """
            public interface IRepository<T> { }
            public class PrivateCtorRepository<T> : IRepository<T>
            {
                private PrivateCtorRepository() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(PrivateCtorRepository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericAbstractClass_Reports()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public abstract class AbstractRepository<T> : IRepository<T> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(AbstractRepository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(10, 9)
                .WithArguments("AbstractRepository<>", "IRepository<>", "type is abstract"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task ClassWithPublicConstructor_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService
            {
                public MyService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClassWithDefaultConstructor_DoesNotReport()
    {
        var source = Usings + """
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

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AbstractClass_RegisteredWithFactory_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }
            public class ConcreteService : AbstractService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(sp => new ConcreteService());
                }
            }
            """;

        // Factory handles construction, so abstract type is fine
        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
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

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClassWithInternalConstructor_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class InternalCtorService : IMyService
            {
                internal InternalCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, InternalCtorService>();
                }
            }
            """;

        // Internal constructors are accessible to the DI container
        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
