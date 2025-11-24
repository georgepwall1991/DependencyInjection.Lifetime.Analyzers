using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI009;

/// <summary>
/// DI009: Open generic singleton captures scoped or transient dependency.
/// These examples show cases where an open generic singleton service
/// has constructor dependencies with shorter lifetimes.
/// </summary>
public static class OpenGenericExamples
{
    /// <summary>
    /// Register services to demonstrate DI009.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // Register base services
        services.AddSingleton<ISingletonService, SingletonService>();
        services.AddScoped<IScopedService, ScopedService>();
        services.AddTransient<ITransientService, TransientService>();

        // BAD: Open generic singleton capturing scoped dependency
        // DI009: Open generic singleton 'BadRepository' captures scoped dependency 'IScopedService'
        services.AddSingleton(typeof(IBadRepository<>), typeof(BadRepository<>));

        // BAD: Open generic singleton capturing transient dependency
        // DI009: Open generic singleton 'BadRepositoryWithTransient' captures transient dependency 'ITransientService'
        services.AddSingleton(typeof(IBadRepositoryWithTransient<>), typeof(BadRepositoryWithTransient<>));

        // GOOD: Open generic singleton with only singleton dependencies
        services.AddSingleton(typeof(IGoodRepository<>), typeof(GoodRepository<>));

        // GOOD: Open generic scoped with scoped dependency
        services.AddScoped(typeof(IGoodScopedRepository<>), typeof(GoodScopedRepository<>));
    }
}

public interface IBadRepository<T>
{
    T? Get(int id);
    void Save(T entity);
}

public interface IBadRepositoryWithTransient<T>
{
    T? Get(int id);
}

public interface IGoodRepository<T>
{
    T? Get(int id);
}

public interface IGoodScopedRepository<T>
{
    T? Get(int id);
}

/// <summary>
/// ⚠️ BAD: Open generic singleton with scoped dependency.
/// When Repository&lt;Customer&gt; is created as a singleton, the IScopedService
/// will be captured and live for the entire application lifetime.
/// </summary>
public class BadRepository<T> : IBadRepository<T>
{
    private readonly IScopedService _scopedService;

    // DI009 will be reported on the registration line for this constructor
    public BadRepository(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public T? Get(int id)
    {
        _scopedService.DoWork();
        return default;
    }

    public void Save(T entity)
    {
        _scopedService.DoWork();
    }
}

/// <summary>
/// ⚠️ BAD: Open generic singleton with transient dependency.
/// The ITransientService is meant to be recreated on each use,
/// but being captured in a singleton means it lives forever.
/// </summary>
public class BadRepositoryWithTransient<T> : IBadRepositoryWithTransient<T>
{
    private readonly ITransientService _transientService;

    public BadRepositoryWithTransient(ITransientService transientService)
    {
        _transientService = transientService;
    }

    public T? Get(int id)
    {
        _transientService.Process();
        return default;
    }
}

/// <summary>
/// ✅ GOOD: Open generic singleton with singleton dependency only.
/// Singletons can safely depend on other singletons.
/// </summary>
public class GoodRepository<T> : IGoodRepository<T>
{
    private readonly ISingletonService _singletonService;

    public GoodRepository(ISingletonService singletonService)
    {
        _singletonService = singletonService;
    }

    public T? Get(int id)
    {
        _singletonService.Execute();
        return default;
    }
}

/// <summary>
/// ✅ GOOD: Open generic scoped with scoped dependency.
/// Scoped services can depend on scoped or singleton services.
/// </summary>
public class GoodScopedRepository<T> : IGoodScopedRepository<T>
{
    private readonly IScopedService _scopedService;

    public GoodScopedRepository(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public T? Get(int id)
    {
        _scopedService.DoWork();
        return default;
    }
}
