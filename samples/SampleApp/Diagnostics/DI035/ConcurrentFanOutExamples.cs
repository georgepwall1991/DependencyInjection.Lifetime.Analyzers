using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore
{
    // EF Core ships DbContext; SampleApp stays package-free by declaring the same contract, which
    // the analyzer resolves by namespace and name exactly as it resolves the real one.
    public class DbContext { }
}

namespace SampleApp.Diagnostics.DI035
{
    public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Task LoadAsync(int id) => Task.CompletedTask;
    }

    public sealed class Bad_SharedContextFanOut
    {
        private readonly AppDbContext _db;

        public Bad_SharedContextFanOut(AppDbContext db)
        {
            _db = db;
        }

        public async Task ProcessAsync(IEnumerable<int> orderIds)
        {
            // DI035: every task runs against the same DbContext at the same time.
            await Task.WhenAll(orderIds.Select(id => _db.LoadAsync(id)));
        }
    }

    public sealed class Good_ScopePerTaskFanOut
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public Good_ScopePerTaskFanOut(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task ProcessAsync(IEnumerable<int> orderIds)
        {
            await Task.WhenAll(
                orderIds.Select(async id =>
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await db.LoadAsync(id);
                }));
        }
    }
}
