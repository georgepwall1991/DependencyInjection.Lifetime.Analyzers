using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI015;

public interface IRegisteredDependency { }
public class RegisteredDependency : IRegisteredDependency { }

public interface IMissingDependency { }

/// <summary>
/// ⚠️ BAD: Missing dependency is never registered.
/// Resolving this service will fail at runtime.
/// </summary>
public class BadUnresolvableService
{
    public BadUnresolvableService(IMissingDependency missing) { }
}

/// <summary>
/// ⚠️ BAD: Factory requires an unregistered dependency.
/// </summary>
public class BadFactoryUnresolvableService
{
    public BadFactoryUnresolvableService(IMissingDependency missing) { }
}

/// <summary>
/// ✅ GOOD: Dependency is registered.
/// </summary>
public class GoodResolvableService
{
    public GoodResolvableService(IRegisteredDependency dependency) { }
}

public static class UnresolvableDependencyExamples
{
    public static void Register(IServiceCollection services)
    {
        // ✅ GOOD
        services.AddSingleton<IRegisteredDependency, RegisteredDependency>();
        services.AddSingleton<GoodResolvableService>();

        // ⚠️ BAD: IMissingDependency is not registered
        services.AddSingleton<BadUnresolvableService>();

        // ⚠️ BAD: Factory resolves IMissingDependency via GetRequiredService
        services.AddSingleton<BadFactoryUnresolvableService>(
            sp => new BadFactoryUnresolvableService(sp.GetRequiredService<IMissingDependency>()));
    }
}
