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
    public async Task FieldHeldCollection_NoDiagnostic()
    {
        // Codex round 5: a field is reachable from every other member of the type, any of which
        // can build it again, so one code block cannot prove the registration lost.
        var source =
            Usings
            + """
                public class Composition
                {
                    private readonly IServiceCollection _services = new ServiceCollection();

                    public IServiceProvider Build()
                    {
                        var provider = _services.BuildServiceProvider();
                        _services.AddSingleton<IAudit, Audit>();
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
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

    [Fact]
    public async Task UnrelatedBuildOnHostBuilderContract_NoDiagnostic()
    {
        // Codex round 2: a type can implement a builder contract and still declare a Build of
        // its own that creates no provider.
        var source =
            Usings
            + """
                public interface ICustomBuilder
                {
                    IServiceCollection Services { get; }
                }

                public sealed class CustomBuilder : ICustomBuilder
                {
                    public IServiceCollection Services { get; } = new ServiceCollection();

                    public object Build() => new object();
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var builder = new CustomBuilder();
                        var result = builder.Build();
                        builder.Services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BuildInsideConditionalExpression_NoDiagnostic()
    {
        // Codex round 2: the declaration statement is unconditional, but the build itself sits
        // in a ternary arm that may never be evaluated.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure(bool reuse, IServiceProvider existing)
                    {
                        var services = new ServiceCollection();
                        var provider = reuse ? existing : services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AliasFoldedThroughComputedProperty_NoDiagnostic()
    {
        // Codex round 2: a property getter can hand back a fresh collection per access, so a
        // local capturing one result is not an alias of the property path.
        var source =
            Usings
            + """
                public sealed class Holder
                {
                    public IServiceCollection Services => new ServiceCollection();
                }

                public class Composition
                {
                    public void Configure(Holder holder)
                    {
                        var snapshot = holder.Services;
                        var provider = snapshot.BuildServiceProvider();
                        holder.Services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SecondaryCollectionArgumentOfRegistration_NoDiagnostic()
    {
        // Codex round 2: only the receiver argument of a registration call is exempt from the
        // hand-off scan; another collection passed alongside it can still be rebuilt.
        var source =
            Usings
            + """
                public static class BridgeExtensions
                {
                    public static IServiceCollection AddBridge(
                        this IServiceCollection target,
                        IServiceCollection source) => target;
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var primary = new ServiceCollection();
                        var secondary = new ServiceCollection();
                        var provider = primary.BuildServiceProvider();
                        primary.AddSingleton<IAudit, Audit>();
                        secondary.AddBridge(primary);
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task TupleDeconstructionReassignment_NoDiagnostic()
    {
        // Codex round 2: a deconstructing assignment writes each element, so the name at the
        // registration no longer denotes the collection that was built.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceCollection Configure(IServiceCollection fresh)
                    {
                        IServiceCollection services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        (services, fresh) = (fresh, services);
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
    public async Task ThirdPartyExtensionReturningWrapper_NoDiagnostic()
    {
        // Codex round 2: a third-party AddXxx answering with a wrapper may have built a provider
        // of its own and handed it back inside the result.
        var source =
            Usings
            + """
                public sealed class BuildResult
                {
                    public IServiceProvider Provider { get; set; } = null!;
                }

                public static class WrapperExtensions
                {
                    public static BuildResult AddAndBuild(this IServiceCollection services) =>
                        new BuildResult { Provider = services.BuildServiceProvider() };
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        var current = services.AddAndBuild().Provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FrameworkBuilderReturningExtension_ReportsDiagnostic()
    {
        // The wrapper exclusion is namespace-scoped: a framework AddXxx returning a builder is
        // still a registration.
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IAuditBuilder { }

                    public static class AuditServiceCollectionExtensions
                    {
                        public static IAuditBuilder AddAuditing(this IServiceCollection services) =>
                            null!;
                    }
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.AddAuditing()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task LocalAliasOfBuilderServicesRebuilt_NoDiagnostic()
    {
        // Codex round 3: a local capturing `builder.Services` is that collection, so the later
        // `builder.Build()` does pick the registration up.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;

            public interface IAudit { }

            public class Audit : IAudit { }

            public class Composition
            {
                public void Configure()
                {
                    var builder = new HostApplicationBuilder();
                    var services = builder.Services;
                    var first = services.BuildServiceProvider();
                    services.AddSingleton<IAudit, Audit>();
                    var host = builder.Build();
                }
            }
            """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            HostingReferences
        );
    }

    [Fact]
    public async Task NamedArgumentsReorderStaticSpelling_NoDiagnostic()
    {
        // Codex round 3: named arguments can put the receiver anywhere in the list, so position
        // must not decide which collection the call mutates.
        var source =
            Usings
            + """
                public static class FeatureExtensions
                {
                    public static IServiceCollection AddFeature(
                        this IServiceCollection services,
                        IServiceCollection source) => services;
                }

                public class Composition
                {
                    public IServiceProvider Configure()
                    {
                        var stale = new ServiceCollection();
                        var live = new ServiceCollection();
                        var first = stale.BuildServiceProvider();
                        FeatureExtensions.AddFeature(source: stale, services: live);
                        return live.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RecognisedNameReturningWrapper_NoDiagnostic()
    {
        // Codex round 3: the wrapper suppression must apply to recognised registration names
        // too, not only to prefix-matched ones.
        var source =
            Usings
            + """
                public sealed class Runtime
                {
                    public Runtime(IServiceProvider provider) => Provider = provider;

                    public IServiceProvider Provider { get; }
                }

                public static class RuntimeExtensions
                {
                    public static Runtime Configure(this IServiceCollection services)
                    {
                        services.AddSingleton<IAudit, Audit>();
                        return new Runtime(services.BuildServiceProvider());
                    }
                }

                public class Composition
                {
                    public void Build()
                    {
                        var services = new ServiceCollection();
                        var first = services.BuildServiceProvider();
                        var live = services.Configure();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CastAroundLaterRebuild_NoDiagnostic()
    {
        // Codex round 3: a cast in the path must not hide that the later build is on the same
        // collection.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var first = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return ((IServiceCollection)services).BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RebuildThroughCapturingLocalFunction_NoDiagnostic()
    {
        // Codex round 3: a build inside a local function has no fixed position — the call can
        // come after the registration however early the declaration sits.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        IServiceProvider Rebuild() => services.BuildServiceProvider();

                        var first = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return Rebuild();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UserDefinedConversionInPath_NoDiagnostic()
    {
        // Codex round 4: a user-defined conversion is a method call in disguise and may hand
        // back a different collection on each evaluation.
        var source =
            Usings
            + """
                public sealed class Source
                {
                    public ServiceCollection Live { get; } = new ServiceCollection();

                    public static explicit operator ServiceCollection(Source source) =>
                        new ServiceCollection();
                }

                public class Composition
                {
                    public void Configure(Source source)
                    {
                        var provider = ((ServiceCollection)source).BuildServiceProvider();
                        ((ServiceCollection)source).AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionReturnedToCaller_NoDiagnostic()
    {
        // Codex round 4: the caller receives the collection and can build it after this method
        // has run, so the registration is not proven lost.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceCollection Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
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
    public async Task ComputedServicesProperty_NoDiagnostic()
    {
        // Codex round 4: reading a computed getter twice can yield two different collections, so
        // the property does not identify one.
        var source =
            Usings
            + """
                public class Composition
                {
                    private int _reads;
                    private readonly IServiceCollection _probe = new ServiceCollection();
                    private readonly IServiceCollection _live = new ServiceCollection();

                    private IServiceCollection Services => _reads++ == 0 ? _probe : _live;

                    public IServiceProvider Build()
                    {
                        var probe = Services.BuildServiceProvider();
                        Services.AddSingleton<IAudit, Audit>();
                        return _live.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonDescriptorAddOverload_NoDiagnostic()
    {
        // Codex round 4: an IServiceCollection implementation may add same-named overloads of
        // its own that touch no descriptor at all.
        var source =
            Usings
            + """
                public sealed class AuditingCollection : List<ServiceDescriptor>, IServiceCollection
                {
                    public void Add(string message) { }
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new AuditingCollection();
                        var provider = services.BuildServiceProvider();
                        services.Add("audit enabled");
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PropertyHeldCollection_NoDiagnostic()
    {
        // Codex round 5: a property-held collection is reachable from every other member, and a
        // settable one can even be replaced between the build and the registration.
        var source =
            Usings
            + """
                public class Composition
                {
                    private IServiceCollection Services { get; set; } = new ServiceCollection();

                    private void Reset() => Services = new ServiceCollection();

                    public IServiceProvider Build()
                    {
                        var provider = Services.BuildServiceProvider();
                        Reset();
                        Services.AddSingleton<IAudit, Audit>();
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CallerOwnedCollectionParameter_NoDiagnostic()
    {
        // Codex round 5: the caller owns the collection and can build the live provider after
        // this helper returns. Building a provider mid-registration is DI016's claim, not this
        // rule's.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        var probe = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionCapturedByReturnedLambda_NoDiagnostic()
    {
        // Codex round 6: the delegate hands the collection to the caller, which can build it.
        var source =
            Usings
            + """
                public class Composition
                {
                    public Func<IServiceCollection> Configure()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return () => services;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionReturnedInsideTuple_NoDiagnostic()
    {
        // Codex round 6: the tuple's own type is not the collection type, but the collection
        // still leaves the method inside it.
        var source =
            Usings
            + """
                public class Composition
                {
                    public (IServiceCollection Services, IDisposable Snapshot) Configure()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return (services, snapshot);
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionYielded_NoDiagnostic()
    {
        // Codex round 6: an iterator hands the collection out one element at a time.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IEnumerable<IServiceCollection> Configure()
                    {
                        var services = new ServiceCollection();
                        var snapshot = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        yield return services;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SecondRegistrationAfterBuild_ReportsBoth()
    {
        // A further registration is not an escape: both are lost, and both are reported.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceProvider Build()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.AddSingleton<IAudit, Audit>()|};
                        {|DI036:services.AddScoped<IReporting, Reporting>()|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CollectionSavedToFieldBeforeBuild_NoDiagnostic()
    {
        // Codex round 7: a reference handed out before the build is still live when the
        // registration runs, so position does not make the escape harmless.
        var source =
            Usings
            + """
                public class Composition
                {
                    private IServiceCollection _saved = null!;

                    public IServiceProvider Configure()
                    {
                        var services = new ServiceCollection();
                        _saved = services;
                        var probe = services.BuildServiceProvider();
                        services.AddSingleton<IAudit, Audit>();
                        return _saved.BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ExtensionThatBuildsItsOwnProvider_NoDiagnostic()
    {
        // Codex round 7: a void helper can still build and retain a provider internally, which
        // does see what the helper just registered.
        var source =
            Usings
            + """
                public static class RuntimeExtensions
                {
                    public static IServiceProvider Current = null!;

                    public static void AddRuntime(this IServiceCollection services)
                    {
                        services.AddSingleton<IAudit, Audit>();
                        Current = services.BuildServiceProvider();
                    }
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        services.AddRuntime();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InSourceExtensionThatBuildsNothing_ReportsDiagnostic()
    {
        // The build-check is what excludes a helper, not the fact that it is user-written: a
        // custom AddXxx that only registers keeps its coverage.
        var source =
            Usings
            + """
                public static class AuditExtensions
                {
                    public static IServiceCollection AddAuditing(this IServiceCollection services)
                    {
                        services.AddSingleton<IAudit, Audit>();
                        return services;
                    }
                }

                public class Composition
                {
                    public IServiceProvider Configure()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:services.AddAuditing()|};
                        return provider;
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ExtensionBuildingThroughProviderFactory_NoDiagnostic()
    {
        // Codex round 8: building a container is spelled more than one way.
        var source =
            Usings
            + """
                public static class RuntimeExtensions
                {
                    public static IServiceProvider Current = null!;

                    public static IServiceCollection AddRuntime(this IServiceCollection services)
                    {
                        services.AddSingleton<IAudit, Audit>();
                        Current = new DefaultServiceProviderFactory().CreateServiceProvider(
                            services
                        );
                        return services;
                    }
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        services.AddRuntime();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomCollectionForwardingAdd_NoDiagnostic()
    {
        // Codex round 8: a user implementation of IServiceCollection can forward the descriptor
        // straight into a container that is already live.
        var source =
            Usings
            + """
                public sealed class LiveServices : List<ServiceDescriptor>, IServiceCollection
                {
                    public new void Add(ServiceDescriptor descriptor) => base.Add(descriptor);
                }

                public class Composition
                {
                    public void Configure()
                    {
                        var services = new LiveServices();
                        var probe = services.BuildServiceProvider();
                        services.Add(ServiceDescriptor.Singleton<IAudit, Audit>());
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FluentRebuildAfterRegistration_NoDiagnostic()
    {
        // Codex round 9: the registration is chained straight into a second build, so the
        // provider that is kept holds it.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        var provider = services
                            .AddSingleton<IAudit, Audit>()
                            .BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task EarlierRegistrationObservedByFluentRebuild_NoDiagnostic()
    {
        // Codex round 9: the fluent rebuild is the last word on the collection, so every earlier
        // registration reaches it too.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        services.AddScoped<IReporting, Reporting>();
                        var provider = services
                            .AddSingleton<IAudit, Audit>()
                            .BuildServiceProvider();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegistrationResultCarriedAway_NoDiagnostic()
    {
        // Codex round 9: the registration hands its collection to a caller, which is free to
        // build it again.
        var source =
            Usings
            + """
                public class Composition
                {
                    public IServiceCollection Configure()
                    {
                        var services = new ServiceCollection();
                        var probe = services.BuildServiceProvider();
                        return services.AddSingleton<IAudit, Audit>();
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ChainedRegistrationsAfterBuild_ReportsEach()
    {
        // A chain that is discarded loses every link in it, and the framework extensions in the
        // chain provably hand back the same collection.
        var source =
            Usings
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var services = new ServiceCollection();
                        var provider = services.BuildServiceProvider();
                        {|DI036:{|DI036:services.AddScoped<IReporting, Reporting>()|}.AddSingleton<IAudit, Audit>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }
}
