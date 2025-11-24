using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI007;

/// <summary>
/// DI007: Service locator anti-pattern.
/// These examples show cases where IServiceProvider is used to resolve services
/// instead of using constructor injection.
/// </summary>
public static class ServiceLocatorExamples
{
    /// <summary>
    /// Register services to demonstrate DI007.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // Register services
        services.AddSingleton<ISingletonService, SingletonService>();
        services.AddScoped<IScopedService, ScopedService>();

        // Factory registration (allowed)
        services.AddTransient<IGoodFactoryService>(sp =>
            new GoodFactoryService(sp.GetRequiredService<IScopedService>()));

        // Service locator classes (will trigger DI007)
        services.AddScoped<BadServiceLocatorInConstructor>();
        services.AddScoped<BadServiceLocatorInMethod>();

        // Good alternatives
        services.AddScoped<GoodConstructorInjection>();
        services.AddSingleton<GoodFactoryPattern>();
    }
}

/// <summary>
/// ⚠️ BAD: Uses IServiceProvider in constructor to resolve dependencies.
/// </summary>
public class BadServiceLocatorInConstructor
{
    private readonly IScopedService _scopedService;

    // DI007: Consider injecting 'IScopedService' directly instead of resolving via IServiceProvider
    public BadServiceLocatorInConstructor(IServiceProvider provider)
    {
        _scopedService = provider.GetRequiredService<IScopedService>();
    }

    public void DoWork() => _scopedService.DoWork();
}

/// <summary>
/// ⚠️ BAD: Stores IServiceProvider and uses it in methods.
/// </summary>
public class BadServiceLocatorInMethod
{
    private readonly IServiceProvider _provider;

    public BadServiceLocatorInMethod(IServiceProvider provider)
    {
        _provider = provider;
    }

    // DI007: Consider injecting 'IScopedService' directly instead of resolving via IServiceProvider
    public void DoWork()
    {
        var service = _provider.GetRequiredService<IScopedService>();
        service.DoWork();
    }
}

/// <summary>
/// ✅ GOOD: Uses constructor injection directly.
/// </summary>
public class GoodConstructorInjection
{
    private readonly IScopedService _scopedService;

    public GoodConstructorInjection(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public void DoWork() => _scopedService.DoWork();
}

/// <summary>
/// ✅ GOOD: Factory pattern - Create* methods are allowed to use IServiceProvider.
/// </summary>
public class GoodFactoryPattern
{
    private readonly IServiceProvider _provider;

    public GoodFactoryPattern(IServiceProvider provider)
    {
        _provider = provider;
    }

    // This is allowed - factory methods can use service locator
    public IScopedService CreateScopedService()
    {
        return _provider.GetRequiredService<IScopedService>();
    }
}

public interface IGoodFactoryService
{
    void DoWork();
}

public class GoodFactoryService : IGoodFactoryService
{
    private readonly IScopedService _scopedService;

    public GoodFactoryService(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public void DoWork() => _scopedService.DoWork();
}
