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

    public class BusHost
    {
        public MessageBus Bus { get; } = new MessageBus();
    }

    // C# forbids assigning another type's field-like event, so the cross-type Combine leak
    // lives on a public delegate-typed member instead of an event.
    public class DelegateEventBus
    {
        public EventHandler Handlers;
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IMessageBus, MessageBus>();
            services.AddSingleton<BusHost>();
            services.AddSingleton<DelegateEventBus>();
            services.AddTransient<Bad_SubscribeWithoutUnsubscribe>();
            services.AddTransient<Bad_AnonymousHandlerSubscription>();
            services.AddTransient<Bad_ChainedReceiverSubscription>();
            services.AddTransient<Bad_DelegateCombineSubscription>();
            services.AddScoped<Good_UnsubscribeInDispose>();
            services.AddScoped<Good_ChainedUnsubscribeInDispose>();
            services.AddScoped<Good_DelegateRemoveInDispose>();
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

    public class Bad_ChainedReceiverSubscription
    {
        private readonly BusHost _host;

        public Bad_ChainedReceiverSubscription(BusHost host)
        {
            _host = host;
            // [DI025] The publisher is reached through a stable projection of the singleton
            // BusHost, so the chained subscription roots this transient just the same.
            _host.Bus.MessageReceived += OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
    }

    public class Bad_DelegateCombineSubscription
    {
        private readonly DelegateEventBus _bus;

        public Bad_DelegateCombineSubscription(DelegateEventBus bus)
        {
            _bus = bus;
            // [DI025] A Delegate.Combine self-assignment onto the singleton's public delegate
            // field roots this transient exactly like a += on an event, and it never removes.
            _bus.Handlers = (EventHandler)Delegate.Combine(_bus.Handlers, OnMessage);
        }

        private void OnMessage(object sender, EventArgs e) { }
    }

    public class Good_DelegateRemoveInDispose : IDisposable
    {
        private readonly DelegateEventBus _bus;

        public Good_DelegateRemoveInDispose(DelegateEventBus bus)
        {
            _bus = bus;
            _bus.Handlers = (EventHandler)Delegate.Combine(_bus.Handlers, OnMessage);
        }

        public void Dispose()
        {
            // The mirrored Delegate.Remove self-assignment releases this instance.
            _bus.Handlers = (EventHandler)Delegate.Remove(_bus.Handlers, OnMessage);
        }

        private void OnMessage(object sender, EventArgs e) { }
    }

    public class Good_ChainedUnsubscribeInDispose : IDisposable
    {
        private readonly BusHost _host;

        public Good_ChainedUnsubscribeInDispose(BusHost host)
        {
            _host = host;
            _host.Bus.MessageReceived += OnMessage;
        }

        public void Dispose()
        {
            // The matching -= through the same chain releases this instance.
            _host.Bus.MessageReceived -= OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
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
