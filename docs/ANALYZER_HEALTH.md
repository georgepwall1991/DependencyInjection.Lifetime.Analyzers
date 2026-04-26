# Analyzer Health Report

**Date:** 2026-04-26 (DI001 conditional ownership pass)
**Version:** 2.8.5
**Test result:** 782/782 passing.
**Analyzers:** 19 (DI001-DI019)
**Code fix providers:** 12

## Summary

| ID | Rule | Sev | Analyzer Tests | Fixer Tests | Analyzer | Fixer | Status |
|----|------|-----|----------------|-------------|----------|-------|--------|
| DI001 | Scope Disposal | Warn | 31 | 11 | 9.5 | 8.5 | Hardened: conditional ownership proofs, non-null disposal guards, reassignment and loop guardrails |
| DI002 | Scope Escape | Warn | 33 | 6 | 9.5 | 8 | Hardened: delegate-capture escapes, aliases, property/out/ref sinks |
| DI003 | Captive Dependency | Warn | 34 | 8 | 9 | 8 | Solid both sides, IEnumerable/GetServices captures |
| DI004 | Use After Dispose | Warn | 43 | 8 | 10 | 8.5 | Fixer now gated to the owning using scope and invocation-style uses |
| DI005 | Async Disposal | Warn | 17 | 9 | 8 | 8 | Narrow rule, well-tested |
| DI006 | Static Provider Cache | Warn | 11 | 14 | 8 | 9 | Simple rule, strong fixer |
| DI007 | Service Locator | Info | 22 | -- | 8 | -- | Informational, noise-hardened |
| DI008 | Disposable Transient | Warn | 19 | 13 | 8 | 9 | Solid coverage both sides |
| DI009 | Open Generic Mismatch | Warn | 22 | 15 | 9 | 9 | Refactored with RegistrationKind/LifetimeKind, defensive SimpleNameSyntax fix |
| DI010 | Constructor Over-Injection | Info | 24 | -- | 9.5 | -- | Strongest info-level rule |
| DI011 | Service Provider Injection | Info | 19 | -- | 9 | -- | Activation-constructor logic |
| DI012 | Conditional Registration | Info | 30 | 4 | 9 | 8 | Complex flow, ignored TryAdd fixer |
| DI013 | Implementation Mismatch | Error | 59 | 8 | 9.5 | 8 | Variance-aware assignability, named-argument extraction, broad assists |
| DI014 | Root Provider Not Disposed | Warn | 18 | 9 | 9 | 9 | Hardened: reliable local disposal proofs, reassignment leaks, safe manual-fix gating |
| DI015 | Unresolvable Dependency | Warn | 54 | 10 | 9 | 8 | One of strongest overall |
| DI016 | BuildServiceProvider Misuse | Warn | 19 | -- | 9 | -- | Builder-flow hardened |
| DI017 | Circular Dependency | Warn | 16 | -- | 9 | -- | Constructor selection fix, keyed cycle dedup fix, ServiceLookupKey |
| DI018 | Non-Instantiable Impl | Warn | 28 | -- | 9 | -- | Open-generic constructor checks |
| DI019 | Root Scoped Resolution | Warn | 15 | -- | 8.5 | -- | Root/scoped provider classification, transitive scoped graph |

`--` = no code fix exists for this rule.

**Aggregates:** Analyzer mean 8.9/10. Fixer mean 8.5/10.

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

**Analyzer: 9.5/10** | Tests: 31 | **Fixer: 8.5/10** | Fix Tests: 11

Operation-based tracking covers lambdas, fields, conditionals, nested scopes, and both `CreateScope()`/`CreateAsyncScope()` entry points. Explicit-disposal proofs reject `Dispose()` / `DisposeAsync()` calls that are reachable only through unsafe conditional branches, switch sections, loops, or catch blocks, while continuing to accept straight-line and `finally` disposal patterns. Conditional ownership is now modeled for predeclared nullable scope locals assigned inside `if` / `else` or `try` blocks and disposed later through conditional access, exact non-null guards, or `finally` cleanup. Reassignment and repeated loop-creation guardrails keep the analyzer from treating one later dispose call as proof for a lost or repeatedly overwritten scope. Fixer wraps in `using`/`await using` statement.

### DI002 -- Scope Escape (Warning)

**Analyzer: 9.5/10** | Tests: 33 | **Fixer: 8/10** | Fix Tests: 6

Strong analyzer after executable-boundary and delegate-escape hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, out/ref parameter escape sinks, and high-confidence delegates that capture scoped services before escaping via returns, fields, properties, or ref/out parameters. Reassignment guardrails keep stale delegate captures quiet before later escape sinks.

