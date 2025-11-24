using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI001;

/// <summary>
/// DI001: IServiceScope must be disposed.
/// These examples show cases where scopes are not properly disposed.
/// </summary>
public class ScopeNotDisposedExamples
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopeNotDisposedExamples(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// ⚠️ BAD: IServiceScope is created but not disposed.
    /// The scope should be wrapped in a 'using' statement.
    /// </summary>
    public void Bad_ScopeNotDisposed()
    {
        // DI001: IServiceScope created by 'CreateScope' is not disposed
        var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork();
        // Missing: scope.Dispose() or using statement
    }

    /// <summary>
    /// ✅ GOOD: Using declaration ensures disposal.
    /// </summary>
    public void Good_UsingDeclaration()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork();
    }

    /// <summary>
    /// ✅ GOOD: Using statement ensures disposal.
    /// </summary>
    public void Good_UsingStatement()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
            service.DoWork();
        }
    }

    /// <summary>
    /// ✅ GOOD: Explicit Dispose() call.
    /// </summary>
    public void Good_ExplicitDispose()
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
            service.DoWork();
        }
        finally
        {
            scope.Dispose();
        }
    }
}
