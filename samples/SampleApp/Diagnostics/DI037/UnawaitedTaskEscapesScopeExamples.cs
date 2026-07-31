using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI037
{
    public interface IOrderArchiver
    {
        Task ArchiveAsync(int orderId);
    }

    public sealed class OrderArchiver : IOrderArchiver
    {
        public Task ArchiveAsync(int orderId) => Task.CompletedTask;
    }

    public sealed class Dispatcher
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Dispatcher(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

        public Task ArchiveBad(int orderId)
        {
            using var scope = _scopeFactory.CreateScope();
            var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();

            // DI037: the scope is disposed as this returns, tearing down the archiver while the
            // task it handed back is still running.
            return archiver.ArchiveAsync(orderId);
        }

        public async Task ArchiveGood(int orderId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();

            // Awaiting inside the scope keeps the archiver alive for the whole of the work.
            await archiver.ArchiveAsync(orderId);
        }
    }
}
