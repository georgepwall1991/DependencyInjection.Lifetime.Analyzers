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
        using Microsoft.Extensions.DependencyInjection.Extensions;

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
                .WithLocation(11, 9)
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
                .WithLocation(11, 9)
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
                .WithLocation(14, 9)
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
                .WithLocation(11, 9)
                .WithArguments("StaticService", "IMyService", "type is static"));
    }

    [Fact]
    public async Task OpenGenericPrivateConstructor_Reports()
    {
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

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("PrivateCtorRepository<>", "IRepository<>", "type has no accessible constructors"));
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
                .WithLocation(11, 9)
                .WithArguments("AbstractRepository<>", "IRepository<>", "type is abstract"));
    }

    [Fact]
    public async Task ClassWithInternalConstructor_Reports()
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

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("InternalCtorService", "IMyService", "type has no accessible constructors"));
    }

    [Fact]
    public async Task ClassWithOnlyProtectedConstructors_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public class ProtectedCtorService : IMyService
            {
                protected ProtectedCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, ProtectedCtorService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("ProtectedCtorService", "IMyService", "type has no accessible constructors"));
    }

    [Fact]
    public async Task ServiceDescriptor_WithAbstractImplementation_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton(typeof(IMyService), typeof(AbstractService)));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(11, 9)
                .WithArguments("AbstractService", "IMyService", "type is abstract"));
    }

    [Fact]
    public async Task OpenGenericInternalConstructor_Reports()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class InternalCtorRepository<T> : IRepository<T>
            {
                internal InternalCtorRepository() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(InternalCtorRepository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("InternalCtorRepository<>", "IRepository<>", "type has no accessible constructors"));
    }

    [Fact]
    public async Task OpenGenericProtectedConstructor_Reports()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class ProtectedCtorRepository<T> : IRepository<T>
            {
                protected ProtectedCtorRepository() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(ProtectedCtorRepository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("ProtectedCtorRepository<>", "IRepository<>", "type has no accessible constructors"));
    }

    [Fact]
    public async Task TryAddSingleton_WithProtectedImplementation_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public class ProtectedCtorService : IMyService
            {
                protected ProtectedCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, ProtectedCtorService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("ProtectedCtorService", "IMyService", "type has no accessible constructors"));
    }

    [Fact]
    public async Task ServiceDescriptorDescribe_WithProtectedImplementation_Reports()
    {
        var source = Usings + """
            public interface IMyService { }
            public class ProtectedCtorService : IMyService
            {
                protected ProtectedCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(typeof(IMyService), typeof(ProtectedCtorService), ServiceLifetime.Singleton));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("ProtectedCtorService", "IMyService", "type has no accessible constructors"));
    }

    [Fact]
    public async Task KeyedRegistration_WithAbstractImplementation_Reports()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public abstract class AbstractService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton(typeof(IMyService), "myKey", typeof(AbstractService));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(11, 9)
                .WithArguments("AbstractService", "IMyService", "type is abstract"));
    }

    [Fact]
    public async Task KeyedOpenGenericPrivateConstructor_Reports()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IRepository<T> { }
            public class PrivateCtorRepository<T> : IRepository<T>
            {
                private PrivateCtorRepository() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton(typeof(IRepository<>), "myKey", typeof(PrivateCtorRepository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>
                .Diagnostic(DiagnosticDescriptors.NonInstantiableImplementation)
                .WithLocation(14, 9)
                .WithArguments("PrivateCtorRepository<>", "IRepository<>", "type has no accessible constructors"));
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

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationInstance_WithOnlyPrivateConstructors_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class PrivateCtorService : IMyService
            {
                private PrivateCtorService() { }

                public static PrivateCtorService Create() => new PrivateCtorService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), PrivateCtorService.Create());
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorImplementationInstance_WithOnlyPrivateConstructors_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class PrivateCtorService : IMyService
            {
                private PrivateCtorService() { }

                public static PrivateCtorService Create() => new PrivateCtorService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton(typeof(IMyService), PrivateCtorService.Create()));
                }
            }
            """;

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
    public async Task OpenGenericPublicConstructor_DoesNotReport()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T>
            {
                public Repository() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StructRegistration_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public struct MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), typeof(MyService));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TypeofRegistration_WithConcreteImplementation_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class ConcreteService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), typeof(ConcreteService));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddSingleton_WithConcreteImplementation_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public class ConcreteService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, ConcreteService>();
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorDescribe_WithFactory_DoesNotReport()
    {
        var source = Usings + """
            public interface IMyService { }
            public abstract class AbstractService : IMyService { }
            public class ConcreteService : AbstractService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(typeof(IMyService), sp => new ConcreteService(), ServiceLifetime.Singleton));
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedImplementationInstance_WithPrivateConstructor_DoesNotReport()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class PrivateCtorService : IMyService
            {
                private PrivateCtorService() { }

                public static PrivateCtorService Create() => new PrivateCtorService();
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService>("myKey", PrivateCtorService.Create());
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedFactory_WithAbstractImplementation_DoesNotReport()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public abstract class AbstractService : IMyService { }
            public class ConcreteService : AbstractService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService>("myKey", (sp, _) => new ConcreteService());
                }
            }
            """;

        await AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI018_NonInstantiableImplementationAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    #endregion
}
