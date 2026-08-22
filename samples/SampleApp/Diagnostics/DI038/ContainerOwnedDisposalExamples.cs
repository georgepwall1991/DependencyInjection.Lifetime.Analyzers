using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI038
{
    public interface ITelemetryChannel : IDisposable
    {
        void Send(string payload);
    }

    public sealed class TelemetryChannel : ITelemetryChannel
    {
        public void Send(string payload) { }

        public void Dispose() { }
    }

    public static class Registration
    {
        public static void Register(IServiceCollection services)
        {
            services.AddSingleton<ITelemetryChannel, TelemetryChannel>();
            services.AddScoped<Bad_DisposesInjectedChannel>();
            services.AddScoped<Good_LeavesDisposalToContainer>();
        }
    }

    public sealed class Bad_DisposesInjectedChannel : IDisposable
    {
        private readonly ITelemetryChannel _channel;

        public Bad_DisposesInjectedChannel(ITelemetryChannel channel) => _channel = channel;

        public void Flush() => _channel.Send("flush");

        public void Dispose()
        {
            // DI038: the container owns the singleton channel; disposing it here hands every
            // other consumer of the same instance a disposed object.
            _channel.Dispose();
        }
    }

    public sealed class Good_LeavesDisposalToContainer
    {
        private readonly ITelemetryChannel _channel;

        public Good_LeavesDisposalToContainer(ITelemetryChannel channel) => _channel = channel;

        public void Flush() => _channel.Send("flush");

        // No Dispose call for the injected channel: the provider disposes the singleton when
        // it is itself disposed.
    }
}
