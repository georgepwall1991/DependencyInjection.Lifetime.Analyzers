using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI032
{
    public interface IUploadQueue
    {
        Task EnqueueAsync(string payload);
    }

    /// <summary>
    /// Only IAsyncDisposable, so a synchronous provider or scope disposal throws instead of
    /// disposing it.
    /// </summary>
    public sealed class Bad_AsyncOnlyUploadQueue : IUploadQueue, IAsyncDisposable
    {
        public Task EnqueueAsync(string payload) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// Implements both, so every disposal path has something to call.
    /// </summary>
    public sealed class Good_DualDisposableUploadQueue : IUploadQueue, IDisposable, IAsyncDisposable
    {
        public Task EnqueueAsync(string payload) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => default;
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            // DI032: synchronous disposal of the provider throws on this registration.
            services.AddSingleton<IUploadQueue, Bad_AsyncOnlyUploadQueue>();

            services.AddSingleton<Good_DualDisposableUploadQueue>();
        }
    }
}
