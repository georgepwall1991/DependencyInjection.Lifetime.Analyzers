using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI004;

/// <summary>
/// DI004: Service used after scope disposed.
/// These examples show cases where a service is accessed after its scope has been disposed.
/// </summary>
public class UseAfterDisposeExamples
{
    private readonly IServiceScopeFactory _scopeFactory;

    public UseAfterDisposeExamples(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// ⚠️ BAD: Service is used after its scope has been disposed.
    /// </summary>
    public void Bad_UseAfterDispose()
    {
        IScopedService service;
        using (var scope = _scopeFactory.CreateScope())
        {
            service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        }
        // DI004: Service 'service' may be used after its scope is disposed
        service.DoWork();
    }

    /// <summary>
    /// ⚠️ BAD: Multiple services used after scope disposed.
    /// </summary>
    public void Bad_MultipleServicesAfterDispose()
    {
        IScopedService scoped;
        ITransientService transient;
        using (var scope = _scopeFactory.CreateScope())
        {
            scoped = scope.ServiceProvider.GetRequiredService<IScopedService>();
            transient = scope.ServiceProvider.GetRequiredService<ITransientService>();
        }
        // Both services are now invalid
        scoped.DoWork();
        transient.Process();
    }

    /// <summary>
    /// ✅ GOOD: All service usage within the using block.
    /// </summary>
    public void Good_UsedWithinScope()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
            service.DoWork(); // Within scope - OK
        }
    }

    /// <summary>
    /// ✅ GOOD: Using declaration keeps service valid until end of method.
    /// </summary>
    public void Good_UsingDeclaration()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork(); // Scope still active - OK
        // More work here is still OK
        service.DoWork();
    }

    /// <summary>
    /// ✅ GOOD: Extract data from service within scope, use data after.
    /// </summary>
    public string Good_ExtractDataWithinScope()
    {
        string result;
        using (var scope = _scopeFactory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
            result = service.DoWork();
        }
        // Using the result (a string) is fine - it's just data
        return result;
    }
}