The fixer intentionally offers pragma suppression only, because moving scoped services across ownership boundaries is context-dependent. Suppression coverage now spans direct returns, field/property assignments, alias returns, ref/out parameter escapes, and captured-delegate escapes while keeping the transformation trivia-only and low blast radius.

### DI003 -- Captive Dependency (Warning)

**Analyzer: 9/10** | Tests: 34 | **Fixer: 8/10** | Fix Tests: 8

Strong runtime-correctness rule. Instance-backed registrations are explicitly excluded from constructor analysis, direct + ServiceDescriptor regressions are covered, and collection-shaped captures through `IEnumerable<T>` / DI `GetServices<T>()` are detected without matching unrelated same-named APIs. Fixer adjusts service lifetimes with good coverage across injection patterns.

### DI004 -- Use After Dispose (Warning)

**Analyzer: 10/10** | Tests: 43 | **Fixer: 8.5/10** | Fix Tests: 8

Strong after explicit-disposal and post-boundary state hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, explicit `Dispose()` / `DisposeAsync()`, conditional/member uses, deconstruction, `await foreach`, keyed constants, deferred delegate capture, and mixed `GetServices<T>()` collections while keeping uncertain lifetimes silent. Fixer now moves only simple immediate invocation-style uses whose diagnostic local was assigned inside the immediately preceding `using` block, and it offers pragma suppression for context-dependent cases. Guardrails cover unrelated adjacent scopes, escape assignments, nested-boundary assignments, invocation-argument diagnostics, comments, return-only suppression, and `await using` blocks.

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

**Analyzer: 9/10** | Tests: 18 | **Fixer: 9/10** | Fix Tests: 9

Concrete lifetime rule with coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. Fixer wraps `BuildServiceProvider()` in `using`/`await using`. Post-hardening: fixed `IsAsyncMethod` bug where it checked `MethodDeclarationSyntax` ancestors before `LocalFunctionStatementSyntax`/`LambdaExpressionSyntax`, causing async local functions inside sync methods to get plain `using` instead of `await using`. Now walks ancestors in order and returns on the first callable encountered. Added tests for async local function inside sync method, multiple BuildServiceProvider calls, local function scopes, and chained fluent builder patterns.

Latest pass tightened local ownership proofs: conditional and catch-only `Dispose()` calls no longer suppress DI014, provider reassignments before disposal report the leaked first provider, and predeclared providers assigned inside `try` blocks are recognized when a `finally` block disposes them. The fixer now skips diagnostics that already have manual disposal code, keeping partial-disposal repairs explicit instead of layering `using` on top of an unsafe flow.

### DI015 -- Unresolvable Dependency (Warning)

**Analyzer: 9.5/10** | Tests: 62 | **Fixer: 8.8/10** | Fix Tests: 12

One of the strongest overall rules. Broad support for keyed, inherited-key, `AnyKey`, factory, wrapper, open-generic, strict-mode, implementation-instance, and definite registration-mutation scenarios. Full dependency resolution engine with reachability tracking. Fixer adds safe unkeyed and keyed concrete self-bindings, including direct factory-rooted cases.

### DI016 -- BuildServiceProvider Misuse (Warning)

**Analyzer: 9/10** | Tests: 19

Strong after builder-flow hardening. Covers assignable `IServiceCollection` abstractions, same-boundary `.Services` aliases, helper methods that forward builder-style flows, and provider-factory guardrails. No code fix -- the correct alternative varies by context.

### DI017 -- Circular Dependency (Warning)

**Analyzer: 9.5/10** | Tests: 26

Major hardening pass applied. Cycle detection now uses reachable, flow-aware effective registrations, honors `TryAdd` plus `RemoveAll` / `Replace` removal, analyzes high-confidence factory requests, inherited keyed dependencies, open-generic activation, and registered `IEnumerable<T>` elements, and keeps ambiguous constructors, dynamic keys, opaque factories, unrelated service collections, and uninvoked wrappers silent. `knownNoCycle` memoization remains in place for scale. No code fix -- breaking cycles requires architectural decisions.

### DI018 -- Non-Instantiable Implementation (Warning)

**Analyzer: 9/10** | Tests: 28

Open-generic constructor checks use the generic definition. Direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices. Correctly detects abstract classes, interfaces, static classes, and types with no accessible constructors.

### DI019 -- Root Scoped Resolution (Warning)

**Analyzer: 8.5/10** | Tests: 15

Detects scoped services, and service graphs that reach scoped services, resolved from a root provider. Covers `app.Services`, `host.Services`, `BuildServiceProvider()`, local root-provider variables, singleton implementations, hosted services, `GetServices<T>()`, and keyed resolutions. Stays silent for scoped providers from `CreateScope()`/`CreateAsyncScope()`, `HttpContext.RequestServices`, DI factory lambdas covered by DI003, and dynamic service-type requests.

