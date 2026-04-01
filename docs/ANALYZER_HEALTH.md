# Analyzer Health Report

**Date:** 2026-04-01 (post-v2.4.3, includes uncommitted DI009/DI017 work)
**Version:** 2.4.3
**Test result:** 641/641 passing (up from 629)
**Analyzers:** 18 (DI001-DI018)
**Code fix providers:** 9

## Summary

| ID | Rule | Sev | Analyzer Tests | Fixer Tests | Analyzer | Fixer | Status |
|----|------|-----|----------------|-------------|----------|-------|--------|
| DI001 | Scope Disposal | Warn | 23 | 6 | 8 | 7 | Disposal edge cases remain |
| DI002 | Scope Escape | Warn | 27 | 2 | 9 | 5 | Fixer coverage critically weak |
| DI003 | Captive Dependency | Warn | 31 | 8 | 9 | 8 | Solid both sides |
| DI004 | Use After Dispose | Warn | 29 | -- | 9 | -- | Strong after boundary hardening |
| DI005 | Async Disposal | Warn | 17 | 9 | 8 | 8 | Narrow rule, well-tested |
| DI006 | Static Provider Cache | Warn | 11 | 14 | 8 | 9 | Simple rule, strong fixer |
| DI007 | Service Locator | Info | 22 | -- | 8 | -- | Informational, noise-hardened |
| DI008 | Disposable Transient | Warn | 19 | 13 | 8 | 9 | Solid coverage both sides |
| DI009 | Open Generic Mismatch | Warn | 22 | 14 | 9 | 9 | Major fix refactor adds confidence |
| DI010 | Constructor Over-Injection | Info | 24 | -- | 9.5 | -- | Strongest info-level rule |
| DI011 | Service Provider Injection | Info | 19 | -- | 9 | -- | Activation-constructor logic |
| DI012 | Conditional Registration | Info | 30 | -- | 9 | -- | Complex flow, recently hardened |
| DI013 | Implementation Mismatch | Error | 51 | -- | 9 | -- | Most comprehensive tests |
| DI014 | Root Provider Not Disposed | Warn | 13 | 4 | 8 | 7 | Limited fixer coverage |
| DI015 | Unresolvable Dependency | Warn | 53 | 10 | 9 | 8 | One of strongest overall |
| DI016 | BuildServiceProvider Misuse | Warn | 18 | -- | 9 | -- | Builder-flow hardened |
| DI017 | Circular Dependency | Warn | 13 | -- | 8.5 | -- | ServiceLookupKey migration WIP |
| DI018 | Non-Instantiable Impl | Warn | 28 | -- | 9 | -- | Open-generic constructor checks |

`--` = no code fix exists for this rule.

**Aggregates:** Analyzer mean 8.7/10. Fixer mean 7.8/10.

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

**Analyzer: 8/10** | Tests: 23 | **Fixer: 7/10** | Fix Tests: 6

Operation-based tracking covers lambdas, fields, conditionals, nested scopes, and both `CreateScope()`/`CreateAsyncScope()` entry points. Fixer wraps in `using` statement. Remaining debt: disposal-proof edge cases (conditional paths, exception flows) are the most plausible source of future false positives.

### DI002 -- Scope Escape (Warning)

**Analyzer: 9/10** | Tests: 27 | **Fixer: 5/10** | Fix Tests: 2

Strong analyzer after executable-boundary hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, and predeclared scopes. Remaining analyzer debt is conservative sink breadth, not entry coverage.

**Fixer is the weakest in the repo.** Only 2 code fix tests, and both only exercise the `DI002_Suppress` action -- the `DI002_AddTodo` action (TODO comment insertion) is completely untested. The two tests also cover only `return` and field-assignment sink forms. The fixer does not inspect registration shapes (it operates on statement-level trivia), so the actual uncovered surface is action coverage, statement-shape coverage, and trivia/formatting behavior. Low blast radius since the fixer only inserts suppression/comment trivia (no behavior-changing rewrites).

### DI003 -- Captive Dependency (Warning)

**Analyzer: 9/10** | Tests: 31 | **Fixer: 8/10** | Fix Tests: 8

Strong runtime-correctness rule. Instance-backed registrations are explicitly excluded from constructor analysis, and direct + ServiceDescriptor regressions are covered. Fixer adjusts service lifetimes with good coverage across injection patterns.

