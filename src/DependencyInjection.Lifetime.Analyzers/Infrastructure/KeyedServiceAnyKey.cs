namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Sentinel for Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey registrations.
/// </summary>
internal sealed class KeyedServiceAnyKey
{
    public static readonly KeyedServiceAnyKey Instance = new KeyedServiceAnyKey();

    private KeyedServiceAnyKey()
    {
    }

    public override string ToString() => "KeyedService.AnyKey";
}
