# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2024-11-24

### Added

#### Analyzers

- **DI001**: Detect undisposed `IServiceScope` instances
- **DI002**: Detect scoped services escaping their scope lifetime
- **DI003**: Detect captive dependencies (singleton capturing scoped/transient)
- **DI004**: Detect service usage after scope disposal
- **DI005**: Detect `CreateScope()` usage in async methods (should use `CreateAsyncScope()`)
- **DI006**: Detect `IServiceProvider` or `IServiceScopeFactory` cached in static members
- **DI007**: Detect service locator anti-pattern
- **DI008**: Detect transient services implementing `IDisposable`/`IAsyncDisposable`
- **DI009**: Detect open generic singletons capturing scoped/transient dependencies

#### Code Fixes

- **DI001**: Add `using` or `await using` statement
- **DI003**: Change service lifetime to `Scoped` or `Transient`
- **DI005**: Replace `CreateScope()` with `CreateAsyncScope()`
- **DI006**: Remove `static` modifier from field/property
- **DI008**: Change lifetime to `Scoped` or `Singleton`
- **DI009**: Change open generic lifetime to `Scoped` or `Transient`