### DI004 -- Use After Dispose (Warning)

**Analyzer: 9/10** | Tests: 29

Strong after executable-boundary hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, and predeclared scopes while keeping post-disposal reasoning inside the owning executable boundary. No code fix -- the correct resolution is context-dependent.

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

**Analyzer: 9/10** | Tests: 22 | **Fixer: 9/10** | Fix Tests: 14

Strong analyzer after constructor/collection hardening. Handles optional/default-value constructor selection, ambiguous equally-greedy constructor silence, and `IEnumerable<T>` captures. Major code fix refactor in progress (+542 implementation lines) with `RegistrationKind`/`LifetimeKind` enum extraction and comprehensive regression suite (+641 uncommitted test lines). Fixer score reflects the uncommitted state.

### DI010 -- Constructor Over-Injection (Info)

**Analyzer: 9.5/10** | Tests: 24

Strongest info-level rule. Follows likely-activation constructors, covers conservative factory paths, uses symbol-accurate exclusions, and supports `.editorconfig` threshold overrides via `dotnet_code_quality.DI010.max_dependencies`. No code fix -- reducing constructor parameters requires design decisions.

### DI011 -- Service Provider Injection (Info)

**Analyzer: 9/10** | Tests: 19

Uses likely-activation-constructor logic with good allowance coverage for factory classes, middleware, and hosted services. Stays quiet for valid implementation-instance registrations. No code fix -- alternatives depend on architectural context.

### DI012 -- Conditional Registration Misuse (Info)

**Analyzer: 9/10** | Tests: 30

Strong after flow/barrier hardening. Follows same-collection aliases, source-defined helper/local-function wrappers, distinct object-created collection flows, keyed variants, ServiceDescriptor shapes, and opaque ordering barriers. Supports both DI012 (TryAdd ignored) and DI012b (duplicate registration) diagnostics.

### DI013 -- Implementation Type Mismatch (Error)

**Analyzer: 9/10** | Tests: 51

Most comprehensive test file in the repo. Covers open-generic projection checks, collector-fed registration shapes, instance-backed mismatches, all registration patterns (typeof, generic forms), interfaces, base classes, and abstract classes. The only Error-severity rule -- critical that it has no false positives.

### DI014 -- Root Provider Not Disposed (Warning)

**Analyzer: 8/10** | Tests: 13 | **Fixer: 7/10** | Fix Tests: 4

Concrete lifetime rule with coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. Fixer wraps `BuildServiceProvider()` in `using`. Limited fixer test coverage -- only 4 tests for a disposal-related fix.

### DI015 -- Unresolvable Dependency (Warning)

**Analyzer: 9/10** | Tests: 53 | **Fixer: 8/10** | Fix Tests: 10

Second most comprehensive test file. Broad support for keyed, factory, wrapper, open-generic, and implementation-instance scenarios. Full dependency resolution engine with reachability tracking. Fixer adds missing service registrations with solid coverage.

### DI016 -- BuildServiceProvider Misuse (Warning)

**Analyzer: 9/10** | Tests: 18

Strong after builder-flow hardening. Covers assignable `IServiceCollection` abstractions, same-boundary `.Services` aliases, helper methods that forward builder-style flows, and provider-factory guardrails. No code fix -- the correct alternative varies by context.

### DI017 -- Circular Dependency (Warning)

**Analyzer: 8.5/10** | Tests: 13

Much healthier after stable effective-registration ordering and mixed instance-plus-constructed graph coverage. Uncommitted work migrates cycle path tracking to `ServiceLookupKey` for keyed service support (+67 implementation lines, +52 test lines). `knownNoCycle` memoization prevents exponential blowup at scale. No code fix -- breaking cycles requires architectural decisions.

### DI018 -- Non-Instantiable Implementation (Warning)

**Analyzer: 9/10** | Tests: 28

Open-generic constructor checks use the generic definition. Direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices. Correctly detects abstract classes, interfaces, static classes, and types with no accessible constructors.

## Code Fix Health

