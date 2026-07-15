using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI027_RxSubscriptionLeakAnalyzerTests
{
    // A minimal Rx surface: System.IObservable<T>/IObserver<T> already live in the BCL, so the
    // only stub needed is the Action-based Subscribe extension that System.Reactive ships. Declaring
    // it in namespace System (name + containing namespace) makes the real System.Reactive bind
    // identically when the sample is compiled against it.
    private const string Prelude = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        namespace System
        {
            public static class RxStub
            {
                public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext) => new Token();
                public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext, Action<Exception> onError) => new Token();
                private sealed class Token : IDisposable { public void Dispose() { } }
            }
        }

        public interface ITicker : IObservable<int> { }

        public class Ticker : ITicker
        {
            public IDisposable Subscribe(IObserver<int> observer) => new Disp();
            private sealed class Disp : IDisposable { public void Dispose() { } }
        }

        public interface ISource
        {
            IObservable<int> Ticks { get; }
        }

        public class Source : ISource
        {
            public IObservable<int> Ticks { get; } = new Ticker();
        }

        """;

    private const string SingletonTickerTransientHandler = """
        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<ITicker, Ticker>();
                services.AddTransient<TickHandler>();
            }
        }

        """;

    // ----------------------------------------------------------------
    // Positives
    // ----------------------------------------------------------------

    [Fact]
    public async Task TransientSubscriber_InjectedFieldSingletonObservable_MethodGroupInCtor_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|_ticker.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StaticExtensionCall_TransientSubscriberSingletonObservable_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|System.RxStub.Subscribe(_ticker, OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StaticExtensionCall_NamedArgumentsReordered_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|System.RxStub.Subscribe(onNext: OnTick, source: _ticker)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BclObserverOverload_SubscriberPassesThis_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler : IObserver<int>
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|_ticker.Subscribe(this)|];
                }

                public void OnNext(int value) { }
                public void OnError(Exception error) { }
                public void OnCompleted() { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_CtorParameterReceiver_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                public TickHandler(ITicker ticker)
                {
                    [|ticker.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientSubscriber_ChainedGetOnlyPropertySegment_SingletonRoot_Reports()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ISource, Source>();
                    services.AddTransient<TickHandler>();
                }
            }

            public class TickHandler
            {
                private readonly ISource _source;

                public TickHandler(ISource source)
                {
                    _source = source;
                    [|_source.Ticks.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DiscardAssignment_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ = [|_ticker.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ThisCapturingLambda_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;
                private int _count;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|_ticker.Subscribe(value => _count += value)|];
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ScopedSubscriber_SingletonObservable_MethodGroupInOrdinaryMethod_Reports()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ITicker, Ticker>();
                    services.AddScoped<TickHandler>();
                }
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker) => _ticker = ticker;

                public void Initialize()
                {
                    [|_ticker.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalTokenNeverReferenced_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    var subscription = [|_ticker.Subscribe(OnTick)|];
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultiCallback_StaticOnNextCapturingOnError_Reports()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;
                private int _errors;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    [|_ticker.Subscribe(value => { }, ex => _errors++)|];
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Negatives
    // ----------------------------------------------------------------

    [Fact]
    public async Task StaticExtensionCall_StaticCallback_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    System.RxStub.Subscribe(_ticker, value => { });
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonExtensionStaticHelperNamedSubscribe_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public static class SubscriptionHelper
            {
                public static IDisposable Subscribe<T>(IObservable<T> source, Action<T> onNext) =>
                    System.RxStub.Subscribe(source, onNext);
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    SubscriptionHelper.Subscribe(_ticker, OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultiCallback_AllStatic_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(value => { }, ex => System.Console.WriteLine(ex));
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TokenStoredInField_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;
                private IDisposable _subscription;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _subscription = _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingDeclaration_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker) => _ticker = ticker;

                public void Initialize()
                {
                    using var subscription = _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalTokenDisposedLater_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker) => _ticker = ticker;

                public void Initialize()
                {
                    var subscription = _ticker.Subscribe(OnTick);
                    subscription.Dispose();
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LocalTokenReturned_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker) => _ticker = ticker;

                public IDisposable Subscribe()
                {
                    var subscription = _ticker.Subscribe(OnTick);
                    return subscription;
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingletonSubscriber_Silent()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ITicker, Ticker>();
                    services.AddSingleton<TickHandler>();
                }
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TransientPublisher_Silent()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<ITicker, Ticker>();
                    services.AddTransient<TickHandler>();
                }
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UnregisteredSubscriber_Silent()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ITicker, Ticker>();
                }
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StaticFreeLambda_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(value => System.Console.WriteLine(value));
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedOnlyPublisher_Silent()
    {
        var source = Prelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddKeyedSingleton<ITicker, Ticker>("primary");
                    services.AddTransient<TickHandler>();
                }
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(OnTick);
                }

                private void OnTick(int value) { }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source, AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task BclObserverOverload_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public class TickHandler
            {
                private readonly ITicker _ticker;
                private readonly IObserver<int> _observer;

                public TickHandler(ITicker ticker, IObserver<int> observer)
                {
                    _ticker = ticker;
                    _observer = observer;
                    _ticker.Subscribe(_observer);
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonObserverSubscribeOverload_PassesThis_Silent()
    {
        var source = Prelude + SingletonTickerTransientHandler + """
            public static class ObserverLikeExtensions
            {
                public static IDisposable Subscribe<T>(this IObservable<T> source, TickHandler handler) =>
                    System.RxStub.Subscribe(source, value => { });
            }

            public class TickHandler
            {
                private readonly ITicker _ticker;

                public TickHandler(ITicker ticker)
                {
                    _ticker = ticker;
                    _ticker.Subscribe(this);
                }
            }
            """;

        await AnalyzerVerifier<DI027_RxSubscriptionLeakAnalyzer>.VerifyDiagnosticsAsync(source);
    }
}
