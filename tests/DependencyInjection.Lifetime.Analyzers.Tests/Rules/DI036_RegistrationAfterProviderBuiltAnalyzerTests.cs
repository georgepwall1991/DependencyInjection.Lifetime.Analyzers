using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
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
        var source =
            Usings
            + HostBuilderStub
            + """
                public class Composition
                {
                    public void Configure()
                    {
                        var builder = new AppBuilder();
                        builder.Services.AddScoped<IReporting, Reporting>();
                        var app = builder.Build();
                        {|DI036:builder.Services.AddSingleton<IAudit, Audit>()|};
                    }
                }
                """;

        await AnalyzerVerifier<DI036_RegistrationAfterProviderBuiltAnalyzer>.VerifyDiagnosticsAsync(
            source
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
}
