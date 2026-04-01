# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.4.2] - 2026-04-01

### Changed

- **DI002 Executable-Root Coverage**: Scoped-service escape analysis now runs across constructors, accessors, local functions, lambdas, and anonymous methods instead of only ordinary methods, while still keeping escape tracking local to the current executable boundary.
- **DI004 Executable-Root Coverage**: Use-after-dispose analysis now follows the same broader executable-root set, including constructor and accessor bodies plus local and anonymous functions, without speculating across nested executable boundaries.
- **DI002/DI004 Regression Guardrails**: Added direct should-report and should-not-report coverage for constructor, property-getter, local-function, anonymous-method, and nested-boundary cases so the widened entry surface stays conservative.

## [2.4.1] - 2026-04-01

### Changed

- **DI010 Runtime-Faithful Constructor Selection**: Constructor over-injection now analyzes the constructor set the container could actually activate instead of every accessible constructor, reducing false positives on multi-constructor services where only a shorter constructor is resolvable.
- **DI010 Conservative Factory Coverage**: The analyzer now covers straightforward factory registrations that directly construct the service and `ActivatorUtilities.CreateInstance<T>(sp)` factory paths, while staying quiet for more dynamic factory bodies it cannot classify confidently.
- **DI010 Symbol-Accurate Exclusions and Config**: Logging, options, configuration, and provider-plumbing exclusions now use exact symbols instead of simple names, optional/default-value parameters are ignored, and the dependency threshold is configurable through `.editorconfig` with `dotnet_code_quality.DI010.max_dependencies`.
- **DI010 Regression Coverage and Health**: Added direct tests for constructor selection, factory registrations, namespace-accurate exclusions, optional parameters, and threshold overrides, and refreshed the analyzer health snapshot to reflect the stronger rule quality.

## [2.4.0] - 2026-03-31

### Changed

- **DI002 Scope/Alias Tracking**: Scoped-service escape analysis now follows scopes created before a `using (scope)` statement, recognizes provider aliases, and stays quiet after safe alias overwrites instead of keeping stale escape state alive.
- **DI004 Scope/Alias Disposal Coverage**: Use-after-dispose analysis now recognizes services resolved from predeclared scopes and provider aliases, and it clears stale tracked aliases after safe overwrites to avoid new false positives.
- **DI009 Registration-Shape Coverage**: Open-generic captive-dependency coverage now has direct tests for `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, and keyed open-generic singleton registrations, plus no-diagnostic guardrails for mismatched keys and ineffective `TryAddSingleton(...)`.
- **Analyzer Health Snapshot**: Refreshed the rule-health scoring after the DI002/DI004/DI009 hardening pass and rolled the direct-test counts forward to the current suite.

## [2.3.9] - 2026-03-31

### Changed

- **DI016 Minimal-Hosting Coverage**: BuildServiceProvider misuse detection now recognizes top-level registration code that flows from a builder-style `.Services` property, closing the missed-diagnostic gap for minimal-hosting registration composition without expanding the rule to every top-level `IServiceCollection` usage.
- **DI016 Diagnostic Precision**: DI016 now reports on the `BuildServiceProvider` member name instead of highlighting the full invocation, producing tighter squiggles in the IDE and test output.
- **DI016 Regression Coverage**: Added tests for `IServiceCollection` variable indirection, top-level `.Services` aliases, and a top-level standalone `new ServiceCollection()` no-diagnostic guardrail so the rule stays conservative.

## [2.3.8] - 2026-03-31

### Changed

- **DI018 Open-Generic Activatability**: Non-instantiable implementation detection now checks unbound generic registrations against the generic type definition, so open generic implementations with only private, internal, or protected constructors are reported instead of slipping through to runtime activation failures.
- **DI018 Registration-Shape Coverage**: Added direct guardrails for keyed registrations, `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, `ServiceDescriptor.Describe(...)`, `typeof(...)` registrations, and factory/implementation-instance silence so DI018 stays aligned with the collector paths the analyzers actually support.
- **DI018 Sample and Docs Parity**: Expanded the DI018 sample, contract, and rule docs to include constructor-accessibility failures in addition to abstract implementations, and refreshed the analyzer health snapshot to reflect the stronger coverage.

## [2.3.7] - 2026-03-31

### Changed

