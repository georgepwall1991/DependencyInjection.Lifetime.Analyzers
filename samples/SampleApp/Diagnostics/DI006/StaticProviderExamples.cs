using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI006;

public static class StaticProviderExamples
{
#pragma warning disable DI007
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

        // DI006: 'Lazy<IServiceProvider>' should not be stored in static member '_lazyProvider'
        private static readonly Lazy<IServiceProvider?> _lazyProvider = new(() => _provider);

        public static void Initialize(IServiceProvider provider, IServiceScopeFactory scopeFactory)
        {
            _provider = provider;
            _scopeFactory = scopeFactory;
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
