using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI016;

public static class BuildServiceProviderMisuseExamples
{
    /// <summary>
    /// ⚠️ BAD: Building a provider during registration creates a second container.
    /// </summary>
    public static void RegisterBad(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        _ = provider.GetService(typeof(object));
    }

    /// <summary>
    /// ⚠️ BAD: Extension-based registration helper with BuildServiceProvider misuse.
    /// </summary>
    public static IServiceCollection AddFeatureBad(this IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return services;
    }

    /// <summary>
    /// ✅ GOOD: Explicit provider factory intentionally returns IServiceProvider.
    /// </summary>
    public static IServiceProvider CreateProvider(IServiceCollection services)
    {
        return services.BuildServiceProvider();
    }
}
