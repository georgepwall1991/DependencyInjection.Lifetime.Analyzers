using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI025
{
    public interface IMessageBus
    {
        event EventHandler MessageReceived;
    }

    public class MessageBus : IMessageBus
    {
        public event EventHandler MessageReceived;

        public void Publish() => MessageReceived?.Invoke(this, EventArgs.Empty);
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IMessageBus, MessageBus>();
            services.AddTransient<Bad_SubscribeWithoutUnsubscribe>();
            services.AddTransient<Bad_AnonymousHandlerSubscription>();
            services.AddScoped<Good_UnsubscribeInDispose>();
        }
    }

    // Rule DI025: Unsubscribe from longer-lived publishers before the subscriber is released
    // A transient/scoped service that subscribes to a singleton's event without unsubscribing
    // is rooted by the publisher's delegate list — every resolved instance leaks.

    public class Bad_SubscribeWithoutUnsubscribe
    {
        private readonly IMessageBus _bus;

        public Bad_SubscribeWithoutUnsubscribe(IMessageBus bus)
        {
            _bus = bus;
            // [DI025] 'Bad_SubscribeWithoutUnsubscribe' is registered as transient but never unsubscribes
            _bus.MessageReceived += OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
    }

    public class Bad_AnonymousHandlerSubscription
    {
        private int _count;

        public Bad_AnonymousHandlerSubscription(IMessageBus bus)
        {
            // [DI025] An anonymous handler that is never stored can never be unsubscribed
            bus.MessageReceived += (sender, e) => _count++;
        }
    }

    public class Good_UnsubscribeInDispose : IDisposable
    {
        private readonly IMessageBus _bus;

        public Good_UnsubscribeInDispose(IMessageBus bus)
        {
            _bus = bus;
            _bus.MessageReceived += OnMessage;
        }

        public void Dispose()
        {
            // The matching -= releases this instance from the singleton's delegate list.
            _bus.MessageReceived -= OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
    }
}
