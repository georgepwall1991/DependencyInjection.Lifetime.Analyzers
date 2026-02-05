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
