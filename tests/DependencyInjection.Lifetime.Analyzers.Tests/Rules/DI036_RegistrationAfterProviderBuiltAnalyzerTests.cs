using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI036_RegistrationAfterProviderBuiltAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        public interface IReporting { }

        public interface IAudit { }

        public class Reporting : IReporting { }

        public class Audit : IAudit { }

        """;

    /// <summary>
    /// The host-builder leg needs the real <c>HostApplicationBuilder</c>: DI036 recognises only
    /// the framework builder contracts, so a stub cannot stand in for it.
    /// </summary>
    private static readonly ReferenceAssemblies HostingReferences =
        ReferenceAssemblies.Net.Net80.AddPackages(
            [
                new PackageIdentity(
                    "Microsoft.Extensions.DependencyInjection.Abstractions",
                    "10.0.2"
                ),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "10.0.2"),
                new PackageIdentity("Microsoft.Extensions.Hosting", "10.0.2"),
            ]
        );

    private const string HostBuilderStub = """
        public sealed class AppBuilder
        {
            public IServiceCollection Services { get; } = new ServiceCollection();

            public object Build() => new object();
        }

        """;

    [Fact]
    public async Task RegistrationAfterBuildServiceProvider_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        services.AddScoped<IReporting, Reporting>();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.AddSingleton<IAudit, Audit>()|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationAfterHostBuilderBuild_ReportsDiagnostic()
    {
        // `using` clauses must precede the type declarations `Usings` carries, so this source is
        // self-contained.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;

            public interface IReporting { }

            public interface IAudit { }

            public class Reporting : IReporting { }

            public class Audit : IAudit { }

            public class Composition
            {
                public void Configure()
                {
                    var builder = new HostApplicationBuilder();
                    builder.Services.AddScoped<IReporting, Reporting>();
                    var host = builder.Build();
                    {|DI036:builder.Services.AddSingleton<IAudit, Audit>()|};
                }
            }
            """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            HostingReferences
        );
    }

    [Fact]
    public async Task RegistrationInsideBranchAfterUnconditionalBuild_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build(bool auditing)
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        if (auditing)
                        {
                            {|DI036:services.AddSingleton<IAudit, Audit>()|};
                        }

                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DescriptorAddAfterBuild_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.Add(ServiceDescriptor.Singleton<IAudit, Audit>())|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ReplaceAfterBuild_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        services.AddSingleton<IAudit, Audit>();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.Replace(ServiceDescriptor.Scoped<IAudit, Audit>())|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task StaticExtensionSpellingAfterBuild_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:ServiceCollectionServiceExtensions.AddSingleton<IAudit, Audit>(services)|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FieldHeldCollectionRegisteredAfterBuild_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    private readonly IServiceCollection _services = new ServiceCollection();

                    public IServiceProvider Build()
                    {
                        var provider = _services.BuildServiceProvider();
                        {|DI036:_services.AddSingleton<IAudit, Audit>()|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TopLevelStatementRegistrationAfterBuild_ReportsDiagnostic()
    {
        // Types must follow the top-level statements, so this source cannot reuse `Usings`.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();
            {|DI036:services.AddSingleton<IAudit, Audit>()|};

            public interface IAudit { }

            public class Audit : IAudit { }
            """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsConsoleApplicationAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationBeforeBuild_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        services.AddSingleton<IAudit, Audit>();
                        return services.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task LaterRebuildPicksRegistrationUp_NoDiagnostic()
    {
        // Guards the later-build escape: the second provider does see the registration.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return services.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ConditionalBuildBeforeRegistration_NoDiagnostic()
    {
        // Guards the dominance check: the build may never run, so the registration may be live.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure(bool probe)
                    {
                        var services = new ServiceCollection();
                        if (probe)
                        {
                            var provider = services.BuildServiceProvider();
                        }

                        services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BuildAndRegistrationInsideLoop_NoDiagnostic()
    {
        // Guards the loop check: the next iteration builds again after this registration.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure(int count)
                    {
                        var services = new ServiceCollection();
                        for (var i = 0; i < count; i++)
                        {
                            var provider = services.BuildServiceProvider();
                            services.AddSingleton<IAudit, Audit>();
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationInsideLambda_NoDiagnostic()
    {
        // Guards the function boundary: the delegate can run before the build.
        var source =
            Usings
            + """
                public class Composition
                {
                    public Action Configure()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        return () => services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task DifferentCollectionRegisteredAfterBuild_NoDiagnostic()
    {
        // Guards collection identity: the built collection is not the mutated one.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var probeServices = new ServiceCollection();
                        var hostServices = new ServiceCollection();
                        var provider = probeServices.BuildServiceProvider();
                        hostServices.AddSingleton<IAudit, Audit>();
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ClearAfterBuild_NoDiagnostic()
    {
        // Guards the mutation list: resetting a fixture's collection loses no registration.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        services.AddSingleton<IAudit, Audit>();
                        var provider = services.BuildServiceProvider();
                        services.Clear();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionReachedThroughMethodCall_NoDiagnostic()
    {
        // Guards path stability: each call can hand back a different collection.
        var source =
            Usings
            + """
                public class Composition
                {
                    private IServiceCollection Next() => new ServiceCollection();

                    public void Configure()
                    {
                        var provider = Next().BuildServiceProvider();
                        Next().AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task GotoInMethod_NoDiagnostic()
    {
        // Guards the jump bail-out: a back edge can run the registration before the build.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure(bool again)
                    {
                        var services = new ServiceCollection();
                    start:
                        services.AddScoped<IReporting, Reporting>();
                        var provider = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        if (again)
                        {
                            again = false;
                            goto start;
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BuildInSeparateMethod_NoDiagnostic()
    {
        // Guards the code-block scope: cross-method order is not proven here.
        var source =
            Usings
            + """
                public class Composition
                {
                    private readonly IServiceCollection _services = new ServiceCollection();

                    public IServiceProvider Build() => _services.BuildServiceProvider();

                    public void AddAudit() => _services.AddSingleton<IAudit, Audit>();
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task HostBuilderWithoutServicesCollection_NoDiagnostic()
    {
        // An unrelated Build() names no service collection, so it freezes nothing. This pins the
        // host-builder shape against future broadening rather than isolating a single guard.
        var source =
            Usings
            + """
                public sealed class QueryBuilder
                {
                    public string Build() => string.Empty;
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var query = new QueryBuilder().Build();
                        services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UnrelatedBuilderExposingServices_NoDiagnostic()
    {
        // Codex round 1: a type that merely has an `IServiceCollection Services` property and a
        // `Build()` freezes nothing — its provider may be created later from the live collection.
        var source =
            Usings
            + HostBuilderStub
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var builder = new AppBuilder();
                        var report = builder.Build();
                        builder.Services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomBuildServiceProviderExtension_NoDiagnostic()
    {
        // Codex round 1: a same-named community extension may hand back a live view of the
        // collection rather than a snapshot, so the registration still reaches it.
        var source =
            Usings
            + """
                public static class LiveProviderExtensions
                {
                    public static IServiceProvider BuildServiceProvider(
                        this IServiceCollection services,
                        int mode) => null!;
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var live = services.BuildServiceProvider(1);
                        services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ReadOnlyAddPrefixedExtension_NoDiagnostic()
    {
        // Codex round 1: an `AddXxx` extension answering with a scalar is a query over the
        // collection, not a registration, so there is nothing to lose.
        var source =
            Usings
            + """
                public static class InspectionExtensions
                {
                    public static int AddCount(this IServiceCollection services) => services.Count;
                }

                public class Composition
                {
                    public int Configure()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        return services.AddCount();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ExtensionReturningProvider_NoDiagnostic()
    {
        // Codex round 1: a call that hands back a provider builds one itself, so whatever it
        // registers reaches the provider it returns.
        var source =
            Usings
            + """
                public static class ProviderExtensions
                {
                    public static ServiceProvider AddAuditProvider(this IServiceCollection services)
                    {
                        services.AddSingleton<IAudit, Audit>();
                        return services.BuildServiceProvider();
                    }
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        var live = services.AddAuditProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RemoveAfterBuild_NoDiagnostic()
    {
        // Codex round 1: stripping a descriptor back out is teardown, not a lost registration.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var descriptor = ServiceDescriptor.Singleton<IAudit, Audit>();
                        services.Add(descriptor);
                        var provider = services.BuildServiceProvider();
                        services.Remove(descriptor);
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AliasedRebuildCancels_NoDiagnostic()
    {
        // Codex round 1: the later build goes through an alias of the same collection, so the
        // registration does reach a provider.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        IServiceCollection services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        IServiceCollection alias = services;
                        return alias.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ReassignedCollection_NoDiagnostic()
    {
        // Codex round 1: the name denotes a different collection at the registration than it did
        // at the build, so the build proves nothing about it.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceCollection Configure()
                    {
                        IServiceCollection services = new ServiceCollection();
                        var discarded = services.BuildServiceProvider();
                        services = new ServiceCollection();
                        services.AddSingleton<IAudit, Audit>();
                        return services;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionHandedToHelperAfterRegistration_NoDiagnostic()
    {
        // Codex round 1: the helper can build the collection again out of sight of this block.
        var source =
            Usings
            + """
                public class Composition
                {
                    private static ServiceProvider CreateProvider(IServiceCollection services) =>
                        services.BuildServiceProvider();

                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return CreateProvider(services);
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }
}
