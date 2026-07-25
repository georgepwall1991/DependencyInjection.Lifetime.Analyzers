using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI030_UnboundedSingletonCacheAnalyzerTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using Microsoft.Extensions.DependencyInjection;

        public class Quote { public decimal Price { get; set; } }

        public enum Region { Us, Eu }

        """;

    private const string CachePrelude = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using Microsoft.Extensions.Caching.Memory;
        using Microsoft.Extensions.DependencyInjection;

        public class Quote { public decimal Price { get; set; } }

        public enum Region { Us, Eu }

        """;

    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyDiagnosticsAsync(source);

    private static Task VerifySilentAsync(string source) =>
        AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyNoDiagnosticsAsync(source);

    private static Task VerifyCacheAsync(string source) =>
        AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    private static Task VerifyCacheSilentAsync(string source) =>
        AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    // ----------------------------------------------------------------
    // Tier A -- unbounded growth, positives
    // ----------------------------------------------------------------

    [Fact]
    public async Task SingletonRegistered_ConcurrentDictionaryGetOrAdd_ParameterKey_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public Quote Get(string userId) => [|_cache.GetOrAdd(userId, id => Load(id))|];

                    private Quote Load(string id) => new Quote();
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task StaticDictionary_IndexerAssignmentParameterKey_Reports()
    {
        var source =
            Prelude
            + """
                public class PriceService
                {
                    private static readonly Dictionary<string, Quote> Cache = new();

                    public void Store(string userId, Quote quote)
                    {
                        [|Cache[userId] = quote|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_ListAddParameterElement_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<AuditLog>();
                }

                public class AuditLog
                {
                    private readonly List<string> _entries = new();

                    public void Record(string message)
                    {
                        [|_entries.Add(message)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_HashSetAddParameterElement_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<SeenTracker>();
                }

                public class SeenTracker
                {
                    private readonly HashSet<Guid> _seen = new();

                    public void Mark(Guid id)
                    {
                        [|_seen.Add(id)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_TryAddInterpolatedKey_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string tenantId, string symbol, Quote quote)
                    {
                        [|_cache.TryAdd($"{tenantId}:{symbol}", quote)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_ConcurrentQueueEnqueueParameter_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<Outbox>();
                }

                public class Outbox
                {
                    private readonly ConcurrentQueue<string> _pending = new();

                    public void Enqueue(string payload)
                    {
                        [|_pending.Enqueue(payload)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_KeyFromParameterProperty_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class Request { public string TenantId { get; set; } = ""; }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(Request request, Quote quote)
                    {
                        [|_cache[request.TenantId] = quote|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_KeyFromGuidNewGuid_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<SessionStore>();
                }

                public class SessionStore
                {
                    private readonly ConcurrentDictionary<Guid, Quote> _sessions = new();

                    public void Start(Quote quote)
                    {
                        [|_sessions.TryAdd(Guid.NewGuid(), quote)|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task DerivedSingletonImplementation_BaseTypeRegistered_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceServiceBase
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    protected void Store(string key, Quote quote)
                    {
                        [|_cache[key] = quote|];
                    }
                }

                public class PriceService : PriceServiceBase
                {
                    public void Put(string key, Quote quote) => Store(key, quote);
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SingletonRegistered_TwoWrites_ReportsOnceAtFirstWrite()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        [|_cache[key] = quote|];
                    }

                    public void Put(string key, Quote quote)
                    {
                        _cache.TryAdd(key, quote);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- bounded key spaces
    // ----------------------------------------------------------------

    [Fact]
    public async Task EnumKey_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<Region, Quote> _cache = new();

                    public void Store(Region region, Quote quote)
                    {
                        _cache[region] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task BoolKey_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<bool, Quote> _cache = new();

                    public void Store(bool live, Quote quote)
                    {
                        _cache[live] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task TypeKey_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<Type, Quote> _cache = new();

                    public void Store(Type type, Quote quote)
                    {
                        _cache[type] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ConstStringKey_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private const string Key = "only";
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(Quote quote)
                    {
                        _cache[Key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task KeyFromFieldNotParameter_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly string _instanceKey = "node";
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(Quote quote)
                    {
                        _cache[_instanceKey] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LocalAliasedKey_Silent()
    {
        // Accepted false negative: v1 requires the key expression itself to be parameter-derived,
        // so a local hop hides the provenance. Local-alias tracking is a follow-up.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string userId, Quote quote)
                    {
                        var key = userId.Trim();
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- eviction, capping, and escape
    // ----------------------------------------------------------------

    [Fact]
    public async Task RemoveElsewhereInType_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }

                    public void Release(string key) => _cache.TryRemove(key, out _);
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ClearInDisposeMethod_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService : IDisposable
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }

                    public void Dispose() => _cache.Clear();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task CountCapCheckBeforeAdd_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        if (_cache.Count > 1000)
                        {
                            return;
                        }

                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task EvictionInsideTimerLambda_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();
                    private readonly Timer _timer;

                    public PriceService()
                    {
                        _timer = new Timer(_ => _cache.Clear(), null, 0, 60000);
                    }

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task FieldPassedAsArgumentToHelper_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public static class Evictor
                {
                    public static void Trim(ConcurrentDictionary<string, Quote> cache) => cache.Clear();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                        Evictor.Trim(_cache);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ForeachDrainLoop_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<Outbox>();
                }

                public class Outbox
                {
                    private readonly ConcurrentQueue<string> _pending = new();

                    public void Enqueue(string payload) => _pending.Enqueue(payload);

                    public void Drain()
                    {
                        foreach (var item in _pending)
                        {
                            Handle(item);
                        }
                    }

                    private void Handle(string item) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task PublicField_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    public readonly ConcurrentDictionary<string, Quote> Cache = new();

                    public void Store(string key, Quote quote)
                    {
                        Cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task InterfaceTypedField_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly IDictionary<string, Quote> _cache = new Dictionary<string, Quote>();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task SortedDictionaryField_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly SortedDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- one-time initialization
    // ----------------------------------------------------------------

    [Fact]
    public async Task WriteOnlyInConstructor_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public PriceService(string seedKey)
                    {
                        _cache[seedKey] = new Quote();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task EnumGetValuesPopulation_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Warm(string prefix)
                    {
                        foreach (var value in Enum.GetValues(typeof(Region)))
                        {
                            _cache[prefix + value] = new Quote();
                        }
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task OneShotInitializedFlagGuard_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private bool _initialized;
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Warm(string key)
                    {
                        if (_initialized)
                        {
                            return;
                        }

                        _initialized = true;
                        _cache[key] = new Quote();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ArgumentGuardBeforeWrite_StillReports()
    {
        // An early return over a parameter is an ordinary argument check, not a one-shot
        // initialization flag. Treating any early return as one-time init would silence a real leak
        // behind the most common validation shape in C#.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        if (key is null)
                        {
                            return;
                        }

                        [|_cache[key] = quote|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- ownership gates
    // ----------------------------------------------------------------

    [Fact]
    public async Task ScopedRegistered_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task TransientRegistered_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task UnregisteredNonStaticType_Silent()
    {
        var source =
            Prelude
            + """
                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredSingletonAndScoped_Silent()
    {
        // Most-conservative-wins: a type also registered scoped is not a process-lifetime owner.
        var source =
            Prelude
            + """
                public interface IPrices { }

                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<PriceService>();
                        services.AddScoped<IPrices, PriceService>();
                    }
                }

                public class PriceService : IPrices
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task KeyedOnlyRegistration_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddKeyedSingleton<PriceService>("primary");
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        // AddKeyedSingleton only exists from DI 8.0, so this needs the keyed reference set.
        await AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.ReferenceAssembliesWithLatestKeyedDi
        );
    }

    // ----------------------------------------------------------------
    // Tier A -- exclusions owned by other rules
    // ----------------------------------------------------------------

    [Fact]
    public async Task StaticDictionaryOfServiceProviders_DI006Only_Silent()
    {
        // DI006's exact shape (a static member whose value type is a provider). Excluded with
        // DI006's own predicate so the two rules cannot both fire.
        var source =
            Prelude
            + """
                public class ProviderCache
                {
                    private static readonly ConcurrentDictionary<string, IServiceProvider> Providers = new();

                    public void Store(string key, IServiceProvider provider)
                    {
                        Providers[key] = provider;
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task ScopeResolvedValueStored_DI002Only_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<PriceService>();
                        services.AddScoped<Quote>();
                    }
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();
                    private readonly IServiceProvider _provider;

                    public PriceService(IServiceProvider provider) => _provider = provider;

                    public void Store(string key)
                    {
                        using var scope = _provider.CreateScope();
                        _cache[key] = scope.ServiceProvider.GetRequiredService<Quote>();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task LockRegistryOfSemaphoreSlim_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<Locks>();
                }

                public class Locks
                {
                    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

                    public SemaphoreSlim For(string key) =>
                        _locks.GetOrAdd(key, _ => new SemaphoreSlim(1));
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task DictionaryOfLazy_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Lazy<Quote>> _cache = new();

                    public void Store(string key)
                    {
                        _cache[key] = new Lazy<Quote>(() => new Quote());
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- editorconfig knob
    // ----------------------------------------------------------------

    [Fact]
    public async Task AllowedCacheTypes_SimpleName_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        _cache[key] = quote;
                    }
                }
                """;

        const string editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI030.allowed_cache_types = Quote
            """;

        await AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyNoDiagnosticsAsync(
            source,
            editorConfig
        );
    }

    [Fact]
    public async Task AllowedCacheTypes_UnrelatedEntry_StillReports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PriceService>();
                }

                public class PriceService
                {
                    private readonly ConcurrentDictionary<string, Quote> _cache = new();

                    public void Store(string key, Quote quote)
                    {
                        [|_cache[key] = quote|];
                    }
                }
                """;

        const string editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI030.allowed_cache_types = SomethingElse
            """;

        await AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyDiagnosticsAsync(
            source,
            editorConfig
        );
    }

    // ----------------------------------------------------------------
    // Tier B -- IMemoryCache
    // ----------------------------------------------------------------

    [Fact]
    public async Task MemoryCacheSet_TwoArgOverload_ParameterKey_Reports()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        [|_cache.Set(userId, quote)|];
                    }
                }
                """;

        await VerifyCacheAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_EmptyEntryOptions_Reports()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        [|_cache.Set(userId, quote, new MemoryCacheEntryOptions())|];
                    }
                }
                """;

        await VerifyCacheAsync(source);
    }

    [Fact]
    public async Task MemoryCacheGetOrCreate_FactoryIgnoresEntry_Reports()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public Quote Get(string userId) =>
                        [|_cache.GetOrCreate(userId, entry => new Quote())|]!;
                }
                """;

        await VerifyCacheAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_SlidingExpirationInitializer_Silent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(
                            userId,
                            quote,
                            new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_SetSizeExtension_Silent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote, new MemoryCacheEntryOptions().SetSize(1));
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_TimeSpanOverload_Silent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote, TimeSpan.FromMinutes(5));
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheGetOrCreate_FactorySetsSlidingExpiration_Silent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public Quote Get(string userId) =>
                        _cache.GetOrCreate(userId, entry =>
                        {
                            entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                            return new Quote();
                        })!;
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_OptionsFromField_Silent()
    {
        // The single biggest Tier B false-positive source: options built anywhere other than inline
        // cannot be inspected, so a non-inline options expression is silenced outright.
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;
                    private readonly MemoryCacheEntryOptions _options = new();

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote, _options);
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_ConstKey_Silent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private const string Key = "all-quotes";
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(Quote quote)
                    {
                        _cache.Set(Key, quote);
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task AddMemoryCacheWithSizeLimit_AnySetInCompilation_Silent()
    {
        // With a global SizeLimit the cache evicts under pressure and the framework requires a
        // per-entry Size, so reporting an unsized entry would be wrong.
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddMemoryCache(options => options.SizeLimit = 1024);
                        services.AddScoped<PriceService>();
                    }
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote);
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheOptionsInitializerSizeLimit_Silent()
    {
        // An initializer assigns SizeLimit through a bare identifier rather than a member access.
        // Missing that shape would leave the tier enabled for a cache that really is bounded.
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 }));
                        services.AddScoped<PriceService>();
                    }
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote);
                    }
                }
                """;

        await VerifyCacheSilentAsync(source);
    }

    [Fact]
    public async Task MemoryCacheSet_PostEvictionCallbackOnly_Reports()
    {
        // RegisterPostEvictionCallback observes an eviction after it happens; it neither schedules an
        // expiration nor caps the cache, so it is not a bound.
        var source = CachePrelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddScoped<PriceService>();
            }

            public class PriceService
            {
                private readonly IMemoryCache _cache;

                public PriceService(IMemoryCache cache) => _cache = cache;

                public void Store(string userId, Quote quote)
                {
                    [|_cache.Set(
                        userId,
                        quote,
                        new MemoryCacheEntryOptions().RegisterPostEvictionCallback((k, v, r, st) => { }))|];
                }
            }
            """;

        await VerifyCacheAsync(source);
    }

    [Fact]
    public async Task MemoryCacheOptionsSizeLimitNull_StillReports()
    {
        // SizeLimit is nullable; assigning null leaves the cache unbounded. Treating any assignment as
        // a bound would let one such line suppress every memory-cache diagnostic in the compilation.
        var source = CachePrelude + """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddMemoryCache(options => options.SizeLimit = null);
                    services.AddScoped<PriceService>();
                }
            }

            public class PriceService
            {
                private readonly IMemoryCache _cache;

                public PriceService(IMemoryCache cache) => _cache = cache;

                public void Store(string userId, Quote quote)
                {
                    [|_cache.Set(userId, quote)|];
                }
            }
            """;

        await VerifyCacheAsync(source);
    }

    [Fact]
    public async Task DetectMemoryCacheBoundsFalse_TierBSilent()
    {
        var source =
            CachePrelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote);
                    }
                }
                """;

        const string editorConfig = """
            root = true

            [*.cs]
            dotnet_code_quality.DI030.detect_memory_cache_bounds = false
            """;

        await AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI030_UnboundedSingletonCacheAnalyzer>.ReferenceAssembliesWithFrameworkExtensions,
            editorConfig
        );
    }

    [Fact]
    public async Task MemoryCachePackageAbsent_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<PriceService>();
                }

                public interface IMemoryCache { object? Set(object key, object value); }

                public class PriceService
                {
                    private readonly IMemoryCache _cache;

                    public PriceService(IMemoryCache cache) => _cache = cache;

                    public void Store(string userId, Quote quote)
                    {
                        _cache.Set(userId, quote);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }
}
