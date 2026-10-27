using Microsoft.Extensions.DependencyInjection;

namespace DependencyInjection.Showcase;

public interface IScopedService
{
    void Work();
}

public sealed class ScopedService : IScopedService
{
    public void Work() { }
}

public interface ISingletonService
{
    void Run();
}

/// <summary>
/// DIA001 / DI003: a singleton captures a scoped service. The scoped instance is created once and
/// then shared for the process lifetime, so it can be disposed while the singleton still uses it.
/// </summary>
public sealed class CaptiveSingleton : ISingletonService
{
    private readonly IScopedService _scoped;

    public CaptiveSingleton(IScopedService scoped) => _scoped = scoped;

    public void Run() => _scoped.Work();
}

public interface IOpenGenericService<T>
{
    void Run();
}

/// <summary>
/// DIA005 / DI009: the open generic is registered as a singleton, but every closed implementation
/// needs a scoped dependency. The first resolution captures a scoped instance for the process.
/// </summary>
public sealed class OpenGenericService<T> : IOpenGenericService<T>
{
    private readonly IScopedService _scoped;

    public OpenGenericService(IScopedService scoped) => _scoped = scoped;

    public void Run() => _scoped.Work();
}

public sealed class ConflictService
{
}

/// <summary>
/// DIA006 / DI006: a static field keeps a provider alive after the composition root has finished,
/// hiding scope ownership and making tests share process state.
/// </summary>
public static class ProviderCache
{
    public static IServiceProvider? Provider;
}

public static class Showcase
{
    /// <summary>
    /// Every call below is a distinct production failure. The analyzer reports the rule named in
    /// the comment; the README maps each one to the safe shape.
    /// </summary>
    public static void BadComposition()
    {
        var services = new ServiceCollection();

        services.AddScoped<IScopedService, ScopedService>();

        // DIA001 / DI003: singleton captures scoped.
        services.AddSingleton<ISingletonService, CaptiveSingleton>();

        // DIA004 / DI012b: the same concrete service is registered twice with different lifetimes.
        // The last registration wins; the singleton registration is unreachable and the intent is
        // unclear.
        services.AddSingleton<ConflictService>();
        services.AddScoped<ConflictService>();

        // DIA005 / DI009: open generic singleton captures scoped.
        services.AddSingleton(typeof(IOpenGenericService<>), typeof(OpenGenericService<>));

        // DIA002 / DI016: BuildServiceProvider during registration snapshots a half-built container.
        RegisterWithTemporaryProvider(services);

        using var provider = services.BuildServiceProvider();

        // DIA006 / DI006: a static provider cache outlives every scope it can create.
        ProviderCache.Provider = provider;

        // DIA003 / DI019: a scoped service is resolved from the root provider. It lives until the
        // root provider is disposed, not for one unit of work.
        var scopedFromRoot = provider.GetRequiredService<IScopedService>();
        scopedFromRoot.Work();
    }

    /// <summary>
    /// DIA002 / DI016: a second container is built while the first one is still being composed.
    /// </summary>
    public static void RegisterWithTemporaryProvider(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        _ = provider.GetService(typeof(object));
    }
}
