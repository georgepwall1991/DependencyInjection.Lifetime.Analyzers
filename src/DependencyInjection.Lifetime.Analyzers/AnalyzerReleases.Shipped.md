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