- **Instance-Backed Registration Fidelity**: Shared registration metadata now tracks pre-built implementation instances explicitly so constructor-driven analyzers and dependency resolution stay aligned with runtime behavior instead of treating existing objects like activatable implementation types.
- **False-Positive Reduction Across Rules**: DI003, DI010, DI011, DI015, DI017, and DI018 now stay silent for valid implementation-instance registrations, including `ServiceDescriptor` instance forms and generic `implementationInstance:` overloads.
- **DI017 Determinism Hardening**: Circular-dependency analysis now builds its effective registration set from stable source order, preventing duplicate or mixed instance-plus-constructed registrations from changing results due to concurrent discovery order.
- **Sample Verification Stability**: Full-solution test runs now prebuild the analyzer and rebuild sample projects without rebuilding project references, avoiding fragile analyzer-output races in the sample diagnostics verifier.
- **Regression Coverage Expansion**: Added direct tests for generic and named implementation-instance collection, `ServiceDescriptor` instance shapes, mixed instance-plus-constructed DI017 graphs, and the cross-rule fallout cases that motivated this pass.

## [2.3.6] - 2026-03-31

### Changed

- **DI013 Open-Generic Compatibility Hardening**: Implementation-type mismatch analysis now rejects open-service registrations backed by closed generic implementations and validates open-generic projection ordering and arity more precisely across interface and base-type mappings.
- **DI013 Registration-Shape Coverage**: Added direct guardrails for `ServiceDescriptor.Singleton(...)`, `ServiceDescriptor.Describe(...)`, keyed registrations, `TryAddSingleton(...)`, implementation-instance registrations, and other collector-fed registration shapes so compatibility checks stay aligned with the registrations the analyzers actually understand.
- **Analyzer Health Snapshot**: Added `docs/ANALYZER_HEALTH.md` with per-rule scoring, pass recommendations, and current verification status to make future hardening priorities explicit.

## [2.3.5] - 2026-03-31

### Changed

- **DI005 Extension-Method Coverage**: Added analyzer and code-fix coverage for `IServiceProvider.CreateScope()` in async and sync contexts, including both `using var` and block-form `using (...)` fixer rewrites to `CreateAsyncScope()`.
- **DI005 Ownership and Boundary Guardrails**: Locked expected behavior for `CreateScope()` passed into helper methods, returned from async methods, synchronously disposed inside async methods, and used inside async anonymous methods.
- **DI005 Async-Context Precision**: Added no-diagnostic guardrails for synchronous lambdas and synchronous local functions nested inside async methods so future DI005 changes preserve correct executable-boundary behavior.

## [2.3.4] - 2026-03-31

### Changed

- **DI001 Explicit Disposal Accuracy**: Scope-disposal analysis now recognizes `scope?.Dispose()` and reassignment-to-local disposal patterns, reducing false positives for valid explicit disposal code.
- **DI001 Reassignment Leak Detection**: Explicit disposal tracking now rejects dispose proofs when the local is reassigned before disposal, including non-scope reassignments, while respecting executable boundaries so lambda-only reassignments do not create noise.
- **DI001 Regression Coverage**: Added guardrail tests for conditional disposal, reassignment-to-local, intervening reassignments, expression-based `using (CreateScope())`, and `IServiceProvider.CreateScope()` report/no-report behavior.

## [2.3.3] - 2026-03-31

### Changed

- **DI003 Deduplication**: Captive-dependency reporting now emits one diagnostic per unique captured dependency per registration, reducing duplicate reports when the same scoped/transient service is resolved multiple times inside one singleton factory.
- **Shared Key and Constant Helpers**: Extracted keyed-parameter parsing and constant-value extraction into shared infrastructure so DI003, DI015, and registration/factory analysis stay aligned on keyed-service semantics.
- **DI003 Regression Coverage**: Added guardrail tests for `TryAddSingleton` captive dependencies, open-generic constructor no-diagnostic behavior, repeated factory resolutions of the same scoped dependency, and keyed factory resolutions with a `null` key.

## [2.3.2] - 2026-03-30

### Changed

