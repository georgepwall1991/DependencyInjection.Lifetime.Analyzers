using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI018;

public interface IMyService { }

/// <summary>
/// BAD: Abstract class cannot be instantiated by the DI container.
/// </summary>
public abstract class BadAbstractService : IMyService { }

/// <summary>
/// GOOD: Concrete class can be instantiated.
/// </summary>
public class GoodConcreteService : IMyService { }

public static class NonInstantiableImplementationExamples
{
    public static void Register(IServiceCollection services)
    {
        // BAD: AbstractService is abstract and cannot be constructed
        services.AddSingleton<IMyService, BadAbstractService>();

        // GOOD: ConcreteService has a public constructor
        services.AddSingleton<IMyService, GoodConcreteService>();
    }
}
