# Analyzer Health Report

**Date:** 2026-04-23 (critical analyzer hardening pass)
**Version:** 2.4.6+
**Test result:** 699/699 passing.
**Analyzers:** 18 (DI001-DI018)
**Code fix providers:** 10

## Summary

| ID | Rule | Sev | Analyzer Tests | Fixer Tests | Analyzer | Fixer | Status |
|----|------|-----|----------------|-------------|----------|-------|--------|
| DI001 | Scope Disposal | Warn | 23 | 11 | 8 | 8.5 | Hardened: nested scopes, explicit types, trivia, async delegates |
| DI002 | Scope Escape | Warn | 29 | 5 | 9 | 7 | Hardened: AddTodo action tested, duplicate TODO bug fixed, property/out/ref sinks |
| DI003 | Captive Dependency | Warn | 34 | 8 | 9 | 8 | Solid both sides, IEnumerable/GetServices captures |
| DI004 | Use After Dispose | Warn | 29 | -- | 9 | -- | Strong after boundary hardening, GetServices foreach |
| DI005 | Async Disposal | Warn | 17 | 9 | 8 | 8 | Narrow rule, well-tested |
| DI006 | Static Provider Cache | Warn | 11 | 14 | 8 | 9 | Simple rule, strong fixer |
| DI007 | Service Locator | Info | 22 | -- | 8 | -- | Informational, noise-hardened |
| DI008 | Disposable Transient | Warn | 19 | 13 | 8 | 9 | Solid coverage both sides |
| DI009 | Open Generic Mismatch | Warn | 22 | 15 | 9 | 9 | Refactored with RegistrationKind/LifetimeKind, defensive SimpleNameSyntax fix |
| DI010 | Constructor Over-Injection | Info | 24 | -- | 9.5 | -- | Strongest info-level rule |
| DI011 | Service Provider Injection | Info | 19 | -- | 9 | -- | Activation-constructor logic |
| DI012 | Conditional Registration | Info | 30 | 4 | 9 | 8 | Complex flow, ignored TryAdd fixer |
| DI013 | Implementation Mismatch | Error | 59 | 8 | 9.5 | 8 | Variance-aware assignability, named-argument extraction, broad assists |
| DI014 | Root Provider Not Disposed | Warn | 13 | 8 | 8 | 8.5 | Hardened: IsAsyncMethod bug fixed, async local fn, chained builders |
| DI015 | Unresolvable Dependency | Warn | 54 | 10 | 9 | 8 | One of strongest overall |
| DI016 | BuildServiceProvider Misuse | Warn | 19 | -- | 9 | -- | Builder-flow hardened |
| DI017 | Circular Dependency | Warn | 16 | -- | 9 | -- | Constructor selection fix, keyed cycle dedup fix, ServiceLookupKey |
| DI018 | Non-Instantiable Impl | Warn | 28 | -- | 9 | -- | Open-generic constructor checks |

`--` = no code fix exists for this rule.

**Aggregates:** Analyzer mean 8.8/10. Fixer mean 8.4/10.

## Scoring Methodology

| Score | Meaning |
|-------|---------|
| 10/10 | Strong implementation, strong tests, no obvious hardening need |
| 7-8/10 | Solid, but with precision or coverage debt |
| 5-6/10 | Important enough that a targeted hardening pass should be scheduled |

**Analyzer factors:** runtime impact if the analyzer misses or misreports, implementation complexity versus direct test coverage, whether recent hardening suggests stability.

**Fixer factors:** all analyzer factors plus correctness risk (a bad code fix is worse than a missed diagnostic — behavior-changing rewrites like `using` wrapping rank higher risk than trivia-only fixes like suppression comments), and coverage of the fixer's actual transformation surface (action variants, statement shapes, trivia/formatting).

## Per-Rule Details

### DI001 -- Scope Disposal (Warning)

**Analyzer: 8/10** | Tests: 23 | **Fixer: 8.5/10** | Fix Tests: 11

Operation-based tracking covers lambdas, fields, conditionals, nested scopes, and both `CreateScope()`/`CreateAsyncScope()` entry points. Fixer wraps in `using`/`await using` statement. Post-hardening: added tests for nested scopes, IServiceProvider entry, explicit type declarations, trivia preservation with leading comments, and async anonymous method delegates. Remaining debt: disposal-proof edge cases (conditional paths, exception flows).

