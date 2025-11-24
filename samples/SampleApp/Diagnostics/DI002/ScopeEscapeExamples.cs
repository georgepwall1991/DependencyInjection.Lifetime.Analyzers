using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI002;

/// <summary>
/// DI002: Scoped service escapes its scope.
/// These examples show cases where services outlive their scope.
/// </summary>
public class ScopeEscapeExamples
{
    private readonly IServiceScopeFactory _scopeFactory;
    private IScopedService? _capturedService;

    public ScopeEscapeExamples(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// ⚠️ BAD: Service resolved from scope is returned, escaping the scope lifetime.
    /// </summary>
    public IScopedService Bad_ServiceEscapesViaReturn()
    {
        using var scope = _scopeFactory.CreateScope();
        // DI002: Service resolved from scope escapes via 'return'
        return scope.ServiceProvider.GetRequiredService<IScopedService>();
    }

    /// <summary>
    /// ⚠️ BAD: Service resolved from scope is stored in a field.
    /// </summary>
    public void Bad_ServiceEscapesViaField()
    {
        using var scope = _scopeFactory.CreateScope();
        // DI002: Service resolved from scope escapes via '_capturedService'
        _capturedService = scope.ServiceProvider.GetRequiredService<IScopedService>();
    }

    /// <summary>
    /// ✅ GOOD: Service is used within the scope and not escaped.
    /// </summary>
    public void Good_UsedWithinScope()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork(); // Used within scope - OK
    }

    /// <summary>
    /// ✅ GOOD: Return a result, not the service itself.
    /// </summary>
    public string Good_ReturnResultNotService()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        return service.DoWork(); // Return result of operation, not the service
    }
}
