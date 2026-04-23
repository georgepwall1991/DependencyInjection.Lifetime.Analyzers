using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

namespace SampleApp.Diagnostics.DI019;

/// <summary>
/// DI019: Scoped service resolved from root provider.
/// These examples show root-provider resolutions that should create a scope first.
/// </summary>
public static class RootScopedResolutionExamples
{
    public static void BuildRootScopedResolutionExample()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScopedService, ScopedService>();
        services.AddTransient<BadRootResolvedRunner>();

        using var provider = services.BuildServiceProvider();

        // DI019: Resolves a scoped service directly from the root provider.
        provider.GetRequiredService<IScopedService>();

        // DI019: Resolves a transient graph that reaches a scoped service from the root provider.
        provider.GetRequiredService<BadRootResolvedRunner>();
    }

    public static void GoodScopedResolutionExample()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScopedService, ScopedService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
        service.DoWork();
    }
}

public sealed class BadRootResolvedRunner
{
    private readonly IScopedService _scopedService;

    public BadRootResolvedRunner(IScopedService scopedService)
    {
        _scopedService = scopedService;
    }

    public void Run() => _scopedService.DoWork();
}
