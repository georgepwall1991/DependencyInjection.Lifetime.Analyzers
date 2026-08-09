using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI032_AsyncOnlyDisposableRegistrationAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        public interface IWorker { }

        """;

    [Fact]
    public async Task Singleton_AsyncOnlyDisposable_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker, AsyncWorker>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task Scoped_AsyncOnlyDisposable_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddScoped<IWorker, AsyncWorker>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BothDisposableInterfaces_NoDiagnostic()
    {
        // Implementing IDisposable alongside IAsyncDisposable is the documented fix: a
        // synchronous provider disposal has something to call.
        var source =
            Usings
            + """
                public class Worker : IWorker, IDisposable, IAsyncDisposable
                {
                    public void Dispose() { }
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, Worker>();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonDisposableService_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Worker : IWorker { }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, Worker>();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PreBuiltInstance_NoDiagnostic()
    {
        // The container never disposes an instance it did not create, so it never reaches the
        // synchronous-disposal throw. That registration is DI033's finding.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker>(new AsyncWorker());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FactoryConstructingAsyncOnlyDisposable_ReportsDiagnostic()
    {
        // The container creates and tracks whatever a factory returns, so the synchronous
        // disposal throw applies exactly as it does to a type registration.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker>(sp => new AsyncWorker())|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueFactory_NoDiagnostic()
    {
        // Nothing at the registration site proves what this factory builds.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    private static IWorker Create() => new AsyncWorker();

                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IWorker>(sp => Create());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RemovedRegistration_NoDiagnostic()
    {
        // A descriptor removed after it was added never reaches the provider.
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IWorker { }

            public class AsyncWorker : IWorker, IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IWorker, AsyncWorker>();
                    services.RemoveAll<IWorker>();
                }
            }
            """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FactoryWithUserDefinedConversion_NoDiagnostic()
    {
        // The converted result is what the container holds; the constructed temporary is not
        // registered at all, so its disposal story is irrelevant.
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            public class Worker
            {
                public static implicit operator Worker(AsyncOnlyConvertible source) => new Worker();
            }

            public class AsyncOnlyConvertible : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Worker>(sp => new AsyncOnlyConvertible());
                }
            }
            """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task KeyedReplacementWithDifferentKey_DoesNotRemoveRegistration_ReportsDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddKeyedSingleton<IWorker, AsyncWorker>("first")|};
                        services.Replace(
                            ServiceDescriptor.KeyedSingleton<IWorker, SyncWorker>("second")
                        );
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.ReferenceAssembliesWithLatestKeyedDi
        );
    }

    [Fact]
    public async Task ReplacementForDifferentService_DoesNotRemoveRegistration_ReportsDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public interface IOther { }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IOther, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker, AsyncWorker>()|};
                        services.Replace(ServiceDescriptor.Singleton<IOther, SyncWorker>());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ReplacementBeforeRegistration_DoesNotRemoveRegistration_ReportsDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                        {|DI032:services.AddSingleton<IWorker, AsyncWorker>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ReplaceOnDifferentServiceCollection_DoesNotSuppressAsyncOnlyRegistration_ReportsDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var first = new ServiceCollection();
                        var second = new ServiceCollection();
                        {|DI032:first.AddSingleton<IWorker, AsyncWorker>()|};
                        second.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RootRegistrationAndWrapperReplacement_RecognizeTheSameFlow_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public static class RegistrationExtensions
                {
                    public static IServiceCollection ReplaceAsyncWorker(this IServiceCollection services)
                    {
                        services.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                        return services;
                    }
                }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var services = new ServiceCollection();
                        services.AddSingleton<IWorker, AsyncWorker>();
                        services.ReplaceAsyncWorker();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationAndReplacementInsideWrapper_RecognizeTheSameFlow_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public static class RegistrationExtensions
                {
                    public static IServiceCollection AddAndReplace(this IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, AsyncWorker>();
                        services.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                        return services;
                    }
                }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var services = new ServiceCollection();
                        services.AddAndReplace();
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WrapperWithMultipleCollections_RemainsConservative_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public static class RegistrationExtensions
                {
                    public static IServiceCollection AddFirstReplaceSecond(
                        this IServiceCollection first,
                        IServiceCollection second
                    )
                    {
                        first.AddSingleton<IWorker, AsyncWorker>();
                        second.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                        return first;
                    }
                }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var first = new ServiceCollection();
                        var second = new ServiceCollection();
                        first.AddFirstReplaceSecond(second);
                    }
                }
                """;

        // Wrapper parameter flows are not proven across the callsite, so DI032 remains
        // conservative rather than guessing which collection owns the mutation.
        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WrapperCalledOnMultipleFlows_RemainsConservative_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public static class RegistrationExtensions
                {
                    public static IServiceCollection AddAsyncWorker(this IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, AsyncWorker>();
                        return services;
                    }
                }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var first = new ServiceCollection();
                        var second = new ServiceCollection();
                        first.AddAsyncWorker();
                        second.AddAsyncWorker();
                        first.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                    }
                }
                """;

        // Per-call wrapper flow replay is intentionally unproven, so this source-defined
        // wrapper remains conservatively silent.
        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueCollectionCall_DoesNotBreakSameFlowReplacement_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var services = new ServiceCollection();
                        services.GetHashCode();
                        services.AddSingleton<IWorker, AsyncWorker>();
                        services.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task WrapperRegistrationAndRootReplacement_RecognizeTheSameFlow_NoDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public static class RegistrationExtensions
                {
                    public static IServiceCollection AddAsyncWorker(this IServiceCollection services)
                    {
                        services.AddSingleton<IWorker, AsyncWorker>();
                        return services;
                    }
                }

                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices()
                    {
                        var services = new ServiceCollection();
                        services.AddAsyncWorker();
                        services.Replace(ServiceDescriptor.Singleton<IWorker, SyncWorker>());
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task KeyedReplacement_DoesNotRemoveUnkeyedAsyncOnlyDisposable_ReportsDiagnostic()
    {
        var source =
            """
                using Microsoft.Extensions.DependencyInjection.Extensions;

                """
            + Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class SyncWorker : IWorker, IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<IWorker, AsyncWorker>()|};
                        services.Replace(
                            ServiceDescriptor.KeyedSingleton<IWorker, SyncWorker>("key")
                        );
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.ReferenceAssembliesWithLatestKeyedDi
        );
    }

    [Fact]
    public async Task TargetTypedAndParenthesizedFactory_ReportsDiagnostic()
    {
        // `new()` and a parenthesised creation are still single object creations.
        var source =
            Usings
            + """
                public class AsyncWorker : IWorker, IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|DI032:services.AddSingleton<AsyncWorker>(sp => new())|};
                        {|DI032:services.AddScoped<AsyncWorker>(sp => (new AsyncWorker()))|};
                    }
                }
                """;

        await AnalyzerVerifier<DI032_AsyncOnlyDisposableRegistrationAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }
}
