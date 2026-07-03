using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI027
{
    // System.IObservable<T>/IObserver<T> live in the BCL, but the Action-based Subscribe overload
    // ships in System.Reactive. This sample stays package-free by declaring the same extension in
    // its own namespace; DI027 matches any method named Subscribe returning IDisposable on an
    // IObservable<T> receiver, so the real System.Reactive binds identically.
    internal static class RxStub
    {
        public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext) => new Token();

        private sealed class Token : IDisposable
        {
            public void Dispose() { }
        }
    }

    public interface ITicker : IObservable<int>
    {
    }

    public class Ticker : ITicker
    {
        public IDisposable Subscribe(IObserver<int> observer) => new Subscription();

        private sealed class Subscription : IDisposable
        {
            public void Dispose() { }
        }
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<ITicker, Ticker>();
            services.AddTransient<Bad_SubscribeWithoutDispose>();
            services.AddScoped<Good_StoreAndDispose>();
        }
    }

    // Rule DI027: Dispose the subscription returned by Subscribe on a longer-lived observable.
    // Subscribe returns an IDisposable that unsubscribes the observer. A transient/scoped service
    // that discards that token when subscribing to a singleton observable stays rooted in the
    // publisher for the whole process — the Rx twin of DI025's missing -=.

    public class Bad_SubscribeWithoutDispose
    {
        private readonly ITicker _ticker;

        public Bad_SubscribeWithoutDispose(ITicker ticker)
        {
            _ticker = ticker;
            // [DI027] The IDisposable token is discarded, so every instance the container creates
            // stays rooted in the singleton ticker's observer list.
            _ticker.Subscribe(OnTick);
        }

        private void OnTick(int value) { }
    }

    public class Good_StoreAndDispose : IDisposable
    {
        private readonly IDisposable _subscription;

        public Good_StoreAndDispose(ITicker ticker)
        {
            // Keep the token and dispose it when the subscriber is released.
            _subscription = ticker.Subscribe(OnTick);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnTick(int value) { }
    }
}
