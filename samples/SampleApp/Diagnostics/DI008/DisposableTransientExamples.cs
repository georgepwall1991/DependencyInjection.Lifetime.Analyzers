using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI008;

/// <summary>
/// DI008: Transient service implements IDisposable.
/// These examples show cases where a transient service implements IDisposable or IAsyncDisposable,
/// which means the DI container will NOT track or dispose the service.
/// </summary>
public static class DisposableTransientExamples
{
    /// <summary>
    /// Register services to demonstrate DI008.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // BAD: Transient disposable - container won't dispose
        // DI008: Transient service 'BadDisposableTransient' implements IDisposable but the container will not track or dispose it
        services.AddTransient<IDisposableService, BadDisposableTransient>();

        // BAD: Transient async disposable
        // DI008: Transient service 'BadAsyncDisposableTransient' implements IAsyncDisposable but the container will not track or dispose it
        services.AddTransient<IAsyncDisposableService, BadAsyncDisposableTransient>();

        // GOOD: Scoped - container will track and dispose
        services.AddScoped<IScopedDisposable, GoodScopedDisposable>();

        // GOOD: Singleton - container will track and dispose
        services.AddSingleton<ISingletonDisposable, GoodSingletonDisposable>();

        // GOOD: Factory registration - caller is responsible for disposal
        services.AddTransient<IDisposableService>(sp =>
            new GoodFactoryDisposable());
    }
}

public interface IDisposableService
{
    void DoWork();
}

public interface IAsyncDisposableService
{
    void DoWork();
}

public interface IScopedDisposable : IDisposable
{
    void DoWork();
}

public interface ISingletonDisposable : IDisposable
{
    void DoWork();
}

/// <summary>
/// ⚠️ BAD: Transient service that implements IDisposable.
/// The DI container will NOT track this service, so Dispose() will never be called.
/// This can lead to resource leaks.
/// </summary>
public class BadDisposableTransient : IDisposableService, IDisposable
{
    private bool _disposed;

    public void DoWork()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BadDisposableTransient));
        // Do work...
    }

    public void Dispose()
    {
        _disposed = true;
        // Resources will NOT be cleaned up because container doesn't track transients
    }
}

/// <summary>
/// ⚠️ BAD: Transient service that implements IAsyncDisposable.
/// Same issue as IDisposable - the container won't track or dispose it.
/// </summary>
public class BadAsyncDisposableTransient : IAsyncDisposableService, IAsyncDisposable
{
    private bool _disposed;

    public void DoWork()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BadAsyncDisposableTransient));
        // Do work...
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        // Resources will NOT be cleaned up
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// ✅ GOOD: Scoped disposable - container tracks and disposes when scope ends.
/// </summary>
public class GoodScopedDisposable : IScopedDisposable
{
    private bool _disposed;

    public void DoWork()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GoodScopedDisposable));
        // Do work...
    }

    public void Dispose()
    {
        _disposed = true;
        // Container WILL call this when the scope is disposed
    }
}

/// <summary>
/// ✅ GOOD: Singleton disposable - container tracks and disposes at shutdown.
/// </summary>
public class GoodSingletonDisposable : ISingletonDisposable
{
    private bool _disposed;

    public void DoWork()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GoodSingletonDisposable));
        // Do work...
    }

    public void Dispose()
    {
        _disposed = true;
        // Container WILL call this when the application shuts down
    }
}

/// <summary>
/// ✅ GOOD: Factory registration - caller controls disposal.
/// When using a factory, the caller knows they're responsible for disposal.
/// </summary>
public class GoodFactoryDisposable : IDisposableService, IDisposable
{
    private bool _disposed;

    public void DoWork()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GoodFactoryDisposable));
        // Do work...
    }

    public void Dispose()
    {
        _disposed = true;
        // Caller is responsible for calling Dispose
    }
}