- **DI008 Keyed Registration Fixes**: `AddKeyedTransient(...)` is now fully supported by the DI008 code fix provider, including keyed lifetime rewrites to `AddKeyedScoped` / `AddKeyedSingleton` and keyed factory rewrites that preserve the original key argument and generate the correct two-parameter factory lambda shape.
- **DI008 Factory Detection Hardening**: Disposable-transient analysis now exercises both delegate-variable and method-group factory registrations for keyed and non-keyed DI paths, keeping DI008 conservative when registrations already use caller-owned factory delegates.
- **DI008 Regression Coverage**: Added DI 8 keyed-service analyzer/code-fix tests, guardrails for no-fix `typeof(...)` factory conversions, and named-key preservation checks so the highest-risk keyed paths are locked by executable coverage instead of code inspection alone.

## [2.3.1] - 2026-03-30

### Changed

- **DI006 Inherited-Type Coverage**: Static provider cache analysis now catches custom interfaces that inherit from `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider`, closing a real false-negative path for wrapper abstractions.
- **DI006 Fixer Safety**: The remove-`static` code fix now stays conservative by refusing fixes for static classes, non-private members, partial types, static local-function usage, and other cases where rewriting could silently break references or produce invalid code.
- **DI006 Regression Coverage**: Added targeted should-fix and should-not-fix tests for inherited interface detection, public member no-fix behavior, implicitly private members, static local functions, static classes, multi-variable fields, and partial-type guardrails.

## [2.3.0] - 2026-03-30

### Added

- **DI017 Circular Dependency Detection**: New diagnostic that detects direct, transitive, and self-referential circular dependencies in constructor injection chains. Uses keyed-service-aware resolution with memoized graph traversal for performance. Conservative by design — skips factory registrations, optional parameters, and `IEnumerable<T>` dependencies to avoid false positives. Reports deterministically on the lexicographically smallest service type in each cycle.
- **DI018 Non-Instantiable Implementation Detection**: New diagnostic that catches implementation types the DI container cannot construct — abstract classes, interfaces, static classes, and types with no public constructors. Skips factory registrations where the factory handles construction. Catches registration mistakes that would otherwise surface as runtime `InvalidOperationException`.
- **Sample Parity Enforcement**: New contract tests that enforce every public diagnostic ID has sample coverage with concrete claim anchors, all contract folder claims reference existing sample directories, and all claimed diagnostic IDs exist in the public inventory.
- **Cross-Rule Interaction Tests**: New test suite validating expected multi-rule behavior — DI007+DI011, DI003+DI015, DI008+DI012b, and DI010+DI011 interactions on shared source.
- **Performance Regression Gate**: Deterministic stress tests with 200-service registration sets, diamond dependency patterns, and large-scale cycle detection. Memoized cycle detection prevents exponential blowup on acyclic graphs.

### Changed

- **DI012 Accuracy**: Wrapper flow ordering restored for invoked helper methods so TryAdd-after-Add detection correctly follows service collection parameters across method boundaries.
- **DI011 Precision**: Provider heuristics narrowed to honor selected constructors and reduce service-locator escape hatches, keeping diagnostics focused on real DI misuse.
- **DI012 Flow Scoping**: Duplicate registration analysis scoped to service-collection flows to prevent cross-wrapper false positives.
- **Sample Verification Hardening**: SARIF-based contract verification tightened with approved secondary diagnostic allowlisting, public diagnostic parity checks, and sample/docs freshness gate integration.

## [2.2.2] - 2026-03-17

### Changed

- **DI015 Wrapper Reachability**: DI015 now expands source-visible `IServiceCollection` wrapper extensions from real invocation sites instead of treating registrations inside uncalled wrappers as globally available.
- **DI015 Opaque Wrapper Suppression**: Earlier opaque or external wrappers on the same `IServiceCollection` flow now suppress DI015 when registration state is uncertain, reducing false positives in layered registration modules.
- **DI015 Ordering and Flow Isolation**: Wrapper-aware resolution now respects call order and per-collection flow, so wrappers invoked on a different `IServiceCollection` instance or after a registration do not hide genuine missing-dependency diagnostics.
- **DI015 Regression Coverage**: Added should-report and should-not-report guardrails for invoked vs uninvoked wrappers, nested wrapper chains, cyclic wrappers, keyed mismatches, opaque external wrappers, same-method flow isolation, and registration ordering.
- **Release Metadata Sync**: Backfilled the missing `2.2.1` changelog entry and resynced package metadata so the tagged release train and project version history are aligned again.

## [2.2.1] - 2026-03-16

### Changed

