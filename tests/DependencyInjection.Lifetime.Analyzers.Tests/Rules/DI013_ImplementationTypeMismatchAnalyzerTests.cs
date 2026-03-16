using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI013_ImplementationTypeMismatchAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedSingleton(
                    this IServiceCollection services,
                    Type serviceType,
                    object? serviceKey,
                    Type implementationType) => services;

                public static IServiceCollection AddKeyedSingleton(
                    this IServiceCollection services,
                    Type serviceType,
                    object? serviceKey) => services;

                public static IServiceCollection AddKeyedSingleton(
                    this IServiceCollection services,
                    Type serviceType,
                    object? serviceKey,
                    object implementationInstance) => services;
            }
        }
        """;

    private static DiagnosticResult Expected(int location, string implementationTypeName, string serviceTypeName) =>
        AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
            .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
            .WithLocation(location)
            .WithArguments(implementationTypeName, serviceTypeName);

    [Fact]
    public async Task ValidClosedRegistrationsAndConcreteSelfRegistrations_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }
            public class ConcreteSelf { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IService), typeof(Service));
                    services.AddSingleton(typeof(Service));
                    services.AddScoped(typeof(ConcreteSelf));
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ValidOpenGenericInterfaceAndBaseRegistrations_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepo<T> { }
            public class Repo<T> : IRepo<T> { }

            public abstract class BaseRepo<T> { }
            public class DerivedRepo<T> : BaseRepo<T> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));
                    services.AddSingleton(typeof(BaseRepo<>), typeof(DerivedRepo<>));
                    services.AddSingleton(typeof(Repo<>));
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ValidFactoryAndUnknownInstanceRegistrations_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    IService existing = new Service();
                    services.AddSingleton(typeof(IService), existing);
                    services.AddSingleton(typeof(IService), sp => new Service());
                    services.AddSingleton(typeof(IService), typeof(Service));
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvalidClosedTypeMismatch_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class WrongType { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(WrongType))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "WrongType", "IService"));
    }

    [Fact]
    public async Task InvalidAbstractAndOpenGenericClosedImplementations_ReportDiagnostics()
    {
        var source = Usings + """
            public interface IService { }
            public abstract class AbstractService : IService { }
            public class GenericService<T> : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(AbstractService))|};
                    {|#1:services.AddSingleton(typeof(IService), typeof(GenericService<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "AbstractService", "IService"),
            Expected(1, "GenericService", "IService"));
    }

    [Fact]
    public async Task InvalidInterfaceAndPrivateCtorSelfRegistrations_ReportDiagnostics()
    {
        var source = Usings + """
            public interface IService { }

            public sealed class PrivateCtorService
            {
                private PrivateCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService))|};
                    {|#1:services.AddSingleton(typeof(PrivateCtorService))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "IService", "IService"),
            Expected(1, "PrivateCtorService", "PrivateCtorService"));
    }

    [Fact]
    public async Task InvalidPrivateCtorImplementation_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IService { }

            public sealed class PrivateCtorService : IService
            {
                private PrivateCtorService() { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(PrivateCtorService))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "PrivateCtorService", "IService"));
    }

    [Fact]
    public async Task InvalidOpenGenericArityMismatch_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepo<T> { }
            public class TwoArgRepo<TLeft, TRight> : IRepo<TLeft> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IRepo<>), typeof(TwoArgRepo<,>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "TwoArgRepo", "IRepo"));
    }

    [Fact]
    public async Task InvalidOpenGenericClosedImplementation_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepo<T> { }
            public class Repo<T> : IRepo<T> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IRepo<>), typeof(Repo<int>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "Repo", "IRepo"));
    }

    [Fact]
    public async Task InvalidOpenGenericAbstractAndInterfaceImplementations_ReportDiagnostics()
    {
        var source = Usings + """
            public interface IRepo<T> { }
            public abstract class AbstractRepo<T> : IRepo<T> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IRepo<>), typeof(AbstractRepo<>))|};
                    {|#1:services.AddSingleton(typeof(IRepo<>), typeof(IRepo<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "AbstractRepo", "IRepo"),
            Expected(1, "IRepo", "IRepo"));
    }

    [Fact]
    public async Task InvalidOpenGenericReorderedProjection_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IPairRepo<TLeft, TRight> { }
            public class ReorderedPairRepo<TLeft, TRight> : IPairRepo<TRight, TLeft> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IPairRepo<,>), typeof(ReorderedPairRepo<,>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "ReorderedPairRepo", "IPairRepo"));
    }

    [Fact]
    public async Task InvalidOpenGenericTransformedProjection_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IWrapped<T> { }
            public class WrappedRepo<T> : IWrapped<IEnumerable<T>> { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IWrapped<>), typeof(WrappedRepo<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "WrappedRepo", "IWrapped"));
    }

    [Fact]
    public async Task ValidTryAddKeyedAndServiceDescriptorRegistrations_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService { }
            public class Service : IService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton(typeof(IService), typeof(Service));
                    services.AddKeyedSingleton(typeof(IService), "blue", typeof(Service));
                    services.Add(ServiceDescriptor.Singleton(typeof(IService), typeof(Service)));
                    services.Add(ServiceDescriptor.Describe(typeof(Service), typeof(Service), ServiceLifetime.Singleton));
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvalidTryAddKeyedAndServiceDescriptorRegistrations_ReportDiagnostics()
    {
        var source = Usings + """
            public interface IService { }
            public class WrongType { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.TryAddSingleton(typeof(IService), typeof(WrongType))|};
                    {|#1:services.AddKeyedSingleton(typeof(IService), "blue", typeof(WrongType))|};
                    {|#2:services.Add(ServiceDescriptor.Singleton(typeof(IService), typeof(WrongType)))|};
                    {|#3:services.Add(ServiceDescriptor.Singleton(typeof(IService), new WrongType()))|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "WrongType", "IService"),
            Expected(1, "WrongType", "IService"),
            Expected(2, "WrongType", "IService"),
            Expected(3, "WrongType", "IService"));
    }

    [Fact]
    public async Task InvalidStandardAndKeyedInstanceRegistrations_ReportDiagnostics()
    {
        var source = Usings + """
            public interface IService { }
            public class WrongType { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), new WrongType())|};
                    {|#1:services.AddKeyedSingleton(typeof(IService), "blue", new WrongType())|};
                }
            }
            """;

        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            Expected(0, "WrongType", "IService"),
            Expected(1, "WrongType", "IService"));
    }
}
