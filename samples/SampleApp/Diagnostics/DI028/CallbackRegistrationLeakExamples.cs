using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Hosting
{
    // IHostApplicationLifetime ships in Microsoft.Extensions.Hosting.Abstractions. This sample stays
    // package-free by declaring the same contract in its own namespace: DI028 matches the abstraction
    // by symbol identity first and by name plus namespace second, so the real package binds
    // identically. Delete this stub if that package is ever added to SampleApp — two declarations of
    // the same name would then be ambiguous (CS0436).
    public interface IHostApplicationLifetime
    {
        CancellationToken ApplicationStopping { get; }

        void StopApplication();
    }
}

namespace SampleApp.Diagnostics.DI028
{
    public sealed class ApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

        public CancellationToken ApplicationStopping => _stopping.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IHostApplicationLifetime, ApplicationLifetime>();
            services.AddTransient<Bad_RegisterWithoutDispose>();
            services.AddScoped<Good_StoreAndDispose>();
        }
    }

    // Rule DI028: Dispose the registration returned when registering a callback on a longer-lived
    // source. CancellationToken.Register, IOptionsMonitor.OnChange, ChangeToken.OnChange and
    // IChangeToken.RegisterChangeCallback all return a registration that detaches the callback when
    // disposed. A transient or scoped service that discards it stays rooted in the source — here the
    // shutdown token, which lives as long as the process.

    public class Bad_RegisterWithoutDispose
    {
        private readonly IHostApplicationLifetime _lifetime;

        public Bad_RegisterWithoutDispose(IHostApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            // [DI028] The registration is discarded, so every instance the container creates stays
            // rooted in the singleton lifetime's shutdown token for the life of the process.
            _lifetime.ApplicationStopping.Register(OnStopping);
        }

        private void OnStopping() { }
    }

    public class Good_StoreAndDispose : IDisposable
    {
        private readonly CancellationTokenRegistration _registration;

        public Good_StoreAndDispose(IHostApplicationLifetime lifetime)
        {
            // Keep the registration and dispose it when the subscriber is released.
            _registration = lifetime.ApplicationStopping.Register(OnStopping);
        }

        public void Dispose() => _registration.Dispose();

        private void OnStopping() { }
    }
}