- **DI013 Compatibility Hardening**: Expanded implementation-type validation beyond simple `typeof(service), typeof(implementation)` assignability checks so DI013 now also reports deterministic-invalid self-registrations, abstract/interface implementations, private-constructor implementations, and invalid implementation instances when their exact runtime type is known.
- **DI013 Open Generic Precision**: Reworked open-generic validation to require exact generic-parameter projection compatibility, rejecting arity mismatches, reordered parameters, transformed generic arguments, and other registrations that compile but cannot be activated by the built-in container.
- **Collector and Engine Support**: Extended registration collection to recognize non-factory implementation-instance overloads, keyed registrations, and equivalent `ServiceDescriptor` forms while keeping factory registrations out of DI013 scope and preventing constructor-based analyzers from treating pre-built instances like activatable implementation types.
- **Regression Coverage and Docs**: Expanded DI013 tests across valid/invalid closed, open-generic, keyed, `TryAdd*`, `ServiceDescriptor`, and implementation-instance scenarios, and updated `README.md`, `docs/RULES.md`, the sample app, and descriptor wording to match the hardened behavior.

## [2.2.0] - 2026-03-16

### Changed

- **DI015 Precision Refactor**: Moved unresolved-dependency analysis onto a shared resolution engine with explicit confidence/provenance tracking, so constructor, factory, keyed, open-generic, and strict-mode paths share the same conservative resolution rules.
- **DI015 TryAdd/Descriptor Coverage**: Effective `TryAdd*` registrations now participate in DI015 analysis, while shadowed `TryAdd*` registrations stay silent to match runtime behaviour; coverage also now includes `ServiceDescriptor` registration forms and direct `IKeyedServiceProvider` resolutions.
- **DI015 Code Fix**: Added a narrow safe fix that inserts a self-binding registration for one direct concrete constructor dependency when the registration site is local and unambiguous; factory-rooted, keyed, abstract, multi-missing, and transitive-only cases intentionally remain no-fix.
- **DI015 Guardrails**: Expanded DI015 regression coverage with paired should-report / should-not-report scenarios for `TryAdd*`, `ServiceDescriptor`, framework-provided dependencies, opaque duplicate registrations, and fixer/no-fixer boundaries.
- **Packaging Metadata**: Expanded NuGet description, package tags, and release notes so search and package landing pages better describe DI lifetime, scope, and registration coverage.
- **Adoption Docs**: Added `docs/ADOPTION.md` and linked it from `README.md` so teams evaluating the analyzer have a fast install and rollout path.
- **Repository Intake**: Added GitHub issue-template routing to point users toward setup guidance and the full rule reference before they file issues.
- **Growth Automation**: Added `tools/generate-growth-assets.mjs` to generate a searchable static docs site, problem-intent landing pages, version-synced README install snippets, and curated release notes from the repo source material.
- **GitHub Pages**: Added automated Pages publishing plus CI verification for the generated docs site, sitemap, robots.txt, and search index.
- **Release Surfaces**: Updated release automation to generate GitHub Release body content and package release-note input from `CHANGELOG.md` instead of relying on generic auto-generated notes.

## [2.1.4] - 2026-03-16

### Changed

- **DI003 Factory Coverage**: Expanded captive-dependency analysis to cover high-confidence factory paths including method-group factories, local-function factories, keyed resolutions, `ActivatorUtilities.CreateInstance(...)`, and open-generic lifetime lookups.
- **DI003 Signal Quality**: Added diagnostic deduplication for equivalent captive constructor findings and kept analysis conservative when dependency lifetimes or constructor activation paths are ambiguous.
- **DI003 Code Fixes**: Extended safe lifetime rewrites beyond direct `AddSingleton` calls to include keyed registrations, non-generic `typeof(...)` registrations, and supported `ServiceDescriptor` forms while continuing to skip unsafe refactors.
- **Shared Infrastructure**: Extracted reusable factory-invocation analysis so DI003 and DI015 stay aligned on method-group and helper-body traversal behavior.
- **Regression Coverage and Docs**: Added targeted DI003 tests for new analyzer/fixer paths and updated `README.md` plus `docs/RULES.md` to reflect the broadened factory support and safe-fix scope.

## [2.1.3] - 2026-03-16

### Changed