### DI002 -- Scope Escape (Warning)

**Analyzer: 9/10** | Tests: 29 | **Fixer: 7/10** | Fix Tests: 5

Strong analyzer after executable-boundary hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, and out/ref parameter escape sinks. Remaining analyzer debt is conservative sink breadth, not entry coverage.

Post-hardening: both `DI002_AddTodo` and `DI002_Suppress` actions are now tested. Fixed a duplicate TODO bug where the fixer would insert a new TODO comment on every iterative application because it didn't check for existing TODOs. Added property assignment sink coverage for suppress action. The fixer does not inspect registration shapes (it operates on statement-level trivia). Low blast radius since the fixer only inserts suppression/comment trivia.

### DI003 -- Captive Dependency (Warning)

**Analyzer: 9/10** | Tests: 34 | **Fixer: 8/10** | Fix Tests: 8

Strong runtime-correctness rule. Instance-backed registrations are explicitly excluded from constructor analysis, direct + ServiceDescriptor regressions are covered, and collection-shaped captures through `IEnumerable<T>` / DI `GetServices<T>()` are detected without matching unrelated same-named APIs. Fixer adjusts service lifetimes with good coverage across injection patterns.

### DI004 -- Use After Dispose (Warning)

**Analyzer: 9/10** | Tests: 29

Strong after executable-boundary hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, and `GetServices<T>()` collections enumerated after disposal while keeping post-disposal reasoning inside the owning executable boundary. No code fix -- the correct resolution is context-dependent.

### DI005 -- Async Disposal (Warning)

**Analyzer: 8/10** | Tests: 17 | **Fixer: 8/10** | Fix Tests: 9

Narrow rule with a clear trigger: `CreateScope()` in async methods. Fixer replaces with `CreateAsyncScope()` and handles `await using` conversion. Async methods, lambdas, local functions, and `IServiceProvider.CreateScope()` covered.

### DI006 -- Static Provider Cache (Warning)

**Analyzer: 8/10** | Tests: 11 | **Fixer: 9/10** | Fix Tests: 14

Simple symbol-level rule with low ambiguity. Focused tests cover fields, properties, inherited provider types, and static classes. Fixer has more tests than the analyzer itself -- strong fix coverage.

### DI007 -- Service Locator Anti-Pattern (Info)

**Analyzer: 8/10** | Tests: 22

Informational by design. Good factory and lambda allowance handling. Correctly suppresses in factory registrations, middleware Invoke methods, and IServiceScopeFactory usage. No code fix -- service locator elimination requires architectural decisions.

### DI008 -- Disposable Transient (Warning)

**Analyzer: 8/10** | Tests: 19 | **Fixer: 9/10** | Fix Tests: 13

Solid coverage across generic registrations, `typeof`, `IDisposable`, and `IAsyncDisposable`. Fixer changes transient to scoped/singleton with strong shape coverage.

### DI009 -- Open Generic Lifetime Mismatch (Warning)

**Analyzer: 9/10** | Tests: 22 | **Fixer: 9/10** | Fix Tests: 15

Strong analyzer after constructor/collection hardening. Handles optional/default-value constructor selection, ambiguous equally-greedy constructor silence, and `IEnumerable<T>` captures. Code fix refactored with `RegistrationKind`/`LifetimeKind` enum extraction and comprehensive regression suite. Defensive fix: `CreateServiceLifetimeExpression` now emits `ServiceLifetime.X` member access (not bare identifier) for `SimpleNameSyntax`, preventing uncompilable output for const-backed lifetime identifiers.

### DI010 -- Constructor Over-Injection (Info)

**Analyzer: 9.5/10** | Tests: 24

Strongest info-level rule. Follows likely-activation constructors, covers conservative factory paths, uses symbol-accurate exclusions, and supports `.editorconfig` threshold overrides via `dotnet_code_quality.DI010.max_dependencies`. No code fix -- reducing constructor parameters requires design decisions.

### DI011 -- Service Provider Injection (Info)

**Analyzer: 9/10** | Tests: 19

Uses likely-activation-constructor logic with good allowance coverage for factory classes, middleware, and hosted services. Stays quiet for valid implementation-instance registrations. No code fix -- alternatives depend on architectural context.

