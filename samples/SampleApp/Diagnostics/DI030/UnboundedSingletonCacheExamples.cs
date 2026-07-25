using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI030
{
    public class Report
    {
        public string Body { get; set; } = string.Empty;
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddSingleton<Bad_UnboundedTenantCache>();
            services.AddScoped<Bad_UnboundedCacheEntry>();
            services.AddSingleton<Good_BoundedByEvictionOnRelease>();
        }
    }

    // Rule DI030: Bound the growth of a cache held for the lifetime of the process. A collection owned
    // by a singleton (or held in a static member) that is written with request-derived keys and never
    // evicted grows monotonically until the process dies of OutOfMemoryException. An IMemoryCache
    // entry with neither an expiration nor a size is the same defect with extra steps.
    // Reported at Info: the key space may be bounded in practice even when the type system cannot
    // prove it.

    public class Bad_UnboundedTenantCache
    {
        private readonly ConcurrentDictionary<string, Report> _cache =
            new ConcurrentDictionary<string, Report>();

        public Report Get(string tenantId)
        {
            // [DI030] Unbounded key space, and nothing in this type ever removes from the store or
            // caps its size, so it grows for the life of the process.
            return _cache.GetOrAdd(tenantId, id => Load(id));
        }

        private Report Load(string id) => new Report { Body = id };
    }

    public class Bad_UnboundedCacheEntry
    {
        private readonly IMemoryCache _cache;

        public Bad_UnboundedCacheEntry(IMemoryCache cache) => _cache = cache;

        public void Store(string userId, Report report)
        {
            // [DI030] No expiration and no size, in a cache with no configured SizeLimit: the entry
            // is never evicted except by an explicit Remove.
            _cache.Set(userId, report);
        }
    }

    public class Good_BoundedByEvictionOnRelease
    {
        private readonly ConcurrentDictionary<string, Report> _cache =
            new ConcurrentDictionary<string, Report>();

        public Report Get(string tenantId) =>
            _cache.GetOrAdd(tenantId, id => new Report { Body = id });

        // An explicit eviction path tied to the lifetime of what the entry describes.
        public void Release(string tenantId) => _cache.TryRemove(tenantId, out _);
    }
}