- **False Positive Reduction**: Narrowed `DI003` to true singleton captive-dependency cases, allowed `DI011` infrastructure patterns such as hosted services and endpoint filter factories, and taught `DI014` to recognize explicit owner disposal in `Dispose`/`DisposeAsync`.
- **Sharper Guidance**: Improved `DI007` so non-generic `GetService(typeof(...))` calls report the actual requested service type instead of degrading to `object`.
- **Safer Code Fixes**: Hardened `DI005`, `DI006`, and `DI008` so fixes are only offered when they can be applied without breaking code or generating uncompilable output.
- **Fixer Robustness**: Updated `DI003` and `DI009` code fixes to consume structured diagnostic metadata instead of parsing English diagnostic messages.
- **Documentation and Samples**: Synced `README.md`, `docs/RULES.md`, and the sample app with the hardened analyzer behavior and current warning output.

## [2.1.2] - 2026-03-16

### Changed

- **DI007 / DI011**: Lowered default severity from `Warning` to `Info` so broad service-location and `IServiceProvider`-injection guidance is still visible without warning-level noise by default.
- **Severity Guardrails**: Added a descriptor severity regression test to lock the intended default confidence split between runtime-failure rules and softer design-smell rules.

## [2.1.1] - 2026-02-06

### Changed

- **DI001 / DI014**: Reduced false positives by recognising explicit disposal inside the same lambda/local-function execution boundary while still warning when disposal is only deferred into a different boundary.
- **DI007**: Reduced false positives by continuing context analysis from lambdas up to containing methods (for example `Create*`/`Build*` factory methods), instead of stopping early.
- **DI008**: Reduced false positives with stricter symbol-based `AddTransient` detection and factory method-group detection for member-access expressions (for example `FactoryMethods.Create`).
- **Tests**: Added dedicated regression coverage for all fixed cases plus negative guardrail scenarios to ensure diagnostics still fire for deferred-lambda disposal patterns.

## [2.1.0] - 2026-02-06

### Added

- **DI016**: New analyser detecting `BuildServiceProvider()` misuse during service-registration composition (for example in `ConfigureServices`, `IServiceCollection` extension registration methods, and registration lambdas).

### Changed

- **Documentation/Samples**: Added DI016 rule documentation and sample coverage, including conservative false-positive guardrail notes.

## [2.0.0] - 2026-02-06

### Changed

- **DI015**: Expanded dependency resolution from direct constructor parameters to transitive constructor chains, including open-generic dependency closures.
- **DI015**: Factory registrations using `GetRequiredService`/`GetRequiredKeyedService` now resolve transitive dependency graphs and report missing leaf dependencies.
- **DI015**: Added conservative cycle handling (treat cycles as resolvable) and preserved framework/factory assumptions to keep false positives low.
- **Tests**: Added transitive DI015 regression coverage for constructor chains, factory-rooted chains, open-generic transitive resolution, and circular dependency paths.

## [1.13.0] - 2026-02-06

### Changed

- **DI002**: Reduced false positives by reporting scope-escape diagnostics only when the resolved service lifetime is known and scoped.
- **DI004**: Reduced false positives by reporting use-after-dispose diagnostics only when the resolved service lifetime is known and scoped/transient.
- **RegistrationCollector**: Improved fallback symbol handling to track DI registrations in unresolved/ambiguous invocation cases, including `Add*(typeof(...))` patterns.
- **Tests**: Added and updated regression coverage for unknown-lifetime suppression and `typeof`-based registration tracking in collector/rule/code-fix scenarios.

## [1.12.0] - 2026-02-05

### Added

- **Quality Gates**: Added CI coverage thresholds (line + branch) and release-time tag/version validation.
- **Community Files**: Added issue templates, pull request template, and `CODE_OF_CONDUCT.md`.
- **Documentation**: Added `docs/RULES.md` as the deep-dive rule reference and reshaped `README.md` into a quickstart-focused guide.

### Changed

- **RegistrationCollector**: Added all-registration tracking (`AllRegistrations`) so analyzers can inspect duplicate `Add*` registrations instead of only the last one.
- **Analyzer Coverage for Duplicates**: Updated DI003, DI009, DI010, DI011, DI013, and DI015 to analyze all discovered `Add*` registrations.
- **DI002**: Switched to symbol-based tracking and lifetime-aware filtering to reduce false positives (known singleton/transient registrations no longer trigger scope-escape diagnostics).
- **DI004**: Switched to symbol-based tracking and lifetime-aware filtering (known singleton services resolved from scope are excluded from use-after-dispose diagnostics).
- **DI001 / DI014**: Hardened explicit-dispose detection by requiring symbol-matched dispose calls that occur after creation, reducing false negatives from name-only matching.
- **Symbol Matching**: Improved service-provider and registration matching logic to prefer robust symbol/namespace checks while preserving compatibility with test stubs.
- **Tests**: Expanded regression coverage for variable shadowing, singleton scope-usage scenarios, duplicate registration analysis, and dispose-before-create edge cases.

