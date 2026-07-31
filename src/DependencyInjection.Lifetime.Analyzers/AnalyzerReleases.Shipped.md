; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI001 | DependencyInjection | Warning | Service scope must be disposed
DI002 | DependencyInjection | Warning | Scoped service escapes scope
DI003 | DependencyInjection | Warning | Captive dependency detected
DI004 | DependencyInjection | Warning | Service used after scope disposed
DI005 | DependencyInjection | Warning | Use CreateAsyncScope in async methods
DI006 | DependencyInjection | Warning | Avoid caching IServiceProvider in static members
DI007 | DependencyInjection | Warning | Avoid service locator anti-pattern
DI008 | DependencyInjection | Warning | Transient service implements IDisposable
DI009 | DependencyInjection | Warning | Open generic captive dependency

## Release 1.7.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI010 | DependencyInjection | Info | Constructor has too many dependencies
DI011 | DependencyInjection | Warning | Avoid injecting IServiceProvider/IServiceScopeFactory
DI012 | DependencyInjection | Info | TryAdd registration will be ignored because service already registered
DI012b | DependencyInjection | Info | Service registered multiple times; later registration overrides earlier
DI013 | DependencyInjection | Error | Implementation type does not implement service type (runtime exception)
DI014 | DependencyInjection | Warning | Root IServiceProvider created by BuildServiceProvider() is not disposed

## Release 1.10.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI015 | DependencyInjection | Warning | Registered service depends on unregistered dependency (constructor/factory)

## Release 2.1.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI016 | DependencyInjection | Warning | Avoid BuildServiceProvider() while composing service registrations (duplicate container risk)

## Release 2.3.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI017 | DependencyInjection | Warning | Circular dependency detected in constructor injection chain
DI018 | DependencyInjection | Warning | Non-instantiable implementation type (abstract, interface, static, no public constructors)

## Release 2.7.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI019 | DependencyInjection | Warning | Scoped service resolved from root provider

## Release 2.9.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI020 | DependencyInjection | Warning | Middleware captures scoped dependency in constructor

## Release 2.10.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI021 | DependencyInjection | Warning | Non-thread-safe service shared across concurrent handler invocations
DI022 | DependencyInjection | Info | Service instance reused across handler invocations of a concurrency-configurable sink

## Release 2.11.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI024 | DependencyInjection | Warning | Hosted service creates a scope or resolves a scoped service outside its long-running execution loop

## Release 2.12.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI025 | DependencyInjection | Warning | Shorter-lived service subscribes to an event on a longer-lived publisher (singleton dependency or static event) without a matching unsubscription

## Release 2.13.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI026 | DependencyInjection | Info | Transient service subscribes to an event on a scoped publisher without a matching unsubscription (scope-bounded tier of DI025)

## Release 2.1.2

### Changed Rules
Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
DI007 | DependencyInjection | Info | DependencyInjection | Warning | Defaulted to Info to keep broad service-locator guidance from becoming warning-level noise
DI011 | DependencyInjection | Info | DependencyInjection | Warning | Defaulted to Info because IServiceProvider injection is a design smell rather than a definite runtime bug

## Release 2.18.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI027 | DependencyInjection | Warning | Shorter-lived service subscribes to an observable on a longer-lived publisher and discards the IDisposable subscription token

## Release 3.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI028 | DependencyInjection | Warning | Shorter-lived service registers a callback on a longer-lived registration source and discards the returned registration
DI029 | DependencyInjection | Warning | HttpClient or a pooling handler constructed per invocation, or an HttpClient registered as a singleton or held in a static member
DI030 | DependencyInjection | Info | Singleton-owned or static collection grows with request-derived keys and is never evicted, or an IMemoryCache entry has neither expiration nor size

## Release 3.1.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI023 | DependencyInjection | Warning | Fire-and-forget background work captures a using scope, its provider, or a service resolved from it

## Release 3.2.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI031 | DependencyInjection | Info | One implementation type registered under several service types with a shared lifetime, producing a separate instance per registration

## Release 3.3.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI032 | DependencyInjection | Warning | Container-created service implements only IAsyncDisposable, so synchronous provider or scope disposal throws
DI033 | DependencyInjection | Info | Disposable instance registered as a pre-built singleton, which the container never disposes

## Release 3.4.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI034 | DependencyInjection | Warning | HttpContext reaches fire-and-forget background work that outlives the request

## Release 3.5.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI035 | DependencyInjection | Warning | One non-thread-safe service shared by every task of a Task.WhenAll fan-out

## Release 3.6.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI036 | DependencyInjection | Warning | Registration runs after a provider was already built from the same IServiceCollection
