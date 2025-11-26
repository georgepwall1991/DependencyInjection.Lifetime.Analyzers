# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2025-11-26

### Added

- **DI003**: Enhanced Captive Dependency analyzer to support factory delegate registrations (e.g., `AddSingleton(sp => new Service(sp.GetRequiredService<IScoped>()))`).

### Changed

- **RegistrationCollector**: Updated to parse and store factory expressions for analysis.

## [1.2.0] - 2025-11-25

### Added

- **DI010**: New analyzer detecting constructor over-injection (5+ dependencies suggests class may violate SRP)
- **DI011**: New analyzer detecting `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider` injection
  - Excludes factory classes (name ends with "Factory") and middleware classes (has Invoke/InvokeAsync method)
- **.NET 8 Keyed Services Support**: All analyzers now support keyed service patterns
  - `AddKeyedSingleton`, `AddKeyedScoped`, `AddKeyedTransient` registrations
  - `GetKeyedService`, `GetRequiredKeyedService`, `GetKeyedServices` service resolution
  - `IKeyedServiceProvider` detection in DI006, DI007, DI011

### Changed

- Enhanced `WellKnownTypes` with `IKeyedServiceProvider` support
- Updated `RegistrationCollector` to track keyed service registrations
- Updated `DI006_StaticProviderCacheAnalyzer` to detect `IKeyedServiceProvider` in static fields
- Updated `DI007_ServiceLocatorAntiPatternAnalyzer` to detect keyed service resolution methods
- Updated `DI008_DisposableTransientAnalyzer` to detect `AddKeyedTransient` registrations

---

## [1.1.0] - 2025-11-25

### Added

- **DI012**: New analyzer detecting conditional registration misuse
  - **DI012**: `TryAdd*` called after `Add*` for the same service type (will be silently ignored)
  - **DI012b**: Multiple `Add*` calls for the same service type (later registration overrides earlier)
- **DI002 Code Fix**: Added pragma suppression and TODO comment code fixes for scope escape diagnostics
- Extended `RegistrationCollector` infrastructure to track registration order for DI012 analysis

### Changed

- Updated README with DI012 documentation and corrected DI002 code fix availability

---

## [1.0.0] - 2025-11-24

### Added

- **DI004**: Support for modern `using var` declarations (previously only `using` statements were detected)
- Additional edge case test coverage for DI001, DI004, and DI007 analyzers
- Analyzer release tracking files for Roslyn best practices
- CONTRIBUTING.md with contribution guidelines
- Known Limitations section in README

### Fixed

- Build warnings RS2008 and RS1037 resolved
- DI004 now properly detects services used after `using var` scope ends in nested blocks

### Changed

- Version bumped to 1.0.0 for stable release

---

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
