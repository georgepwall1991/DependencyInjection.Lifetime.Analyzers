using Microsoft.Extensions.DependencyInjection;

Console.WriteLine(@"DI015 In Action: Unresolvable Dependency");
Console.WriteLine(@"========================================");
Console.WriteLine();

Console.WriteLine(@"Broken configuration (expected runtime failures):");
using (ServiceProvider provider = Di015DemoRegistrations.BuildBrokenProvider())
{
    TryResolve<BrokenInvoiceScheduler>(provider);
    TryResolve<BrokenKeyedNotificationRunner>(provider);
}

Console.WriteLine();
Console.WriteLine(@"Fixed configuration (expected successful resolution):");
using (ServiceProvider provider = Di015DemoRegistrations.BuildFixedProvider())
{
    FixedInvoiceScheduler? scheduler = TryResolve<FixedInvoiceScheduler>(provider);
    FixedKeyedNotificationRunner? runner = TryResolve<FixedKeyedNotificationRunner>(provider);

    if (scheduler is not null)
    {
        Console.WriteLine($@"  Next invoice run: {scheduler.GetNextRunUtc()}");
    }

    runner?.Run();
}

Console.WriteLine();

#pragma warning disable DI007
static T? TryResolve<T>(IServiceProvider provider) where T : class
{
    string name = typeof(T).Name;

    try
    {
        T resolved = provider.GetRequiredService<T>();
        Console.WriteLine($@"  OK: {name}");
        return resolved;
    }
    catch (Exception ex)
    {
        Console.WriteLine($@"  FAIL: {name}");
        Console.WriteLine($@"    {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}
#pragma warning restore DI007

public static class Di015DemoRegistrations
{
    public static ServiceProvider BuildBrokenProvider()
    {
        var services = new ServiceCollection();
        RegisterBroken(services);
        return services.BuildServiceProvider();
    }

    public static ServiceProvider BuildFixedProvider()
    {
        var services = new ServiceCollection();
        RegisterFixed(services);
        return services.BuildServiceProvider();
    }

    public static void RegisterBroken(IServiceCollection services)
    {
        // DI015: IBrokenClock was never registered.
        services.AddSingleton<BrokenInvoiceScheduler>();

        // DI015: Keyed dependency lookup has no matching keyed registration.
        services.AddSingleton<BrokenKeyedNotificationRunner>(
            sp => new BrokenKeyedNotificationRunner(
                sp.GetRequiredKeyedService<IBrokenNotificationSender>("primary")));
    }

    public static void RegisterFixed(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddKeyedSingleton<INotificationSender, ConsoleNotificationSender>("primary");

        services.AddSingleton<FixedInvoiceScheduler>();
        services.AddSingleton<FixedKeyedNotificationRunner>(
            sp => new FixedKeyedNotificationRunner(
                sp.GetRequiredKeyedService<INotificationSender>("primary")));
    }
}

public interface IBrokenClock
{
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class BrokenInvoiceScheduler
{
    public BrokenInvoiceScheduler(IBrokenClock clock)
    {
    }
}

public sealed class FixedInvoiceScheduler(IClock clock)
{
    public string GetNextRunUtc() => clock.UtcNow.AddHours(1).ToString("O");
}

public interface INotificationSender
{
    void Send(string message);
}

public interface IBrokenNotificationSender
{
}

public sealed class ConsoleNotificationSender : INotificationSender
{
    public void Send(string message) => Console.WriteLine($"  Notification sent: {message}");
}

public sealed class BrokenKeyedNotificationRunner
{
    public BrokenKeyedNotificationRunner(IBrokenNotificationSender sender)
    {
    }
}

public sealed class FixedKeyedNotificationRunner(INotificationSender sender)
{
    public void Run() => sender.Send("Invoices scheduled");
}
