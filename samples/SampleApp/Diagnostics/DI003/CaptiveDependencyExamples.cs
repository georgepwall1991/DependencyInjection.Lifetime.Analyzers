using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI003;

/// <summary>
/// DI003: Captive dependency - singleton captures scoped/transient.
/// These examples show cases where a longer-lived service captures a shorter-lived dependency.
/// </summary>
public static class CaptiveDependencyExamples
{
    /// <summary>
    /// Register services to demonstrate DI003.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // Register the services
        services.AddSingleton<ISingletonService, SingletonService>();
        services.AddScoped<IScopedService, ScopedService>();
        services.AddTransient<ITransientService, TransientService>();

        // BAD: Singleton captures scoped
        services.AddSingleton<BadSingletonWithScopedDependency>();

        // BAD: Singleton captures transient
        services.AddSingleton<BadSingletonWithTransientDependency>();

        // GOOD: Singleton with singleton dependency
        services.AddSingleton<GoodSingletonWithSingletonDependency>();

        // GOOD: Singleton with scope factory
        services.AddSingleton<GoodSingletonWithScopeFactory>();
    }
}

/// <summary>
/// ⚠️ BAD: This singleton service captures a scoped dependency.
/// The scoped service will live for the entire application lifetime.
/// </summary>
public class BadSingletonWithScopedDependency
{
    private readonly IScopedService _scopedService;

    // DI003: Singleton 'BadSingletonWithScopedDependency' captures scoped dependency 'IScopedService'
    public BadSingletonWithScopedDependency(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public void DoWork() => _scopedService.DoWork();
}

/// <summary>
/// ⚠️ BAD: This singleton service captures a transient dependency.
/// </summary>
public class BadSingletonWithTransientDependency
{
    private readonly ITransientService _transientService;

    // DI003: Singleton 'BadSingletonWithTransientDependency' captures transient dependency 'ITransientService'
    public BadSingletonWithTransientDependency(ITransientService transientService)
    {
        _transientService = transientService;
    }

    public void Process() => _transientService.Process();
}

/// <summary>
/// ✅ GOOD: Singleton depends only on other singletons.
/// </summary>
public class GoodSingletonWithSingletonDependency
{
    private readonly ISingletonService _singletonService;

    public GoodSingletonWithSingletonDependency(ISingletonService singletonService)
    {
        _singletonService = singletonService;
    }

    public void Execute() => _singletonService.Execute();
}

/// <summary>
/// ✅ GOOD: Use IServiceScopeFactory to create scopes when needed.
/// </summary>
public class GoodSingletonWithScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GoodSingletonWithScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void DoWork()
    {
        using var scope = _scopeFactory.CreateScope();
        var scopedService = scope.ServiceProvider.GetRequiredService<IScopedService>();
        scopedService.DoWork();
    }
}
