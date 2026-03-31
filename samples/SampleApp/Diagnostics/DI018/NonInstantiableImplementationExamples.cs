using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI018;

public interface IMyService { }
public interface IPrivateCtorService { }

/// <summary>
/// BAD: Abstract class cannot be instantiated by the DI container.
/// </summary>
public abstract class BadAbstractService : IMyService { }

/// <summary>
/// BAD: No public constructor means the container cannot create the type.
/// </summary>
public class BadPrivateCtorService : IPrivateCtorService
{
    private BadPrivateCtorService() { }
}

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

        // BAD: BadPrivateCtorService has no public constructor
        services.AddSingleton<IPrivateCtorService, BadPrivateCtorService>();

        // GOOD: ConcreteService has a public constructor
        services.AddSingleton<IMyService, GoodConcreteService>();
    }
}
