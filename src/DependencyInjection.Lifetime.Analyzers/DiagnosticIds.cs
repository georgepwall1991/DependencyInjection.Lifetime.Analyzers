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
    /// DI006: IServiceProvider, IServiceScopeFactory, or keyed provider stored in static member.
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

    /// <summary>
    /// DI016: BuildServiceProvider used during service registration.
    /// </summary>
    public const string BuildServiceProviderMisuse = "DI016";

    /// <summary>
    /// DI017: Circular dependency detected in constructor injection chain.
    /// </summary>
    public const string CircularDependency = "DI017";

    /// <summary>
    /// DI018: Implementation type cannot be constructed by the DI container.
    /// </summary>
    public const string NonInstantiableImplementation = "DI018";

    /// <summary>
    /// DI019: Scoped service resolved from root provider.
    /// </summary>
    public const string RootScopedResolution = "DI019";

    /// <summary>
    /// DI020: Middleware captures scoped dependency in constructor.
    /// </summary>
    public const string MiddlewareScopedService = "DI020";

    /// <summary>
    /// DI021: Non-thread-safe service shared across concurrent handler invocations.
    /// </summary>
    public const string ConcurrentHandlerSharedState = "DI021";

    /// <summary>
    /// DI022: Service instance captured once and reused across handler invocations of a
    /// concurrency-configurable sink whose concurrency setting cannot be proven.
    /// </summary>
    public const string ConcurrentHandlerConfigGatedSharedState = "DI022";

    /// <summary>
    /// DI031: One implementation type is registered under several service types with a shared
    /// lifetime, so the container builds a separate instance for each registration.
    /// </summary>
    public const string SharedImplementationSeparateInstances = "DI031";

    /// <summary>
    /// DI023: Fire-and-forget background work (Task.Run, TaskFactory.StartNew) captures a scope,
    /// a scope's provider, or a service resolved from one. The scope is disposed when the
    /// starting method returns while the thread-pool work is still running.
    /// </summary>
    public const string FireAndForgetScopeCapture = "DI023";

    /// <summary>
    /// DI024: Hosted service creates a scope (or resolves a scoped service) outside its
    /// long-running execution loop instead of once per iteration.
    /// </summary>
    public const string HostedServiceScopePerIteration = "DI024";

    /// <summary>
    /// DI025: Shorter-lived service subscribes to an event on a longer-lived publisher
    /// without a matching unsubscription, rooting every subscriber instance in the
    /// publisher's delegate list.
    /// </summary>
    public const string EventSubscriptionLeak = "DI025";

    /// <summary>
    /// DI026: Transient service subscribes to an event on a scoped publisher without a
    /// matching unsubscription. The Info tier of DI025 — the publisher's delegate list
    /// grows only for the lifetime of its scope, not the process.
    /// </summary>
    public const string EventSubscriptionLeakScopedPublisher = "DI026";

    /// <summary>
    /// DI027: Shorter-lived service subscribes to an observable on a longer-lived publisher
    /// and discards the IDisposable subscription token, so the observer (and the subscriber
    /// it captures) stays rooted for the publisher's lifetime. The Rx twin of DI025 —
    /// the leak proof inverts from "missing -=" to "discarded token".
    /// </summary>
    public const string RxSubscriptionLeak = "DI027";

    /// <summary>
    /// DI028: Shorter-lived service registers a callback on a longer-lived registration source —
    /// IOptionsMonitor.OnChange, CancellationToken.Register, ChangeToken.OnChange,
    /// IChangeToken.RegisterChangeCallback, or a linked CancellationTokenSource — and discards the
    /// returned registration. The callback captures the subscriber, so the source roots every
    /// subscriber instance for its own lifetime. The third member of the DI025/DI027 family:
    /// the leak proof is the same discarded-token shape applied to callback registrations.
    /// </summary>
    public const string CallbackRegistrationLeak = "DI028";

    /// <summary>
    /// DI029: HttpClient or one of its pooling handlers is constructed on a per-invocation path,
    /// exhausting sockets under load, or an HttpClient is handed to the container as a singleton or
    /// held in a static member, pinning a handler that then never rotates and never observes DNS
    /// changes. Both are lifetime defects in the connection pool the container should own.
    /// </summary>
    public const string HttpClientLifetimeMisuse = "DI029";

    /// <summary>
    /// DI030: A collection owned by a singleton service or held in a static member is written with
    /// request-derived keys and never evicted, or an IMemoryCache entry is written with neither an
    /// expiration nor a size limit. The store grows monotonically for the life of the process.
    /// </summary>
    public const string UnboundedSingletonCache = "DI030";
}
