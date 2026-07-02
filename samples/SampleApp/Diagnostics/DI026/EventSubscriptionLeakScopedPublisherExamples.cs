using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI026
{
    public interface IScopedMessageBus
    {
        event EventHandler MessageReceived;
    }

    public class ScopedMessageBus : IScopedMessageBus
    {
        public event EventHandler MessageReceived;

        public void Publish() => MessageReceived?.Invoke(this, EventArgs.Empty);
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddScoped<IScopedMessageBus, ScopedMessageBus>();
            services.AddTransient<Bad_TransientSubscribesToScopedPublisher>();
            services.AddScoped<Good_UnsubscribeInDispose>();
        }
    }

    // Rule DI026: the scope-bounded Info tier of DI025. A transient that subscribes to a
    // scoped publisher's event stays rooted in the publisher's delegate list until the
    // scope is disposed — every transient the scope resolves accumulates, and the event
    // keeps invoking handlers on instances the container already released.

    public class Bad_TransientSubscribesToScopedPublisher
    {
        private readonly IScopedMessageBus _bus;

        public Bad_TransientSubscribesToScopedPublisher(IScopedMessageBus bus)
        {
            _bus = bus;
            // [DI026] Rooted by the scoped bus for as long as its scope lives
            _bus.MessageReceived += OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
    }

    public class Good_UnsubscribeInDispose : IDisposable
    {
        private readonly IScopedMessageBus _bus;

        public Good_UnsubscribeInDispose(IScopedMessageBus bus)
        {
            _bus = bus;
            _bus.MessageReceived += OnMessage;
        }

        public void Dispose()
        {
            // The matching -= releases this instance from the scoped publisher's delegate list.
            _bus.MessageReceived -= OnMessage;
        }

        private void OnMessage(object sender, EventArgs e) { }
    }
}
