using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI005;

/// <summary>
/// DI005: Use CreateAsyncScope in async methods.
/// These examples show cases where CreateScope() is used in async methods.
/// </summary>
public class AsyncScopeExamples
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AsyncScopeExamples(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// ⚠️ BAD: CreateScope() is used in an async method instead of CreateAsyncScope().
    /// </summary>
    public async Task Bad_CreateScopeInAsyncMethod()
    {
        // DI005: Use 'CreateAsyncScope' instead of 'CreateScope' in async method
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        await Task.Delay(100);
        service.DoWork();
    }

    /// <summary>
    /// ⚠️ BAD: CreateScope() in async lambda.
    /// </summary>
    public void Bad_CreateScopeInAsyncLambda()
    {
        Func<Task> asyncWork = async () =>
        {
            // DI005: Use 'CreateAsyncScope' instead of 'CreateScope' in async method
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
            await Task.Delay(100);
            service.DoWork();
        };

        _ = asyncWork();
    }

    /// <summary>
    /// ✅ GOOD: CreateAsyncScope() used in async method.
    /// </summary>
    public async Task Good_CreateAsyncScope()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        await Task.Delay(100);
        service.DoWork();
    }

    /// <summary>
    /// ✅ GOOD: CreateScope() in synchronous method is OK.
    /// </summary>
    public void Good_CreateScopeInSyncMethod()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork();
    }

    /// <summary>
    /// ✅ GOOD: Task-returning method without async keyword doesn't need CreateAsyncScope.
    /// </summary>
    public Task Good_TaskReturningWithoutAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork();
        return Task.CompletedTask;
    }
}