### DI012 -- Conditional Registration Misuse (Info)

**Analyzer: 9/10** | Tests: 30 | **Fixer: 8/10** | Fix Tests: 4

Strong after flow/barrier hardening. Follows same-collection aliases, source-defined helper/local-function wrappers, distinct object-created collection flows, keyed variants, ServiceDescriptor shapes, and opaque ordering barriers. Supports both DI012 (TryAdd ignored) and DI012b (duplicate registration) diagnostics. The fixer removes standalone ignored `TryAdd*` registrations while leaving duplicate override cases manual.

### DI013 -- Implementation Type Mismatch (Error)

**Analyzer: 9.5/10** | Tests: 59 | **Fixer: 8/10** | Fix Tests: 8

Most comprehensive test file in the repo. Covers variance-aware closed generic assignability, open-generic projection checks, collector-fed registration shapes, named direct overload arguments, instance-backed mismatches, all registration patterns (typeof, generic forms), interfaces, base classes, and abstract classes. The only Error-severity rule -- critical that it has no false positives. The new fixer intentionally offers broad assists but keeps FixAll disabled because retargeting service/implementation types requires user judgment.

### DI014 -- Root Provider Not Disposed (Warning)

**Analyzer: 8/10** | Tests: 13 | **Fixer: 8.5/10** | Fix Tests: 8

Concrete lifetime rule with coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. Fixer wraps `BuildServiceProvider()` in `using`/`await using`. Post-hardening: fixed `IsAsyncMethod` bug where it checked `MethodDeclarationSyntax` ancestors before `LocalFunctionStatementSyntax`/`LambdaExpressionSyntax`, causing async local functions inside sync methods to get plain `using` instead of `await using`. Now walks ancestors in order and returns on the first callable encountered. Added tests for async local function inside sync method, multiple BuildServiceProvider calls, local function scopes, and chained fluent builder patterns.

### DI015 -- Unresolvable Dependency (Warning)

**Analyzer: 9/10** | Tests: 54 | **Fixer: 8/10** | Fix Tests: 10

Second most comprehensive test file. Broad support for keyed, factory, wrapper, open-generic, strict-mode, and implementation-instance scenarios. Full dependency resolution engine with reachability tracking. Fixer adds missing service registrations with solid coverage.

### DI016 -- BuildServiceProvider Misuse (Warning)

**Analyzer: 9/10** | Tests: 19

Strong after builder-flow hardening. Covers assignable `IServiceCollection` abstractions, same-boundary `.Services` aliases, helper methods that forward builder-style flows, and provider-factory guardrails. No code fix -- the correct alternative varies by context.

### DI017 -- Circular Dependency (Warning)

**Analyzer: 9/10** | Tests: 16

Significantly hardened. Cycle detection uses stable effective registrations with `ServiceLookupKey` for keyed service support. `knownNoCycle` memoization prevents exponential blowup at scale. Post-hardening fixes: `IsDirectlyResolvableConstructorParameter` no longer treats scalar types (`string`, `int`, etc.) as resolvable — a constructor requiring non-DI params cannot be selected by the runtime, preventing false-negative cycle suppression. `GetGlobalLookupKeyDisplayName` now includes the key's runtime type name in the canonical string to prevent dedup collisions between `int` key `1` and `string` key `"1"`. DI017 now stays silent for ambiguous equally greedy resolvable constructors instead of reporting a speculative cycle. No code fix -- breaking cycles requires architectural decisions.

### DI018 -- Non-Instantiable Implementation (Warning)

**Analyzer: 9/10** | Tests: 28

Open-generic constructor checks use the generic definition. Direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices. Correctly detects abstract classes, interfaces, static classes, and types with no accessible constructors.

## Code Fix Health

