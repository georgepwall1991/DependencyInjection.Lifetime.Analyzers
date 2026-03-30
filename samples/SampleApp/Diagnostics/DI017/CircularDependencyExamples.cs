using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI017;

public interface IOrderService { }
public interface IPaymentService { }

/// <summary>
/// BAD: OrderService depends on IPaymentService, and PaymentService depends on IOrderService.
/// This creates a circular dependency that will cause a StackOverflowException at runtime.
/// </summary>
public class BadOrderService : IOrderService
{
    public BadOrderService(IPaymentService payment) { }
}

public class BadPaymentService : IPaymentService
{
    public BadPaymentService(IOrderService order) { }
}

/// <summary>
/// GOOD: Break the cycle by removing the circular dependency.
/// </summary>
public class GoodOrderService : IOrderService { }

public class GoodPaymentService : IPaymentService
{
    public GoodPaymentService(IOrderService order) { }
}

public static class CircularDependencyExamples
{
    public static void Register(IServiceCollection services)
    {
        // BAD: Circular dependency between IOrderService and IPaymentService
        services.AddScoped<IOrderService, BadOrderService>();
        services.AddScoped<IPaymentService, BadPaymentService>();
    }
}
