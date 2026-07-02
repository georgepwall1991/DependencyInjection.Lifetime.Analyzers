using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI025_EventSubscriptionLeakAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public interface IBus
        {
            event EventHandler MessageReceived;
        }

        public class Bus : IBus
        {
            public event EventHandler MessageReceived;
        }

        public static class GlobalEvents
        {
            public static event EventHandler Changed;
        }

        """;

    private const string SingletonBusTransientHandlerRegistrations = """
        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<IBus, Bus>();
                services.AddTransient<OrderHandler>();
            }
        }

        """;

    // ----------------------------------------------------------------
    // Positives: transient/scoped subscriber, singleton/static publisher
    // ----------------------------------------------------------------

    [Fact]
    public async Task TransientSubscriber_InjectedFieldSingletonPublisher_MethodGroupInCtor_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedSubscriber_InjectedFieldSingletonPublisher_MethodGroupInOrdinaryMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddScoped<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus) => _bus = bus;

                public void Initialize()
                {
                    [|_bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_CtorParameterReceiver_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_StaticEvent_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler()
                {
                    [|GlobalEvents.Changed += OnChanged|];
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_UnstorableLambdaCapturingThis_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private int _count;

                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += (sender, e) => _count++|];
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_LambdaCallingInstanceMethod_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += (sender, e) => Handle()|];
                }

                private void Handle() { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_StoredInstanceMethodGroupDelegateField_NoUnsubscribe_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly EventHandler _handler;

                public OrderHandler(IBus bus)
                {
                    _handler = OnMessage;
                    [|bus.MessageReceived += _handler|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_UnsubscribeUsesDifferentLambdaInstance_StillReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += (sender, e) => Handle()|];
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= (sender, e) => Handle();
                }

                private void Handle() { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SubscriptionInBaseClass_DerivedRegisteredTransient_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<DerivedHandler>();
                }
            }

            public abstract class HandlerBase
            {
                protected HandlerBase(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                protected abstract void OnMessage(object sender, EventArgs e);
            }

            public class DerivedHandler : HandlerBase
            {
                public DerivedHandler(IBus bus) : base(bus) { }

                protected override void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConcreteClassPublisherRegisteredSingleton_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(Bus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetOnlyPropertyPublisher_AssignedFromCtorParameter_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private IBus Bus { get; }

                public OrderHandler(IBus bus)
                {
                    Bus = bus;
                    [|Bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ThisQualifiedFieldReceiver_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    this._bus = bus;
                    [|this._bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DelegateCreationWrappedMethodGroup_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += new EventHandler(OnMessage)|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalAliasOfInjectedField_ReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus) => _bus = bus;

                public void Initialize()
                {
                    var bus = _bus;
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Lattice negatives: subscriber/publisher rank combinations
    // ----------------------------------------------------------------

    [Fact]
    public async Task SingletonSubscriber_SingletonPublisher_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddSingleton<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonSubscriber_StaticEvent_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler()
                {
                    GlobalEvents.Changed += OnChanged;
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HostedServiceSubscriber_SingletonPublisher_NoDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.Extensions.Hosting
            {
                using System.Threading;
                using System.Threading.Tasks;

                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);
                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, Worker>();
                }
            }

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                public Worker(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }

                public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;

                public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedPublisher_TransientSubscriber_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientPublisher_TransientSubscriber_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedSubscriber_ScopedPublisher_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IBus, Bus>();
                    services.AddScoped<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredSubscriberType_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredPublisherType_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SubscriberRegisteredTransientAndSingleton_MaxRankWins_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                    services.AddSingleton<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Handler-shape negatives
    // ----------------------------------------------------------------

    [Fact]
    public async Task StaticMethodGroupHandler_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private static void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LambdaWithoutInstanceReferences_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += (sender, e) => Console.WriteLine("received");
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ParameterProvidedDelegateHandler_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public OrderHandler(IBus bus, EventHandler handler)
                {
                    bus.MessageReceived += handler;
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DelegateFieldAssignedFromCtorParameter_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly EventHandler _handler;

                public OrderHandler(IBus bus, EventHandler handler)
                {
                    _handler = handler;
                    bus.MessageReceived += _handler;
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LambdaReferencingOnlyStaticMembers_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private static int _count;

                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += (sender, e) => _count++;
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CompoundAssignmentOnNonEvent_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private int _count;

                public OrderHandler(IBus bus)
                {
                    _count += 5;
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Unsubscribe proofs
    // ----------------------------------------------------------------

    [Fact]
    public async Task MatchingUnsubscribeInDispose_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MatchingUnsubscribeInDisposeAsync_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IAsyncDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    _bus.MessageReceived -= OnMessage;
                    return default;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MatchingUnsubscribeInArbitraryTeardownMethod_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Detach()
                {
                    _bus.MessageReceived -= OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SubscribeViaCtorParameter_UnsubscribeViaFieldAssignedFromSameParameter_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StoredLambdaField_SubscribedAndUnsubscribedViaSameField_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;
                private readonly EventHandler _handler;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _handler = (sender, e) => Handle();
                    _bus.MessageReceived += _handler;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= _handler;
                }

                private void Handle() { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ResubscribeIdiom_UnsubscribeThenSubscribeSameHandler_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus) => _bus = bus;

                public void Refresh()
                {
                    _bus.MessageReceived -= OnMessage;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnsubscribeWithDifferentMethodGroup_StillReportsDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnOther;
                }

                private void OnMessage(object sender, EventArgs e) { }

                private void OnOther(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnsubscribeOnDifferentEvent_StillReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IBus
            {
                event EventHandler MessageReceived;
                event EventHandler Faulted;
            }

            public class Bus : IBus
            {
                public event EventHandler MessageReceived;
                public event EventHandler Faulted;
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                public void Dispose()
                {
                    _bus.Faulted -= OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnsubscribeInSourceVisibleBaseClassDispose_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<DerivedHandler>();
                }
            }

            public abstract class HandlerBase : IDisposable
            {
                protected readonly IBus Bus;

                protected HandlerBase(IBus bus) => Bus = bus;

                public void Dispose()
                {
                    Bus.MessageReceived -= OnMessage;
                }

                protected abstract void OnMessage(object sender, EventArgs e);
            }

            public class DerivedHandler : HandlerBase
            {
                public DerivedHandler(IBus bus) : base(bus)
                {
                    Bus.MessageReceived += OnMessage;
                }

                protected override void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Silence-on-unknown
    // ----------------------------------------------------------------

    [Fact]
    public async Task ChainedReceiver_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public class Inner
            {
                public event EventHandler Changed;
            }

            public interface IOuter
            {
                Inner Inner { get; }
            }

            public class Outer : IOuter
            {
                public Inner Inner { get; } = new Inner();
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IOuter, Outer>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IOuter outer)
                {
                    outer.Inner.Changed += OnChanged;
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FieldAssignedFromNewExpression_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private readonly Bus _bus;

                public OrderHandler()
                {
                    _bus = new Bus();
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FieldAssignedFromOrdinaryMethodParameter_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private IBus _bus;

                public void SetBus(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FieldWithMixedAssignmentOrigins_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                private IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Reset()
                {
                    _bus = new Bus();
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalFromMethodCall_NoDiagnostic()
    {
        var source = Usings + SingletonBusTransientHandlerRegistrations + """
            public class OrderHandler
            {
                public void Initialize()
                {
                    var bus = GetBus();
                    bus.MessageReceived += OnMessage;
                }

                private IBus GetBus() => new Bus();

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactoryRegisteredSubscriber_UnknownImplementation_NoDiagnostic()
    {
        var source = Usings + """
            public interface IHandler { }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<IHandler>(sp => CreateHandler(sp));
                }

                private static IHandler CreateHandler(IServiceProvider sp) => null;
            }

            public class OrderHandler : IHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Registration-shape matrix
    // ----------------------------------------------------------------

    [Fact]
    public async Task TryAddSingletonPublisher_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IBus
            {
                event EventHandler MessageReceived;
            }

            public class Bus : IBus
            {
                public event EventHandler MessageReceived;
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.TryAddSingleton<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorSingletonPublisher_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Singleton(typeof(IBus), typeof(Bus)));
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task FactorySingletonPublisher_LifetimeKnownFromServiceType_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus>(sp => new Bus());
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SubscriberRegisteredViaTypeofSelfBinding_ReportsDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient(typeof(OrderHandler));
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingletonPublisher_ConstructedInjection_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IEventBus<T>
            {
                event EventHandler MessageReceived;
            }

            public class EventBus<T> : IEventBus<T>
            {
                public event EventHandler MessageReceived;
            }

            public class Order { }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IEventBus<>), typeof(EventBus<>));
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IEventBus<Order> bus)
                {
                    [|bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericScopedPublisher_ConstructedInjection_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IEventBus<T>
            {
                event EventHandler MessageReceived;
            }

            public class EventBus<T> : IEventBus<T>
            {
                public event EventHandler MessageReceived;
            }

            public class Order { }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped(typeof(IEventBus<>), typeof(EventBus<>));
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IEventBus<Order> bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClosedRegistrationOverridesOpenGeneric_ForSameConstructedPublisher_NoDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IEventBus<T>
            {
                event EventHandler MessageReceived;
            }

            public class EventBus<T> : IEventBus<T>
            {
                public event EventHandler MessageReceived;
            }

            public class Order { }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IEventBus<>), typeof(EventBus<>));
                    services.AddScoped<IEventBus<Order>, EventBus<Order>>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IEventBus<Order> bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // DI002 boundary: a handler chained to a service resolved inside an
    // explicit scope block is DI002's line, never DI025's.
    // ----------------------------------------------------------------

    [Fact]
    public async Task ScopeResolvedServiceHandler_IsDi002Territory_NoDi025Diagnostic()
    {
        var source = Usings + """
            public interface IWorker
            {
                void Handle(object sender, EventArgs e);
            }

            public class Worker : IWorker
            {
                public void Handle(object sender, EventArgs e) { }
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddScoped<IWorker, Worker>();
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                private readonly IBus _bus;
                private readonly IServiceScopeFactory _scopeFactory;

                public OrderHandler(IBus bus, IServiceScopeFactory scopeFactory)
                {
                    _bus = bus;
                    _scopeFactory = scopeFactory;
                }

                public void Wire()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var worker = scope.ServiceProvider.GetRequiredService<IWorker>();
                    _bus.MessageReceived += worker.Handle;
                }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedSingletonPublisherOnly_UnkeyedInjection_NoDiagnostic()
    {
        var source = Usings + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IBus, Bus>("primary");
                    services.AddTransient<OrderHandler>();
                }
            }

            public class OrderHandler
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI025_EventSubscriptionLeakAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }
}
