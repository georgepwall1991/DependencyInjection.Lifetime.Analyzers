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
    /// DI006: IServiceProvider or IServiceScopeFactory stored in static field or property.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticProviderCache = new(
        id: DiagnosticIds.StaticProviderCache,
        title: "Avoid caching IServiceProvider in static members",
        messageFormat: "'{0}' should not be stored in static member '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Storing IServiceProvider or IServiceScopeFactory in static fields can lead to issues with scope management and service resolution. Consider injecting the service provider per-use instead.");

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
        defaultSeverity: DiagnosticSeverity.Warning,
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
        defaultSeverity: DiagnosticSeverity.Warning,
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
}
