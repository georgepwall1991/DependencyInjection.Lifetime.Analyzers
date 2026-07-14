using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers;

/// <summary>
/// Diagnostic descriptors for all DI lifetime analyzers.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "DependencyInjection";

    /// <summary>
    /// DI005: CreateAsyncScope should be used in async methods instead of CreateScope.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncScopeRequired = new(
        id: DiagnosticIds.AsyncScopeRequired,
        title: "Use CreateAsyncScope in async methods",
        messageFormat: "Use 'CreateAsyncScope' instead of 'CreateScope' in async method '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In async methods, CreateAsyncScope should be used instead of CreateScope to ensure proper async disposal of the scope.");

    /// <summary>
    /// DI006: IServiceProvider, IServiceScopeFactory, or keyed provider stored in static field or property.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticProviderCache = new(
        id: DiagnosticIds.StaticProviderCache,
        title: "Avoid caching IServiceProvider in static members",
        messageFormat: "'{0}' should not be stored in static member '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Storing IServiceProvider, IServiceScopeFactory, IKeyedServiceProvider, or lazy wrappers around them in static members can lead to issues with scope management and service resolution. Consider injecting the provider per-use instead.");

    /// <summary>
    /// DI003: Singleton service captures scoped or transient dependency (captive dependency).
    /// </summary>
    public static readonly DiagnosticDescriptor CaptiveDependency = new(
        id: DiagnosticIds.CaptiveDependency,
        title: "Captive dependency detected",
        messageFormat: "Service '{0}' captures {1} dependency '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A longer-lived service should not depend on shorter-lived services. Capturing shorter-lived services can cause incorrect behavior, memory leaks, or concurrency issues.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI001: IServiceScope must be disposed.
    /// </summary>
    public static readonly DiagnosticDescriptor ScopeMustBeDisposed = new(
        id: DiagnosticIds.ScopeMustBeDisposed,
        title: "Service scope must be disposed",
        messageFormat: "IServiceScope created by '{0}' is not disposed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "IServiceScope implements IDisposable and must be disposed to release resources. Use a 'using' statement or call Dispose() explicitly.");

    /// <summary>
    /// DI002: Scoped service escapes its scope lifetime.
    /// </summary>
    public static readonly DiagnosticDescriptor ScopedServiceEscapes = new(
        id: DiagnosticIds.ScopedServiceEscapes,
        title: "Scoped service escapes scope",
        messageFormat: "Service resolved from scope escapes via '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Services resolved from a scope should not escape the scope's lifetime. Returning or storing scoped services in longer-lived locations can cause issues when the scope is disposed.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI004: Service used after its scope was disposed.
    /// </summary>
    public static readonly DiagnosticDescriptor UseAfterScopeDisposed = new(
        id: DiagnosticIds.UseAfterScopeDisposed,
        title: "Service used after scope disposed",
        messageFormat: "Service '{0}' may be used after its scope is disposed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using a service after its scope has been disposed can cause ObjectDisposedException or other errors. Ensure all service usage occurs within the scope's lifetime.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI007: Service locator anti-pattern detected.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceLocatorAntiPattern = new(
        id: DiagnosticIds.ServiceLocatorAntiPattern,
        title: "Avoid service locator anti-pattern",
        messageFormat: "Consider injecting '{0}' directly instead of resolving via IServiceProvider",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Resolving services via IServiceProvider hides dependencies and makes code harder to test. Prefer constructor injection. Service locator is acceptable in factories, middleware Invoke methods, and when using IServiceScopeFactory correctly.");

    /// <summary>
    /// DI008: Transient service implements IDisposable.
    /// </summary>
    public static readonly DiagnosticDescriptor DisposableTransient = new(
        id: DiagnosticIds.DisposableTransient,
        title: "Transient service implements IDisposable",
        messageFormat: "Transient service '{0}' implements {1} but the container will not track or dispose it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Transient services implementing IDisposable or IAsyncDisposable are not tracked by the DI container and will not be disposed. Consider using Scoped or Singleton lifetime, or use a factory registration where the caller is responsible for disposal.");

    /// <summary>
    /// DI009: Open generic singleton captures scoped or transient dependency.
    /// </summary>
    public static readonly DiagnosticDescriptor OpenGenericLifetimeMismatch = new(
        id: DiagnosticIds.OpenGenericLifetimeMismatch,
        title: "Open generic captive dependency",
        messageFormat: "Open generic singleton '{0}' captures {1} dependency '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An open generic singleton service should not depend on scoped or transient services. The captured service will live for the entire application lifetime.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI012: TryAdd registration will be ignored because service already registered.
    /// </summary>
    public static readonly DiagnosticDescriptor TryAddIgnored = new(
        id: DiagnosticIds.TryAddIgnored,
        title: "TryAdd registration will be ignored",
        messageFormat: "TryAdd for '{0}' will be ignored because Add already registered this service at {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "TryAdd* methods only register a service if it hasn't been registered before. Since an Add* call already registered this service type, this TryAdd* call will have no effect.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI012b: Service registered multiple times; later registration overrides earlier.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateRegistration = new(
        id: DiagnosticIds.DuplicateRegistration,
        title: "Duplicate service registration",
        messageFormat: "Service '{0}' is registered multiple times; later registration overrides earlier one at {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Registering the same service type multiple times with Add* methods means only the last registration will be used. This may be intentional (for overriding) or a mistake.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI010: Constructor has too many dependencies (over-injection).
    /// </summary>
    public static readonly DiagnosticDescriptor ConstructorOverInjection = new(
        id: DiagnosticIds.ConstructorOverInjection,
        title: "Constructor has too many dependencies",
        messageFormat: "Constructor of '{0}' has {1} dependencies - consider refactoring to reduce complexity",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Having more than 4 constructor dependencies may indicate the class has too many responsibilities. Consider extracting functionality into separate services or using aggregate/facade patterns to reduce the number of direct dependencies.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI011: IServiceProvider or IServiceScopeFactory injected directly.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceProviderInjection = new(
        id: DiagnosticIds.ServiceProviderInjection,
        title: "Avoid injecting IServiceProvider or IServiceScopeFactory",
        messageFormat: "'{0}' injects {1} directly - prefer injecting specific dependencies",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Injecting IServiceProvider or IServiceScopeFactory hides dependencies and makes testing harder. Prefer injecting specific services directly. This pattern is acceptable in factories and middleware.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI013: Implementation type mismatch (implementation does not implement service type).
    /// </summary>
    public static readonly DiagnosticDescriptor ImplementationTypeMismatch = new(
        id: DiagnosticIds.ImplementationTypeMismatch,
        title: "Implementation type mismatch",
        messageFormat: "Type '{0}' cannot be used as implementation for service '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The implementation type provided in a 'typeof' registration must implement or inherit from the service type. This will cause a runtime exception if not corrected.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI014: Root service provider not disposed.
    /// </summary>
    public static readonly DiagnosticDescriptor RootProviderNotDisposed = new(
        id: DiagnosticIds.RootProviderNotDisposed,
        title: "Root service provider not disposed",
        messageFormat: "The root IServiceProvider created by 'BuildServiceProvider()' should be disposed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The root service provider implements IDisposable and should be disposed to ensure that any disposable singletons are properly cleaned up.");

    /// <summary>
    /// DI015: Registered service depends on an unregistered dependency.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableDependency = new(
        id: DiagnosticIds.UnresolvableDependency,
        title: "Unresolvable dependency detected",
        messageFormat: "Service '{0}' depends on unregistered service '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A registered service has a constructor or factory dependency that is not registered in the DI container. This commonly causes runtime InvalidOperationException when resolving the service.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI016: BuildServiceProvider called while composing service registrations.
    /// </summary>
    public static readonly DiagnosticDescriptor BuildServiceProviderMisuse = new(
        id: DiagnosticIds.BuildServiceProviderMisuse,
        title: "Avoid BuildServiceProvider during service registration",
        messageFormat: "Avoid calling 'BuildServiceProvider()' while composing service registrations",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling BuildServiceProvider() during service registration creates an additional root container, which can duplicate singletons and cause lifetime inconsistencies.");

    /// <summary>
    /// DI017: Circular dependency detected in constructor injection chain.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularDependency = new(
        id: DiagnosticIds.CircularDependency,
        title: "Circular dependency detected",
        messageFormat: "Service '{0}' has a circular dependency: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A circular dependency chain was detected in the constructor injection graph. This will cause a StackOverflowException at runtime when the DI container attempts to resolve the service.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI018: Implementation type cannot be constructed by the DI container.
    /// </summary>
    public static readonly DiagnosticDescriptor NonInstantiableImplementation = new(
        id: DiagnosticIds.NonInstantiableImplementation,
        title: "Non-instantiable implementation type",
        messageFormat: "Implementation type '{0}' registered for service '{1}' cannot be constructed: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The registered implementation type cannot be constructed by the DI container because it is abstract, an interface, a static class, a delegate, a value type with no declared public constructor, or a class with no accessible constructor. This will cause a runtime exception when the service is resolved.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI019: Scoped service resolved from a root service provider.
    /// </summary>
    public static readonly DiagnosticDescriptor RootScopedResolution = new(
        id: DiagnosticIds.RootScopedResolution,
        title: "Scoped service resolved from root provider",
        messageFormat: "Service '{0}' resolves scoped dependency from the root provider: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Scoped services should be resolved from an IServiceScope, not from the root IServiceProvider. Create a scope with CreateScope or CreateAsyncScope before resolving scoped services.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI020: Middleware captures scoped dependency in constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor MiddlewareScopedService = new(
        id: DiagnosticIds.MiddlewareScopedService,
        title: "Middleware captures scoped dependency in constructor",
        messageFormat: "Middleware '{0}' captures scoped dependency '{1}' in its constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Middleware are typically instantiated once and live for the application lifetime. Injecting scoped services into the constructor will capture them for the entire application lifetime, which can lead to threading issues or stale data. Move scoped dependencies to the 'Invoke' or 'InvokeAsync' method instead.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI021: Non-thread-safe service shared across concurrent handler invocations.
    /// </summary>
    public static readonly DiagnosticDescriptor ConcurrentHandlerSharedState = new(
        id: DiagnosticIds.ConcurrentHandlerSharedState,
        title: "Non-thread-safe service shared across concurrent handler invocations",
        messageFormat: "'{0}' is shared across concurrent invocations of {1}; concurrent use of a {2} fails at runtime. Resolve it from a new scope inside the handler, or use a per-invocation factory.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A non-thread-safe service (such as an EF Core DbContext) that is created or resolved once and then captured into a handler that a framework invokes concurrently (message processors, timers, Parallel.* bodies) will be used by multiple invocations at the same time. Resolve the service from a new IServiceScope created inside the handler, or inject a per-invocation factory such as IDbContextFactory<TContext>.");

    /// <summary>
    /// DI022: Service instance reused across handler invocations of a concurrency-configurable sink.
    /// </summary>
    public static readonly DiagnosticDescriptor ConcurrentHandlerConfigGatedSharedState = new(
        id: DiagnosticIds.ConcurrentHandlerConfigGatedSharedState,
        title: "Service instance reused across handler invocations",
        messageFormat: "'{0}' is captured once and reused across all invocations of {1}. If {2} is raised above 1 this becomes a concurrency crash; even sequentially, one instance accumulates state across all messages. Resolve it per invocation instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A non-thread-safe service is captured once and reused across every invocation of a handler whose concurrency is controlled by configuration that cannot be proven at compile time (for example ServiceBusProcessor with MaxConcurrentCalls bound from configuration). If the concurrency setting is ever raised above 1 this becomes a runtime concurrency failure, and even with sequential dispatch a single instance accumulates state (change tracking, failed-operation poisoning) across all messages. Resolve the service from a new IServiceScope inside the handler.");

    /// <summary>
    /// DI022 (scoped-lifetime tier): a scoped-registered service captured into a concurrently
    /// invoked handler. Same ID as the config-gated tier, with wording that describes the
    /// scoped capture itself; reported at compilation end because lifetimes need the full
    /// registration picture.
    /// </summary>
    public static readonly DiagnosticDescriptor ConcurrentHandlerScopedLifetimeSharedState = new(
        id: DiagnosticIds.ConcurrentHandlerConfigGatedSharedState,
        title: "Service instance reused across handler invocations",
        messageFormat: "'{0}' is registered as scoped but captured once and reused across all invocations of {1}. One instance outlives its intended scope and accumulates state across all messages. Resolve it from a new scope inside the handler.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A service whose registration is scoped is captured (through a field, closure, or enclosing parameter) into a handler that a framework invokes repeatedly. The single captured instance outlives the scope it was designed for and is reused across every invocation, accumulating state and breaking scoped-lifetime expectations. Resolve the service from a new IServiceScope inside the handler.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI024: Hosted service creates a scope outside its long-running execution loop.
    /// </summary>
    public static readonly DiagnosticDescriptor HostedServiceScopePerIteration = new(
        id: DiagnosticIds.HostedServiceScopePerIteration,
        title: "Create a scope per iteration in hosted service execution loops",
        messageFormat: "Scope is created outside the execution loop of '{0}'; services resolved from it live for the whole service lifetime. Move CreateScope/CreateAsyncScope inside the loop body.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A hosted service that creates a single IServiceScope before its long-running execution loop reuses the same scoped service instances (such as an EF Core DbContext) for the entire process lifetime, accumulating state and serving stale data. Create the scope inside the loop body so each iteration gets fresh scoped services.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI024 (hoisted scoped-service tier): a scoped-registered service resolved once before the
    /// execution loop and reused across iterations. Same ID as the hoisted-scope tier; reported at
    /// compilation end because the lifetime proof needs the full registration picture.
    /// </summary>
    public static readonly DiagnosticDescriptor HostedServiceScopedServicePerIteration = new(
        id: DiagnosticIds.HostedServiceScopePerIteration,
        title: "Create a scope per iteration in hosted service execution loops",
        messageFormat: "'{0}' is registered as scoped but resolved once outside the execution loop of '{1}' and reused across every iteration. Resolve it from a new scope inside the loop body.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A hosted service that resolves a scoped service once before its long-running execution loop reuses the same instance (such as an EF Core DbContext) for the entire process lifetime, accumulating state and serving stale data. Create a scope inside the loop body and resolve the service from it so each iteration gets a fresh instance.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI025 (method-group tier): a shorter-lived service subscribes an instance handler to an
    /// event on a longer-lived publisher (singleton dependency or static event) without a
    /// matching unsubscription. Reported at compilation end because the lifetime proof needs
    /// the full registration picture.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeak = new(
        id: DiagnosticIds.EventSubscriptionLeak,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as {1} but subscribes handler '{2}' to event '{3}' on {4} and never unsubscribes. The publisher's delegate list keeps every '{0}' instance alive. Unsubscribe with -= when the subscriber is released (for example in Dispose).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A transient or scoped service that subscribes to an event on a singleton dependency or a static event outlives nothing — but its subscription does. The longer-lived publisher's delegate list roots every subscriber instance the container ever creates, leaking memory on every resolution and invoking stale handlers against released state. Store the subscription and remove it with -= when the subscriber is released.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI025 (anonymous-handler tier): the subscribed handler is an anonymous function that is
    /// never stored, so no equivalent -= can ever remove it.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeakAnonymousHandler = new(
        id: DiagnosticIds.EventSubscriptionLeak,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as {1} but subscribes an anonymous handler to event '{2}' on {3}. The handler is never stored, so it can never be unsubscribed, and the publisher's delegate list keeps every '{0}' instance alive. Store the handler in a field and remove it with -= when the subscriber is released.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A transient or scoped service subscribes an anonymous handler to an event on a singleton dependency or a static event without storing the delegate. An anonymous handler that is not stored can never be unsubscribed — a -= with a textually identical lambda creates a different delegate instance and removes nothing. Store the handler in a field and use the same reference for += and -=.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI025 (ineffective-unsubscribe tier): a -= exists for the same event but uses a distinct
    /// anonymous delegate instance, so it never removes the subscribed handler.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeakIneffectiveUnsubscribe = new(
        id: DiagnosticIds.EventSubscriptionLeak,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as {1} and subscribes an anonymous handler to event '{2}' on {3}, but the corresponding -= creates a new delegate instance and never removes the handler. Store the delegate once and use the same reference for += and -=.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A -= with an anonymous function that is textually identical to the subscribed handler creates a different delegate instance, so the removal is a no-op and the longer-lived publisher keeps rooting every subscriber instance. Store the delegate once (in a field) and use the same reference on both the += and -= sides.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI026 (method-group tier): a transient service subscribes an instance handler to an
    /// event on a scoped publisher without a matching unsubscription. Info rather than
    /// Warning because the publisher's delegate list dies with its scope — the accumulation
    /// is bounded, but stale handlers still fire on released instances while the scope lives.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeakScopedPublisher = new(
        id: DiagnosticIds.EventSubscriptionLeakScopedPublisher,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as transient but subscribes handler '{1}' to event '{2}' on {3} and never unsubscribes. For as long as that scope lives, the publisher's delegate list grows and roots every '{0}' the scope creates, and the event keeps invoking handlers on released instances. Unsubscribe with -= when the subscriber is released (for example in Dispose).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A transient service that subscribes to an event on a scoped publisher leaks for the lifetime of the scope: every transient instance the scope resolves stays rooted in the publisher's delegate list until the scope is disposed, and the event keeps invoking handlers on instances the container has already released. In a short per-request scope the accumulation is usually harmless; in long-lived scopes (SignalR connections, Blazor circuits, hosted-service loop scopes) it is a real leak. Reported at Info because the impact depends on scope longevity — raise it per team policy with dotnet_diagnostic.DI026.severity = warning.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI026 (anonymous-handler tier): the handler subscribed to the scoped publisher is an
    /// anonymous function that is never stored, so no equivalent -= can ever remove it.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeakScopedPublisherAnonymousHandler = new(
        id: DiagnosticIds.EventSubscriptionLeakScopedPublisher,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as transient but subscribes an anonymous handler to event '{1}' on {2}. The handler is never stored, so it can never be unsubscribed, and for as long as that scope lives the publisher's delegate list keeps every '{0}' instance alive. Store the handler in a field and remove it with -= when the subscriber is released.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A transient service subscribes an anonymous handler to an event on a scoped publisher without storing the delegate. An anonymous handler that is not stored can never be unsubscribed — a -= with a textually identical lambda creates a different delegate instance and removes nothing — so every transient instance stays rooted until the scope is disposed. Store the handler in a field and use the same reference for += and -=.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI026 (ineffective-unsubscribe tier): a -= exists for the same event on the scoped
    /// publisher but uses a distinct anonymous delegate instance, so it never removes the
    /// subscribed handler.
    /// </summary>
    public static readonly DiagnosticDescriptor EventSubscriptionLeakScopedPublisherIneffectiveUnsubscribe = new(
        id: DiagnosticIds.EventSubscriptionLeakScopedPublisher,
        title: "Unsubscribe from longer-lived publishers before the subscriber is released",
        messageFormat: "'{0}' is registered as transient and subscribes an anonymous handler to event '{1}' on {2}, but the corresponding -= creates a new delegate instance and never removes the handler. Store the delegate once and use the same reference for += and -=.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A -= with an anonymous function that is textually identical to the subscribed handler creates a different delegate instance, so the removal is a no-op and the scoped publisher keeps rooting every transient subscriber instance until its scope is disposed. Store the delegate once (in a field) and use the same reference on both the += and -= sides.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// DI027: a shorter-lived service subscribes an instance-capturing handler to an observable
    /// on a longer-lived publisher (a singleton dependency, or a scoped publisher with a
    /// transient subscriber) and discards the IDisposable subscription token. The Rx twin of
    /// DI025 — where DI025 proves a missing <c>-=</c>, DI027 proves the returned token is thrown
    /// away, which leaves the observer (and the subscriber it captures) rooted just the same.
    /// Reported at compilation end because the lifetime proof needs the full registration picture.
    /// </summary>
    public static readonly DiagnosticDescriptor RxSubscriptionLeak = new(
        id: DiagnosticIds.RxSubscriptionLeak,
        title: "Dispose the subscription returned by Subscribe on a longer-lived observable",
        messageFormat: "'{0}' is registered as {1} but discards the IDisposable returned by subscribing {2} to observable '{3}' on {4}. The subscription roots every '{0}' instance the container creates; store the token and dispose it when the subscriber is released.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "IObservable<T>.Subscribe returns an IDisposable that unsubscribes the observer when disposed. A transient or scoped service that subscribes to an observable exposed by a longer-lived publisher (a singleton dependency, or a scoped publisher shared by a transient) and discards that token — as an ignored expression statement or a discard assignment — leaves the observer registered for the publisher's whole lifetime. The publisher roots every subscriber instance the container ever creates, leaking memory on each resolution and invoking stale observers against released state. Store the token and dispose it when the subscriber is released (for example in Dispose, or via a CompositeDisposable).",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