| Fixer | Fix Tests | Score | Risk Assessment |
|-------|-----------|-------|-----------------|
| DI001 (Scope Disposal) | 11 | 8.5 | Low -- behavior-changing rewrite now well-covered (nested, explicit types, trivia, async delegates) |
| DI002 (Scope Escape) | 5 | 7 | Low -- both actions tested, duplicate TODO bug fixed, property sink covered |
| DI003 (Captive Dependency) | 8 | 8 | Low -- solid shape coverage |
| DI005 (Async Scope) | 9 | 8 | Low -- narrow transformation, well-tested |
| DI006 (Static Provider Cache) | 14 | 9 | Low -- more tests than analyzer |
| DI008 (Disposable Transient) | 13 | 9 | Low -- strong shape coverage |
| DI009 (Open Generic Mismatch) | 15 | 9 | Low -- comprehensive refactor with defensive SimpleNameSyntax handling |
| DI012 (Ignored TryAdd) | 4 | 8 | Low -- narrow standalone-statement removal |
| DI013 (Implementation Mismatch) | 8 | 8 | Medium -- broad assists are symbol-backed, FixAll disabled |
| DI014 (Root Provider) | 8 | 8.5 | Low -- IsAsyncMethod bug fixed, async local fn + chained builders covered |
| DI015 (Unresolvable Dependency) | 10 | 8 | Low -- solid registration generation |

**Rules without code fixes:** DI004, DI007, DI010, DI011, DI016, DI017, DI018. These rules detect problems whose resolution requires architectural or context-dependent decisions.

## Infrastructure Health

| Component | Tests | Assessment |
|-----------|-------|------------|
| RegistrationCollector | 27 | Core engine, strong coverage |
| WellKnownTypes | 29 | Type symbol cache, comprehensive |
| PerformanceRegression | 4 | Baseline performance guards |
| SampleDiagnosticsVerifier | 10 | SARIF contract + freshness gates |
| RegistrationCollector (ServiceDescriptor) | 4 | ServiceDescriptor-specific collection |
| DiagnosticDescriptorSeverity | 1 (Theory, 18 cases) | Severity budget enforcement |
| CrossRuleInteraction | 8 | Multi-rule scenario validation |
| KeyedService | 9 | DI 8.0 keyed service support |

**CI quality gates:** 85% line coverage, 70% branch coverage (enforced, CI fails on regression). Coverage badge auto-committed. PR coverage comments via sticky-pull-request-comment.

## Aggregate Metrics

| Metric | Value |
|--------|-------|
| Total tests | 699 |
| Analyzer tests | 518 |
| Code fix tests | 93 |
| Infrastructure tests | 78 |
| Analyzer mean score | 8.8/10 |
| Fixer mean score | 8.4/10 |
| Rules at 9+ | 14/18 (78%) |
| Fixers at 8+ | 8/9 (89%) |
| Rules needing pass | 0 analyzers, 0 fixers |
| TODO/FIXME in source | 0 |
| Skipped tests | 0 |

## Bugs Found During Hardening

| PR | Bug | Severity | Rule |
|----|-----|----------|------|
| #32 | DI017 constructor selection treated scalar params as resolvable, suppressing real cycles | Medium | DI017 |
| #32 | DI009 fixer emitted bare identifier for SimpleNameSyntax lifetime, producing uncompilable code | Medium | DI009 |
| #32 | DI017 keyed cycle dedup collapsed keys with same string representation (int `1` vs string `"1"`) | Medium | DI017 |
| #33 | DI014 `IsAsyncMethod` checked method before nearest callable, wrong `using`/`await using` in nested async | Medium | DI014 |
| #34 | DI002 fixer didn't check for existing TODO, causing duplicate TODOs on iterative application | Low | DI002 |
| Current | DI017 reported speculative cycles for ambiguous equally greedy constructor sets | Medium | DI017 |
| Current | DI004 missed scoped collections from `GetServices<T>()` enumerated after scope disposal | Medium | DI004 |
| Current | DI003 missed captive scoped dependencies captured through `IEnumerable<T>` / `GetServices<T>()` | Medium | DI003 |

## Watchlist

| Item | Reason | Priority |
|------|--------|----------|
| DI002 fixer | Score 7/10 — lowest fixer score but low blast radius (trivia-only) | Low |
| DI001 analyzer | Disposal edge cases remain the most plausible false-positive source | Low |
| DI014 analyzer | Root-provider ownership edge cases | Low |

## Recommended Next Actions

1. **Consider code fixes for high-value analyzer-only rules** -- DI004 (use after dispose) remains warning-level and context-dependent
2. **Expand DI002 fixer sink coverage** -- lambda capture and deeper aliasing sinks remain context-sensitive