## Code Fix Health

| Fixer | Fix Tests | Score | Risk Assessment |
|-------|-----------|-------|-----------------|
| DI001 (Scope Disposal) | 11 | 8.5 | Low -- behavior-changing rewrite now well-covered (nested, explicit types, trivia, async delegates) |
| DI002 (Scope Escape) | 6 | 8 | Low -- pragma-only suppression now covers direct, alias, ref/out, property, and captured-delegate diagnostics |
| DI003 (Captive Dependency) | 8 | 8 | Low -- solid shape coverage |
| DI004 (Use After Dispose) | 8 | 8.5 | Low -- move fix is now gated to owning-scope immediate invocations, with unsafe escape/adjacent-scope shapes suppressed |
| DI005 (Async Scope) | 9 | 8 | Low -- narrow transformation, well-tested |
| DI006 (Static Provider Cache) | 14 | 9 | Low -- more tests than analyzer |
| DI008 (Disposable Transient) | 13 | 9 | Low -- strong shape coverage |
| DI009 (Open Generic Mismatch) | 15 | 9 | Low -- comprehensive refactor with defensive SimpleNameSyntax handling |
| DI012 (Ignored TryAdd) | 4 | 8 | Low -- narrow standalone-statement removal |
| DI013 (Implementation Mismatch) | 8 | 8 | Medium -- broad assists are symbol-backed, FixAll disabled |
| DI014 (Root Provider) | 9 | 9 | Low -- reliable local disposal proofs, reassignment guardrails, async local fn + chained builders covered |
| DI015 (Unresolvable Dependency) | 12 | 8.8 | Low -- keyed and factory self-binding generation is still tightly gated |

**Rules without code fixes:** DI007, DI010, DI011, DI016, DI017, DI018, DI019. These rules detect problems whose resolution requires architectural or context-dependent decisions.

## Infrastructure Health

| Component | Tests | Assessment |
|-----------|-------|------------|
| RegistrationCollector | 27 | Core engine, strong coverage |
| WellKnownTypes | 29 | Type symbol cache, comprehensive |
| PerformanceRegression | 4 | Baseline performance guards |
| SampleDiagnosticsVerifier | 10 | SARIF contract + freshness gates |
| RegistrationCollector (ServiceDescriptor) | 4 | ServiceDescriptor-specific collection |
| DiagnosticDescriptorSeverity | 1 (Theory, 19 cases) | Severity budget enforcement |
| CrossRuleInteraction | 8 | Multi-rule scenario validation |
| KeyedService | 9 | DI 8.0 keyed service support |

**CI quality gates:** 85% line coverage, 70% branch coverage (enforced, CI fails on regression). Coverage badge auto-committed. PR coverage comments via sticky-pull-request-comment.

## Aggregate Metrics

| Metric | Value |
|--------|-------|
| Total tests | 782 |
| Analyzer tests | 602 |
| Code fix tests | 102 |
| Infrastructure tests | 78 |
| Analyzer mean score | 8.9/10 |
| Fixer mean score | 8.5/10 |
| Rules at 9+ | 14/19 (74%) |
| Fixers at 8+ | 12/12 (100%) |
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
| Current | DI003 missed captive scoped dependencies captured through `IEnumerable<T>` / `GetServices<T>()` | Medium | DI003 |
| Current | DI004 move fix could be offered for an unrelated immediately preceding `using` block or for an escape assignment | Medium | DI004 |
| Current | DI002 missed scoped services captured by delegates that escaped through later return, field, property, or ref/out sinks | Medium | DI002 |
| Current | DI001 accepted conditional or catch-only dispose calls as disposal proofs, suppressing real scope leaks | Medium | DI001 |
| Current | DI014 accepted conditional, catch-only, or post-reassignment root-provider disposal as reliable disposal proof | Medium | DI014 |
| Current | DI001 treated conditionally assigned nullable scope locals as leaked when later conditional-access or non-null-guarded cleanup closed ownership | Low | DI001 |

## Watchlist

| Item | Reason | Priority |
|------|--------|----------|
| Info-rule docs | Keep remediation guidance polished now that warning-level ownership edges are hardened | Low |

## Recommended Next Actions

1. **Refresh low-priority info-rule docs** -- keep Info-rule remediation guidance polished without widening diagnostic scope
2. **Revisit DI008/DI005 narrow edges opportunistically** -- both are stable warning rules; future passes should be driven by concrete false-positive or false-negative reports