| Fixer | Fix Tests | Score | Risk Assessment |
|-------|-----------|-------|-----------------|
| DI001 (Scope Disposal) | 6 | 7 | **High** -- behavior-changing `using`/`await using` rewrite with thin test coverage |
| DI002 (Scope Escape) | 2 | **5** | Moderate -- weakest test count but trivia-only fix (low blast radius) |
| DI003 (Captive Dependency) | 8 | 8 | Low -- solid shape coverage |
| DI005 (Async Scope) | 9 | 8 | Low -- narrow transformation, well-tested |
| DI006 (Static Provider Cache) | 14 | 9 | Low -- more tests than analyzer |
| DI008 (Disposable Transient) | 13 | 9 | Low -- strong shape coverage |
| DI009 (Open Generic Mismatch) | 14 | 9 | Low -- major refactor with comprehensive regression suite |
| DI014 (Root Provider) | 4 | 7 | **High** -- behavior-changing `using` rewrite with only 4 tests |
| DI015 (Unresolvable Dependency) | 10 | 8 | Low -- solid registration generation |

**Rules without code fixes:** DI004, DI007, DI010, DI011, DI012, DI013, DI016, DI017, DI018. These rules detect problems whose resolution requires architectural or context-dependent decisions.

## Infrastructure Health

| Component | Tests | Assessment |
|-----------|-------|------------|
| RegistrationCollector | 27 | Core engine, strong coverage |
| WellKnownTypes | 29 | Type symbol cache, comprehensive |
| PerformanceRegression | 3 | Baseline performance guards |
| SampleDiagnosticsVerifier | 10 | SARIF contract + freshness gates |
| RegistrationCollector (ServiceDescriptor) | 4 | ServiceDescriptor-specific collection |
| DiagnosticDescriptorSeverity | 1 (Theory, 18 cases) | Severity budget enforcement |
| CrossRuleInteraction | 8 | Multi-rule scenario validation |
| KeyedService | 9 | DI 8.0 keyed service support |

**CI quality gates:** 85% line coverage, 70% branch coverage (enforced, CI fails on regression). Coverage badge auto-committed. PR coverage comments via sticky-pull-request-comment.

## Aggregate Metrics

| Metric | Value |
|--------|-------|
| Total tests | 641 |
| Analyzer tests | 489 |
| Code fix tests | 80 |
| Infrastructure tests | 73 |
| Analyzer mean score | 8.7/10 |
| Fixer mean score | 7.8/10 |
| Rules at 9+ | 13/18 (72%) |
| Rules needing pass | 0 analyzers, 3 fixers (DI001, DI002, DI014) |
| TODO/FIXME in source | 0 |
| Skipped tests | 0 |

## Watchlist

| Item | Reason | Priority |
|------|--------|----------|
| DI009/DI017 uncommitted | 1,236 lines of improvements sitting in working tree | **High** |
| DI001 fixer | 6 tests for a behavior-changing rewrite (`using`/`await using` wrapping) -- highest correctness risk among fixers | **High** |
| DI014 fixer | 4 tests for a behavior-changing rewrite (`using` wrapping) -- high correctness risk, thin coverage | **High** |
| DI002 fixer | Only 2 tests, `DI002_AddTodo` action entirely untested -- but trivia-only fix has low blast radius | Medium |
| DI001 analyzer | Disposal edge cases are the most plausible false-positive source | Low |
| DI014 analyzer | Root-provider ownership edge cases | Low |
| DI017 analyzer | Graph-shape precision and keyed-path breadth | Low |

## Recommended Next Actions

1. **Merge DI009/DI017 uncommitted work** -- 1,236 lines of improvements and +12 tests in working tree
2. **Harden DI001 fixer** -- behavior-changing rewrite (`using`/`await using`) with only 6 tests; add scenarios for nested scopes, conditional disposal, exception flows, and complex statement shapes
3. **Harden DI014 fixer** -- behavior-changing rewrite (`using` wrapping) with only 4 tests; add coverage for field assignment, property returns, and shadowed provider patterns
4. **Harden DI002 fixer** -- score 5/10, only 2 tests; add coverage for the untested `DI002_AddTodo` action, additional sink forms (property assignment, lambda capture, out parameter), and trivia/formatting edge cases
5. **Consider code fixes for high-value analyzer-only rules** -- DI004 (use after dispose) and DI013 (type mismatch) have high severity with no automated fix
