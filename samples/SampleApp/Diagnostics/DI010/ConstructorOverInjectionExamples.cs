using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI010;

public interface IService1 { }
public interface IService2 { }
public interface IService3 { }
public interface IService4 { }
public interface IService5 { }
public interface IService6 { }

public class Service1 : IService1 { }
public class Service2 : IService2 { }
public class Service3 : IService3 { }
public class Service4 : IService4 { }
public class Service5 : IService5 { }
public class Service6 : IService6 { }

/// <summary>
/// ⚠️ BAD: Too many dependencies injected into constructor.
/// This violates the Single Responsibility Principle.
/// </summary>
public class ConstructorOverInjectionExample
{
    public ConstructorOverInjectionExample(
        IService1 s1,
        IService2 s2,
        IService3 s3,
        IService4 s4,
        IService5 s5,
        IService6 s6)
    {
    }
}

/// <summary>
/// ✅ GOOD: Reasonable number of dependencies.
/// </summary>
public class GoodConstructorExample
{
    public GoodConstructorExample(IService1 s1, IService2 s2)
    {
    }
}

public static class Registration
{
    public static void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddTransient<IService1, Service1>();
        services.AddTransient<IService2, Service2>();
        services.AddTransient<IService3, Service3>();
        services.AddTransient<IService4, Service4>();
        services.AddTransient<IService5, Service5>();
        services.AddTransient<IService6, Service6>();

        // Trigger DI010
        services.AddTransient<ConstructorOverInjectionExample>();
        
        // Good
        services.AddTransient<GoodConstructorExample>();
    }
}
