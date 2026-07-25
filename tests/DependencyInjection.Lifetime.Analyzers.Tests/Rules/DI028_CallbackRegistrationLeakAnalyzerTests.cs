using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI028_CallbackRegistrationLeakAnalyzerTests
{
    // DI028 binds the real Options, Hosting, and Primitives symbols rather than source stubs: the
    // shapes under test (extension overloads, metadata-only projections such as
    // IHostApplicationLifetime.ApplicationStopping) are exactly where a hand-written stub would
    // diverge from the framework and pass for the wrong reason. The name-and-namespace fallback that
    // lets a source-declared stub bind is pinned in WellKnownTypesTests instead.
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Options;
        using Microsoft.Extensions.Primitives;

        public class Settings { public string Value { get; set; } = ""; }

        public interface ITokenPublisher { CancellationTokenSource Cts { get; } }

        public class TokenPublisher : ITokenPublisher
        {
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        }

        """;

    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<DI028_CallbackRegistrationLeakAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI028_CallbackRegistrationLeakAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    private static Task VerifySilentAsync(string source) =>
        AnalyzerVerifier<DI028_CallbackRegistrationLeakAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI028_CallbackRegistrationLeakAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    // ----------------------------------------------------------------
    // Positives -- IOptionsMonitor
    // ----------------------------------------------------------------

    [Fact]
    public async Task TransientSubscriber_OptionsMonitorOnChange_MethodGroupInCtor_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        [|monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_OptionsMonitorOnChange_ExtensionOverload_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        [|monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ScopedSubscriber_OptionsMonitorOnChange_InOrdinaryMethod_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<Reloader>();
                }

                public class Reloader
                {
                    private readonly IOptionsMonitor<Settings> _monitor;

                    public Reloader(IOptionsMonitor<Settings> monitor) => _monitor = monitor;

                    public void Start()
                    {
                        [|_monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task DiscardAssignment_OptionsMonitorOnChange_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        _ = [|monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LocalRegistrationNeverReferenced_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        var registration = [|monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegistrationStoredInOtherwiseUnusedPrivateField_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private IDisposable? _registration;

                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        _registration = [|monitor.OnChange(Apply)|];
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task OptionsMonitorOnChange_ExplicitExtensionReorderedNamedArguments_Reports()
    {
        // The receiver is bound through the declared parameters, so a named-argument call that lists
        // the monitor last is still recognized.
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddTransient<Reloader>();
            }

            public class Reloader
            {
                public Reloader(IOptionsMonitor<Settings> monitor)
                {
                    [|OptionsMonitorExtensions.OnChange(listener: Apply, monitor: monitor)|];
                }

                private void Apply(Settings settings) { }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task UserDefinedVoidOnChangeExtension_Silent()
    {
        // The receiver type alone does not make a call a registration: a void extension that invokes
        // the callback immediately hands back no token to discard.
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddTransient<Reloader>();
            }

            public static class LocalMonitorExtensions
            {
                public static void OnChange<T>(this IOptionsMonitor<T> monitor, Action<T> listener) =>
                    listener(monitor.CurrentValue);
            }

            public class Reloader
            {
                public Reloader(IOptionsMonitor<Settings> monitor)
                {
                    LocalMonitorExtensions.OnChange(monitor, Apply);
                }

                private void Apply(Settings settings) { }
            }
            """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Positives -- cancellation tokens
    // ----------------------------------------------------------------

    [Fact]
    public async Task TransientSubscriber_ApplicationStoppingRegister_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|lifetime.ApplicationStopping.Register(OnStopping)|];
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_ApplicationStoppingRegister_StaticCallbackWithThisState_Reports()
    {
        // A static callback still pins the subscriber when `this` is passed as the state argument.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|lifetime.ApplicationStopping.Register(static s => ((Worker)s!).OnStopping(), this)|];
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_UnsafeRegisterOnApplicationStopping_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|lifetime.ApplicationStopping.UnsafeRegister(s => ((Worker)s!).OnStopping(), this)|];
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_SingletonHeldCtsTokenRegister_Reports()
    {
        // Exercises the framework-suffix segment proof: _publisher.Cts is a user projection and
        // .Token is a metadata-only property that the frozen stable-projection proof cannot prove.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<ITokenPublisher, TokenPublisher>();
                        services.AddTransient<Worker>();
                    }
                }

                public class Worker
                {
                    private readonly ITokenPublisher _publisher;

                    public Worker(ITokenPublisher publisher)
                    {
                        _publisher = publisher;
                        [|_publisher.Cts.Token.Register(OnStopping)|];
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ThisCapturingLambda_ApplicationStoppingRegister_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    private int _count;

                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|lifetime.ApplicationStopping.Register(() => _count++)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Positives -- change tokens
    // ----------------------------------------------------------------

    [Fact]
    public async Task TransientSubscriber_ChangeTokenOnChangeOverInjectedSingletonProducer_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IConfiguration _configuration;

                    public Reloader(IConfiguration configuration)
                    {
                        _configuration = configuration;
                        [|ChangeToken.OnChange(() => _configuration.GetReloadToken(), Apply)|];
                    }

                    private void Apply() { }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_RegisterChangeCallbackOnProducerInvocation_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IConfiguration _configuration;

                    public Reloader(IConfiguration configuration)
                    {
                        _configuration = configuration;
                        [|_configuration.GetReloadToken().RegisterChangeCallback(s => ((Reloader)s!).Apply(), this)|];
                    }

                    private void Apply() { }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Positives -- linked token source
    // ----------------------------------------------------------------

    [Fact]
    public async Task LinkedTokenSourceFromApplicationStopping_BareStatement_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LinkedTokenSourceFromApplicationStopping_UnreferencedLocal_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        var linked = [|CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LinkedTokenSourceTokenRegister_ReportsOnce()
    {
        // Both the linking and the Register match one underlying leak; exactly one diagnostic lands.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        [|CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping)|].Token.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Negatives -- lifetime gates
    // ----------------------------------------------------------------

    [Fact]
    public async Task SingletonSubscriber_ApplicationStoppingRegister_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        lifetime.ApplicationStopping.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task HostedServiceSubscriber_ApplicationStoppingRegister_Silent()
    {
        // The idiomatic pattern. If DI028 ever fires here the rule is unusable.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<IHostedService, Worker>();
                }

                public class Worker : IHostedService
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        lifetime.ApplicationStopping.Register(OnStopping);
                    }

                    private void OnStopping() { }

                    public System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken) =>
                        System.Threading.Tasks.Task.CompletedTask;

                    public System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken) =>
                        System.Threading.Tasks.Task.CompletedTask;
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task UnregisteredSubscriber_OptionsMonitorOnChange_Silent()
    {
        var source =
            Prelude
            + """
                public class Reloader
                {
                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        monitor.OnChange(Apply);
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task OptionsSnapshotScopedPublisher_ScopedSubscriber_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<Reloader>();
                }

                public class Reloader
                {
                    public Reloader(IOptionsSnapshot<Settings> snapshot)
                    {
                        var value = snapshot.Value;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task TransientPublisherHoldingCts_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddTransient<ITokenPublisher, TokenPublisher>();
                        services.AddTransient<Worker>();
                    }
                }

                public class Worker
                {
                    private readonly ITokenPublisher _publisher;

                    public Worker(ITokenPublisher publisher)
                    {
                        _publisher = publisher;
                        _publisher.Cts.Token.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task KeyedOnlyPublisher_CtsTokenRegister_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddKeyedSingleton<ITokenPublisher, TokenPublisher>("primary");
                        services.AddTransient<Worker>();
                    }
                }

                public class Worker
                {
                    private readonly ITokenPublisher _publisher;

                    public Worker(ITokenPublisher publisher)
                    {
                        _publisher = publisher;
                        _publisher.Cts.Token.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Negatives -- token provenance
    // ----------------------------------------------------------------

    [Fact]
    public async Task MethodParameterCancellationToken_Register_Silent()
    {
        // An ASP.NET RequestAborted registration is request-scoped and correct.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public void Run(CancellationToken cancellationToken)
                    {
                        cancellationToken.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LocalCancellationTokenSource_TokenRegister_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public void Run()
                    {
                        var cts = new CancellationTokenSource();
                        cts.Token.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task IChangeTokenFieldReceiver_RegisterChangeCallback_Silent()
    {
        // Change tokens are single-shot and replaced on each reload, so a token reached through a
        // field has unprovable provenance and unprovable longevity.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IChangeToken _token;

                    public Reloader(IChangeToken token)
                    {
                        _token = token;
                        _token.RegisterChangeCallback(s => ((Reloader)s!).Apply(), this);
                    }

                    private void Apply() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ChangeTokenOnChange_BlockBodiedProducerLambda_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IConfiguration _configuration;

                    public Reloader(IConfiguration configuration)
                    {
                        _configuration = configuration;
                        ChangeToken.OnChange(
                            () =>
                            {
                                var token = _configuration.GetReloadToken();
                                return token;
                            },
                            Apply);
                    }

                    private void Apply() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task PublisherMemberAssignedFromNew_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<ITokenPublisher, TokenPublisher>();
                        services.AddTransient<Worker>();
                    }
                }

                public class Worker
                {
                    private readonly ITokenPublisher _publisher = new TokenPublisher();

                    public Worker()
                    {
                        _publisher.Cts.Token.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Negatives -- the registration is handled
    // ----------------------------------------------------------------

    [Fact]
    public async Task RegistrationStoredInFieldAndDisposedInDispose_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader : IDisposable
                {
                    private readonly IDisposable _registration;

                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        _registration = monitor.OnChange(Apply);
                    }

                    public void Dispose() => _registration.Dispose();

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task UsingDeclaration_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IOptionsMonitor<Settings> _monitor;

                    public Reloader(IOptionsMonitor<Settings> monitor) => _monitor = monitor;

                    public void Reload()
                    {
                        using var registration = _monitor.OnChange(Apply);
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LocalRegistrationDisposedLater_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IOptionsMonitor<Settings> _monitor;

                    public Reloader(IOptionsMonitor<Settings> monitor) => _monitor = monitor;

                    public void Reload()
                    {
                        var registration = _monitor.OnChange(Apply);
                        registration.Dispose();
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LocalRegistrationReturned_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly IOptionsMonitor<Settings> _monitor;

                    public Reloader(IOptionsMonitor<Settings> monitor) => _monitor = monitor;

                    public IDisposable Reload() => _monitor.OnChange(Apply);

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegistrationPassedToCompositeDisposable_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    private readonly List<IDisposable> _subscriptions = new();

                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        _subscriptions.Add(monitor.OnChange(Apply));
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegistrationStoredInPublicField_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public class Reloader
                {
                    public IDisposable? Registration;

                    public Reloader(IOptionsMonitor<Settings> monitor)
                    {
                        Registration = monitor.OnChange(Apply);
                    }

                    private void Apply(Settings settings, string? name) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LinkedTokenSourceReferencedLater_Silent()
    {
        // The accepted false negative: the local is referenced, so the discard proof refuses it even
        // though nothing disposes the linked source. Proving this needs DI014-class disposal analysis.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public async Task RunAsync(IHostApplicationLifetime lifetime)
                    {
                        var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
                        await Task.Delay(1, linked.Token);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Negatives -- capture and mechanism gates
    // ----------------------------------------------------------------

    [Fact]
    public async Task StaticCallbackWithoutState_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        lifetime.ApplicationStopping.Register(static () => Log());
                    }

                    private static void Log() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task UserDefinedStaticHelperNamedOnChange_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Reloader>();
                }

                public static class NotChangeToken
                {
                    public static IDisposable OnChange(Func<IChangeToken> producer, Action consumer) =>
                        new Dummy();

                    private sealed class Dummy : IDisposable { public void Dispose() { } }
                }

                public class Reloader
                {
                    private readonly IConfiguration _configuration;

                    public Reloader(IConfiguration configuration)
                    {
                        _configuration = configuration;
                        NotChangeToken.OnChange(() => _configuration.GetReloadToken(), Apply);
                    }

                    private void Apply() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task UserDefinedInstanceMethodNamedRegister_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<Registry>();
                        services.AddTransient<Worker>();
                    }
                }

                public class Registry
                {
                    public IDisposable Register(Action callback) => new Dummy();

                    private sealed class Dummy : IDisposable { public void Dispose() { } }
                }

                public class Worker
                {
                    private readonly Registry _registry;

                    public Worker(Registry registry)
                    {
                        _registry = registry;
                        _registry.Register(OnStopping);
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegistrationDisposedInSameChainedCall_Silent()
    {
        // The chain promotion that lets CreateLinkedTokenSource(...).Token.Register(H) resolve must not
        // then classify an immediate disposal as a discard -- that would warn on a remediation.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime)
                    {
                        lifetime.ApplicationStopping.Register(OnStopping).Dispose();
                    }

                    private void OnStopping() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LinkedTokenSource_LongLivedTokenNotFirstArgument_Reports()
    {
        // Linking registers on every token, so the verdict must not depend on argument order.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<Worker>();
                }

                public class Worker
                {
                    public Worker(IHostApplicationLifetime lifetime, CancellationToken requestToken)
                    {
                        [|CancellationTokenSource.CreateLinkedTokenSource(requestToken, lifetime.ApplicationStopping)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Cross-rule non-duplication
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventSubscriptionOnSingleton_DI028_Silent()
    {
        // DI025's canonical positive. DI028 fires only on invocations, never on += .
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<Bus>();
                        services.AddTransient<Handler>();
                    }
                }

                public class Bus
                {
                    public event Action? Changed;

                    public void Raise() => Changed?.Invoke();
                }

                public class Handler
                {
                    public Handler(Bus bus)
                    {
                        bus.Changed += OnChanged;
                    }

                    private void OnChanged() { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ObservableSubscribeOnSingleton_DI028_Silent()
    {
        // DI027's territory: the name allowlist excludes Subscribe.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<ITicker, Ticker>();
                        services.AddTransient<TickHandler>();
                    }
                }

                public interface ITicker : IObservable<int> { }

                public class Ticker : ITicker
                {
                    public IDisposable Subscribe(IObserver<int> observer) => new Dummy();

                    private sealed class Dummy : IDisposable { public void Dispose() { } }
                }

                public class TickHandler : IObserver<int>
                {
                    public TickHandler(ITicker ticker)
                    {
                        ticker.Subscribe(this);
                    }

                    public void OnCompleted() { }

                    public void OnError(Exception error) { }

                    public void OnNext(int value) { }
                }
                """;

        await VerifySilentAsync(source);
    }
}
