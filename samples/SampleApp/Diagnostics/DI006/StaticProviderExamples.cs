using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI006;

/// <summary>
/// DI006: Static provider cache.
/// These examples show cases where IServiceProvider or IServiceScopeFactory is stored in static members.
/// </summary>
public static class StaticProviderExamples
{
    /// <summary>
    /// ⚠️ BAD: IServiceProvider stored in static field.
    /// </summary>
    public class BadStaticProviderCache
    {
        // DI006: 'IServiceProvider' should not be stored in static member '_provider'
        private static IServiceProvider? _provider;

        // DI006: 'IServiceScopeFactory' should not be stored in static member '_scopeFactory'
        private static IServiceScopeFactory? _scopeFactory;

        // DI006: 'IServiceProvider' should not be stored in static member 'ServiceProvider'
        public static IServiceProvider? ServiceProvider { get; set; }

        public static void Initialize(IServiceProvider provider)
        {
            _provider = provider;
            _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            ServiceProvider = provider;
        }

        public static T? GetService<T>() where T : class
        {
            return _provider?.GetService<T>();
        }
    }

    /// <summary>
    /// ✅ GOOD: Instance field for IServiceProvider.
    /// </summary>
    public class GoodInstanceProvider
    {
        private readonly IServiceProvider _provider;

        public GoodInstanceProvider(IServiceProvider provider)
        {
            _provider = provider;
        }

        public T GetService<T>() where T : notnull
        {
            return _provider.GetRequiredService<T>();
        }
    }

    /// <summary>
    /// ✅ GOOD: Instance field for IServiceScopeFactory.
    /// </summary>
    public class GoodInstanceScopeFactory
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public GoodInstanceScopeFactory(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void DoScopedWork(Action<IServiceProvider> work)
        {
            using var scope = _scopeFactory.CreateScope();
            work(scope.ServiceProvider);
        }
    }
}
