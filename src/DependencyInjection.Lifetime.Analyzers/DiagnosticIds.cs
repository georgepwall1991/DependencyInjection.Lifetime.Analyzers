namespace DependencyInjection.Lifetime.Analyzers;

/// <summary>
/// Diagnostic IDs for all DI lifetime analyzers.
/// </summary>
public static class DiagnosticIds
{
    /// <summary>
    /// DI001: IServiceScope must be disposed.
    /// </summary>
    public const string ScopeMustBeDisposed = "DI001";

    /// <summary>
    /// DI002: Scoped service escapes its scope lifetime.
    /// </summary>
    public const string ScopedServiceEscapes = "DI002";

    /// <summary>
    /// DI003: Singleton captures scoped or transient dependency (captive dependency).
    /// </summary>
    public const string CaptiveDependency = "DI003";

    /// <summary>
    /// DI004: Service used after its scope was disposed.
    /// </summary>
    public const string UseAfterScopeDisposed = "DI004";

    /// <summary>
    /// DI005: CreateAsyncScope should be used in async methods.
    /// </summary>
    public const string AsyncScopeRequired = "DI005";

    /// <summary>
    /// DI006: IServiceProvider or IServiceScopeFactory stored in static field.
    /// </summary>
    public const string StaticProviderCache = "DI006";

    /// <summary>
    /// DI007: Service locator anti-pattern - prefer constructor injection.
    /// </summary>
    public const string ServiceLocatorAntiPattern = "DI007";

    /// <summary>
    /// DI008: Transient service implements IDisposable but container won't dispose it.
    /// </summary>
    public const string DisposableTransient = "DI008";

    /// <summary>
    /// DI009: Open generic singleton captures scoped or transient dependency.
    /// </summary>
    public const string OpenGenericLifetimeMismatch = "DI009";

    /// <summary>
    /// DI010: Constructor has too many dependencies (over-injection).
    /// </summary>
    public const string ConstructorOverInjection = "DI010";

    /// <summary>
    /// DI011: IServiceProvider or IServiceScopeFactory injected directly.
    /// </summary>
    public const string ServiceProviderInjection = "DI011";

    /// <summary>
    /// DI012: TryAdd registration will be ignored because service already registered.
    /// </summary>
    public const string TryAddIgnored = "DI012";

    /// <summary>
    /// DI012b: Service registered multiple times; later registration overrides earlier.
    /// </summary>
    public const string DuplicateRegistration = "DI012b";
    public const string ConditionalRegistrationMisuse = "DI012";
    public const string ImplementationTypeMismatch = "DI013";
    public const string RootProviderNotDisposed = "DI014";

    /// <summary>
    /// DI015: Registered service depends on an unregistered dependency.
    /// </summary>
    public const string UnresolvableDependency = "DI015";
}
