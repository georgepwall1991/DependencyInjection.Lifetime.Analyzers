using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI023
{
    public interface IOrderArchiver
    {
        Task ArchiveAsync(int orderId);
    }

    public sealed class OrderArchiver : IOrderArchiver
    {
        public Task ArchiveAsync(int orderId) => Task.CompletedTask;
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddScoped<IOrderArchiver, OrderArchiver>();
            services.AddScoped<Bad_FireAndForgetScopeCapture>();
            services.AddScoped<Good_ScopeInsideBackgroundWork>();
        }
    }

    /// <summary>
    /// The scope is disposed the moment <c>Handle</c> returns, but the thread-pool work it started
    /// is still running and still holding the service it resolved from that scope.
    /// </summary>
    public sealed class Bad_FireAndForgetScopeCapture
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Bad_FireAndForgetScopeCapture(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void Handle(int orderId)
        {
            using var scope = _scopeFactory.CreateScope();
            var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();

            // DI023: the background work outlives the scope it resolved from.
            _ = Task.Run(async () => await archiver.ArchiveAsync(orderId));
        }
    }

    /// <summary>
    /// The background work owns its own scope, so nothing it uses is torn down underneath it.
    /// </summary>
    public sealed class Good_ScopeInsideBackgroundWork
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Good_ScopeInsideBackgroundWork(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void Handle(int orderId)
        {
            _ = Task.Run(async () =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();
                await archiver.ArchiveAsync(orderId);
            });
        }
    }
}