## [1.11.0] - 2026-02-05

### Changed

- **DI015**: Added factory method-group analysis support (for registrations such as `AddSingleton<IMyService>(CreateMyService)`) and corresponding test coverage.
- **DI015**: Added factory-path analysis for `ActivatorUtilities.CreateInstance(...)` and introduced `.editorconfig` option `dotnet_code_quality.DI015.assume_framework_services_registered` for strict vs framework-assumed dependency checks.
- **DI015**: Hardened factory analysis by supporting named factory arguments, limiting method-body traversal to true method-group registrations, and applying `.editorconfig` framework-service assumptions per syntax tree.

## [1.10.0] - 2026-02-05

### Added

- **DI015**: New analyzer detecting registered services that depend on unregistered dependencies in constructor injection and `GetRequiredService` factory paths, including keyed and open-generic checks.
- **Samples**: Added `samples/DI015InAction`, a dedicated runnable sample showing broken and fixed DI015 registration patterns.

### Changed

- **DI015**: Reduced false positives for multi-constructor services by only reporting when no constructor is fully resolvable, and avoided duplicate reports for factory registrations that include implementation type metadata.

## [1.9.0] - 2026-02-05

### Fixed

- **DI001 / DI014**: Avoided false negatives where any enclosing `using` statement was incorrectly treated as disposing a scope/provider created elsewhere.
- **DI003**: Fixed non-generic service-resolution argument mapping for reduced extension methods (`GetService(typeof(...))`, `GetRequiredService(typeof(...))`), preventing missed captive-dependency reports.
- **Constructor Selection**: Added shared constructor-selection logic honoring `[ActivatorUtilitiesConstructor]` and preventing regressions with new analyzer tests.

### Changed

- **DI003**: Expanded non-generic and keyed resolution handling for factory delegates.
- **CI/Tooling**: Added `global.json`, updated CI/release workflows to install .NET 8 and .NET 10, and aligned test infrastructure/packages with current Roslyn testing defaults.
- **Tests**: Added targeted regression coverage across DI003, DI009, DI010, and DI011 for constructor-selection behavior and factory-resolution edge cases.

## [1.8.0] - 2025-12-11

### Fixed

- **DI012 (Conditional Registration Misuse)**: Keyed service registrations are now grouped by `(serviceType, key)` so different keys no longer trigger false duplicate/TryAdd diagnostics.

### Changed

- **RegistrationCollector**: Returns `null` when `IServiceCollection` is unavailable, avoiding unnecessary analysis in projects without DI references.
- **Dependencies**: Upgraded Roslyn to 5.0.0 and analyzer infrastructure packages to latest stable versions.
- **Tests**: Migrated from deprecated `*.Testing.XUnit` packages to core testing packages with `XUnitVerifier`, and bumped xUnit/Test SDK/Coverlet to current versions.

## [1.6.0] - 2025-11-26

### Changed

- **RegistrationCollector**: Improved robustness for parsing `ServiceDescriptor` arguments, correctly handling named arguments and integer-casted `ServiceLifetime` values.
- **DI014 Code Fix**: Enhanced to apply `await using` in asynchronous contexts and to preserve all leading/trailing trivia (comments, indentation) correctly.

## [1.5.0] - 2025-11-26

### Added

- **DI014 Code Fix**: Added code fix to automatically dispose root `IServiceProvider` instances.

### Changed

- **RegistrationCollector**: Enhanced to support services registered via `new ServiceDescriptor(...)` and `ServiceDescriptor.Describe(...)`. This improves detection accuracy for all analyzers relying on service registration data.

## [1.4.0] - 2025-11-26

### Added

- **DI013**: New analyzer detecting implementation type mismatches in `typeof` registrations (e.g. `AddSingleton(typeof(IService), typeof(BadImpl))`).
- **DI014**: New analyzer detecting undisposed root `IServiceProvider` instances created by `BuildServiceProvider()`.

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
