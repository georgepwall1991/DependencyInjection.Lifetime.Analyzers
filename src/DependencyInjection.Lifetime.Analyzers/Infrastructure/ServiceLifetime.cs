namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Specifies the lifetime of a service in the dependency injection container.
/// Mirrors Microsoft.Extensions.DependencyInjection.ServiceLifetime.
/// </summary>
public enum ServiceLifetime
{
    /// <summary>
    /// A single instance is created and shared throughout the application lifetime.
    /// </summary>
    Singleton = 0,

    /// <summary>
    /// A new instance is created for each scope.
    /// </summary>
    Scoped = 1,

    /// <summary>
    /// A new instance is created every time it is requested.
    /// </summary>
    Transient = 2
}
