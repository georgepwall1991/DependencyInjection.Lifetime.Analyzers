using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public abstract class BackgroundService : IHostedService
    {
        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

namespace SampleApp.Diagnostics.DI024
{
    public interface IOrderProcessor
    {
        Task ProcessPendingAsync(CancellationToken token);
    }

    public class OrderProcessor : IOrderProcessor
    {
        public Task ProcessPendingAsync(CancellationToken token) => Task.CompletedTask;
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddScoped<IOrderProcessor, OrderProcessor>();
        }
    }

    // Rule DI024: Create a scope per iteration in hosted service execution loops
    // A scope created once before the long-running loop keeps the same scoped instances
    // (DbContexts, units of work) alive for the entire process lifetime.

    public class Bad_HoistedScopePollingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Bad_HoistedScopePollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // [DI024] Scope is created outside the execution loop of 'ExecuteAsync'
            await using var scope = _scopeFactory.CreateAsyncScope();
            while (!stoppingToken.IsCancellationRequested)
            {
                var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
                await processor.ProcessPendingAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public class Bad_HoistedScopedServicePollingService : BackgroundService
    {
        private readonly IServiceProvider _provider;

        public Bad_HoistedScopedServicePollingService(IServiceProvider provider) => _provider = provider;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // [DI024] 'IOrderProcessor' is registered as scoped but resolved once outside the execution loop of 'ExecuteAsync'
            var processor = _provider.GetRequiredService<IOrderProcessor>();
            while (!stoppingToken.IsCancellationRequested)
            {
                await processor.ProcessPendingAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public class Good_ScopePerIterationPollingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Good_ScopePerIterationPollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Each iteration gets fresh scoped services.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
                await processor.ProcessPendingAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
