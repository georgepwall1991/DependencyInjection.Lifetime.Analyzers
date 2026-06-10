# Analyzer Health Report

**Date:** 2026-06-10 (honesty re-audit: scores re-verified against source, four rules re-scored down, stale counts corrected, importance ranking added)
**Version:** 2.10.0
**Test result:** 1423/1423 passing (verified this date, 0 skipped).
**Analyzers:** 21 classes / 22 rule IDs (DI001-DI022; DI021 and DI022 share one analyzer)
**Code fix providers:** 14

## Summary

| ID | Rule | Sev | Analyzer Tests | Fixer Tests | Analyzer | Fixer | Status |
|----|------|-----|----------------|-------------|----------|-------|--------|
| DI001 | Scope Disposal | Warn | 59 | 15 | 9.6 | 8.8 | Hardened: conditional ownership proofs, branch-exit disposal proof, non-null disposal guards, reassignment and unsafe await-using guardrails, conditional-access creation (`_provider?.CreateScope()`) disposal/return proofs, and await-using suppression for conditional-access creations |
| DI002 | Scope Escape | Warn | 57 | 8 | 8.8 | 8.5 | 2.10.3 closed the two highest-frequency audit gaps: field/property-collection mutation (`_cache.Add`, `Insert`/`Enqueue`/`Push`/`TryAdd`, direct-resolution arguments) and event subscription (method-group and captured-delegate handlers on owners that outlive the scope); indexer assignment was already covered via the indexer property symbol and is now pinned. Remaining audit debt: tuple/anonymous-object composite returns (FN direction) |
| DI003 | Captive Dependency | Warn | 120 | 13 | 9.8 | 9 | Hardened: known framework scoped lifetimes, stable local delegate factories, EF factory/pooled contexts, fixer shape coverage, and conditional-access (`services?.AddSingleton<...>()`) lifetime rewrite |
| DI004 | Use After Dispose | Warn | 54 | 10 | 8.9 | 8.8 | 2.10.5 fixed the audit's branch-precision gap: uses in branches mutually exclusive with an explicit `Dispose()` (opposite if/else arm, different switch section) no longer report, while shared-path uses after a conditional dispose still do. Remaining cross-boundary limit: field-stored scopes are invisible (shared design limit) |
| DI005 | Async Disposal | Warn | 24 | 11 | 9.2 | 8.8 | Hardened: conditional-access receivers (`provider?.CreateScope()`) participate in detection alongside the existing top-level async, nested async, and direct-resource fixer coverage |
| DI006 | Static Provider Cache | Warn | 28 | 16 | 9.5 | 9.2 | Hardened: nested wrappers, provider dictionaries, holder detection, and static-context fixer guardrails |
| DI007 | Service Locator | Info | 38 | -- | 9.6 | -- | Hardened: exact hosting/options/middleware/factory method checks and bounded provider-factory delegate suppression |
| DI008 | Disposable Transient | Warn | 44 | 18 | 9.5 | 9.3 | Hardened: `ServiceDescriptor`/`TryAdd*` shapes, open generics, descriptor collections, factory guardrails, allowlist option, and descriptor lifetime fixes |
| DI009 | Open Generic Mismatch | Warn | 27 | 15 | 9.2 | 9 | Hardened: known scoped framework services (IOptionsSnapshot<T>) participate in open-generic singleton captive analysis, explicit closed user registrations override the classifier, IEnumerable<T> captures take the worst lifetime across user and framework registrations, and IOptions<T>/IOptionsMonitor<T> stay quiet |
| DI010 | Constructor Over-Injection | Info | 29 | -- | 9.7 | -- | Strongest info-level rule, public-constructor and factory-return precise |
| DI011 | Service Provider Injection | Info | 28 | -- | 9.5 | -- | Activation-constructor logic with middleware, factory-shape, singleton scope-factory, and non-public-constructor guardrails |
| DI012 | Conditional Registration | Info | 32 | 8 | 9.2 | 9 | Complex flow, ignored keyed/non-keyed TryAdd fixer covers conditional-access (`services?.TryAdd*(...)`), embedded/top-level statement guardrails, and EF helper TryAdd-style preservation |
| DI013 | Implementation Mismatch | Error | 59 | 14 | 9.5 | 9 | Variance-aware assignability, named-argument extraction, broad assists with instance retargeting/removal, embedded/top-level rewrite guardrails, and conditional-access (`services?.AddSingleton(...)`) standalone-removal support |
| DI014 | Root Provider Not Disposed | Warn | 84 | 12 | 9.3 | 9 | Hardened: reliable local disposal proofs, branch ownership, direct/nested branch exits, explicit/async/finally cleanup, loop reassignment leaks, nearest-callable fixer guardrails, conditional-access creation (`services?.BuildServiceProvider()`) disposal/return proofs with using/await-using fixer support, and wrapped-result proofs (parenthesized, provable upcast, null-forgiving) that reject user-defined conversions and unproven downcasts |
| DI015 | Unresolvable Dependency | Warn | 121 | 16 | 9.8 | 9.2 | One of strongest overall, EF factory/pooled-registration aware, FixAll-disabled lock covered, conditional-access (`services?.AddXxx(...)`) self-bindings mirror the trigger shape |
| DI016 | BuildServiceProvider Misuse | Warn | 27 | -- | 9.4 | -- | Hardened: conditional-access receivers (`builder.Services?.BuildServiceProvider()`, `builder?.Services.BuildServiceProvider()`, chained `builder?.Services?.BuildServiceProvider()`) participate in detection alongside null-forgiving / cast unwrap and builder-flow precision |
| DI017 | Circular Dependency | Warn | 28 | -- | 8.8 | -- | Re-scored down (was 9.5): the algorithm is sophisticated (flow-aware registrations, mutations, keyed deps, memoization) but 28 tests over 1,319 lines is thin — `Replace` mutation handled in code yet untested, only 2 keyed tests, `Lazy<T>` parameters never modeled |
| DI018 | Non-Instantiable Impl | Warn | 34 | -- | 9.2 | -- | Hardened: delegate-type registrations without a factory are reported (including the one-Type `AddSingleton(typeof(T))` self-binding overload, guarded to avoid the two-Type overload with a variable-typed implementation argument); the default container cannot populate (object, IntPtr) delegate constructors |
| DI019 | Root Scoped Resolution | Warn | 58 | 16 | 9.7 | 9 | Root/scoped provider classification, known and nullable-root provider surface filtering, conditional-access receiver classification (`host?.Services...`, chained `app?.Services?...`, and `var sp = app?.Services;` aliases report; `httpContext?.RequestServices...` / `scope?.ServiceProvider...` stay quiet), transitive scoped graph, known framework scoped services, and full resolution-path messages (`A -> B -> C`) reconstructed from the dependency walk; scope-wrapping code fix gated against scoped-service escape (assignment/argument), type-receiver static calls, conditional-access receivers, and async-aware (`CreateAsyncScope`/`await using`) |
| DI020 | Middleware Scoped Service | Warn | 26 | -- | 9.0 | -- | 2.10.4 closed every audit gap: typeof-overload (positive + explicit-arg-suppressed), keyed dependencies both directions, endpoint-route builder, extension-method receiver path, conditional-access registration (new detection), and fixed the explicit-argument false positive (filled parameters were still reported) |
| DI021 | Concurrent Handler Shared State | Warn | 102 | 16 | 8.8 | 8.5 | Non-thread-safe services (DbContext + derived, DbConnection/DbCommand/DbTransaction/DbDataReader + interfaces, IDbContextTransaction, HttpContext) captured via field/closure/enclosing-parameter into concurrently-invoked handlers (ServiceBus processors, EventProcessorClient, RabbitMQ consumers across the v6/v7 event drift, both Timer types, Parallel.*), including captured-scope in-handler resolution; serialization-guard suppressions (lock, SemaphoreSlim, Interlocked, timer re-arm, async-lock idiom) and proven-sequential escapes ship in v1 |
| DI022 | Config-Gated Handler Capture | Info | (shared) | (shared) | 8.8 | 8.5 | New rule (same analyzer as DI021): the config-gated tier — sink concurrency knob unprovable at compile time (ServiceBusProcessor MaxConcurrentCalls); conditional wording, upgrades to DI021 when the knob is proven > 1, silent when proven 1 |

`--` = no code fix exists for this rule.

**Aggregates:** Analyzer mean 9.3/10. Fixer mean 8.9/10.

## Scoring Methodology

| Score | Meaning |
|-------|---------|
| 10/10 | Strong implementation, strong tests, no obvious hardening need |
| 7-8/10 | Solid, but with precision or coverage debt |
| 5-6/10 | Important enough that a targeted hardening pass should be scheduled |

**Analyzer factors:** runtime impact if the analyzer misses or misreports, implementation complexity versus direct test coverage, whether recent hardening suggests stability.

**Fixer factors:** all analyzer factors plus correctness risk (a bad code fix is worse than a missed diagnostic — behavior-changing rewrites like `using` wrapping rank higher risk than trivia-only fixes like suppression comments), and coverage of the fixer's actual transformation surface (action variants, statement shapes, trivia/formatting).

**Honesty rules (2026-06-10 re-audit):** No rule scores 10 while a same-boundary, syntactically-reachable miss exists — 10 means "no obvious hardening need," and that claim must survive a source read, not just a green test run. Test density counts against the score when implementation complexity outpaces it (a 1,300-line flow algorithm with 28 tests cannot score 9.5 next to a 120-test rule at 9.8). Cross-boundary/inter-procedural blindness (helper methods that store arguments, fields mutated elsewhere) is a shared, deliberate design limit of every rule in this package and is *not* per-rule score debt; missing same-boundary sinks or untested in-code branches *are*.

## Per-Rule Details

### DI001 -- Scope Disposal (Warning)

**Analyzer: 9.6/10** | Tests: 59 | **Fixer: 8.8/10** | Fix Tests: 15

Operation-based tracking covers lambdas, fields, conditionals, nested scopes, and both `CreateScope()`/`CreateAsyncScope()` entry points. Conditional-access creations (`_provider?.CreateScope()`) participate in the consumption-shape proofs: the analyzer resolves the enclosing `ConditionalAccessExpressionSyntax` before matching initializer, assignment, return-statement, and arrow-body parents, so explicitly disposed (`scope?.Dispose()` including `finally` cleanup and predeclared reassignment), returned, and arrow-returned conditional creations stay quiet while undisposed ones still report. The await-using fix is suppressed for conditional-access creations because `factory?.CreateAsyncScope()` yields a nullable `AsyncServiceScope` with no `DisposeAsync`; the plain using fix remains offered for that shape. Explicit-disposal proofs reject `Dispose()` / `DisposeAsync()` calls that are reachable only through unsafe conditional branches, switch sections, loops, catch blocks, or after branch exits that can bypass shared cleanup, while continuing to accept straight-line, same-branch pre-exit, mutually exclusive branch-exit, and `finally` disposal patterns. Conditional ownership is now modeled for predeclared nullable scope locals assigned inside `if` / `else` or `try` blocks and disposed later through conditional access, exact non-null guards, or `finally` cleanup. Reassignment and repeated loop-creation guardrails keep the analyzer from treating one later dispose call as proof for a lost or repeatedly overwritten scope. Fixer wraps in `using`/`await using` statement and now offers `await using` only when the nearest callable can legally await it, so synchronous `CreateAsyncScope()` fixes stay on plain `using`.

### DI002 -- Scope Escape (Warning)

**Analyzer: 8.8/10** (re-scored from 9.5 in the 2026-06-10 audit; raised from 8.5 after the 2.10.3 sink expansion) | Tests: 57 | **Fixer: 8.5/10** | Fix Tests: 8

**2.10.3 sink expansion** closed the audit's two highest-frequency gaps: mutation of field/property-held containers (`_cache.Add(service)` and friends — tracked locals, capturing delegates, and direct resolutions passed as arguments) and event subscription (`_publisher.Changed += service.Handle`, captured-delegate handlers; owners that outlive the scope are fields, properties, parameters, and the enclosing instance, while scope-local publishers stay quiet). Indexer assignment (`_byTenant[key] = service`) was already detected through the indexer property symbol and is now pinned by a regression test. Remaining audit debt, FN direction: composite-construction returns (`return (service, other);`, `return new { Service = service };`); collection receivers that are parameters (`cacheParam.Add(service)` — caller-owned but unnamed-container); immutable collections reassigned in place (`_cache = _cache.Add(service)`). The new sinks use document-order guards as the execution-order proxy and root-of-chain receiver classification; method groups bind at conversion time and gate on IMethodSymbol. Method-argument escape (`Register(service)` where the callee stores it) and iterator/async state-machine capture remain out of scope by design (inter-procedural). Strong analyzer within its implemented sinks after executable-boundary and delegate-escape hardening. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, out/ref parameter escape sinks, and high-confidence delegates that capture scoped services before escaping via returns, fields, properties, or ref/out parameters. Reassignment guardrails keep stale delegate captures quiet before later escape sinks. Conditional-access shapes participate in escape detection: resolutions through `scope?.ServiceProvider.GetRequiredService<T>()` and chained `scope?.ServiceProvider?.GetRequiredService<T>()` are recognized (the provider receiver is resolved through the `MemberBindingExpressionSyntax` and its owning conditional access), `using var scope = factory?.CreateScope();` creations are tracked, and the consumption shape (return, field/property capture, local tracking) is classified from the outermost enclosing conditional access. Transient resolutions and locally-consumed services through the same shapes stay quiet.

The fixer intentionally offers pragma suppression only, because moving scoped services across ownership boundaries is context-dependent. Suppression coverage now spans direct returns, field/property assignments, alias returns, ref/out parameter escapes, captured-delegate escapes, local-function returns, and lambda-body assignments while keeping the transformation trivia-only and low blast radius.

### DI003 -- Captive Dependency (Warning)

**Analyzer: 9.8/10** | Tests: 120 | **Fixer: 9/10** | Fix Tests: 13

Strong runtime-correctness rule. Instance-backed registrations are explicitly excluded from constructor analysis, direct + ServiceDescriptor regressions are covered, collection-shaped captures through `IEnumerable<T>` / DI `GetServices<T>()` are detected without matching unrelated same-named APIs, and known scoped framework services such as `IOptionsSnapshot<T>` plus EF Core contexts registered through `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, or `AddPooledDbContextFactory(...)` now participate in lifetime checks, including guarded `AddDbContext<TService,TImplementation>(...)` and `AddDbContextPool<TService,TImplementation>(...)` implementation self-registrations. Stable same-block local delegate factories are inspected, including later definite simple reassignments, predeclared delegates assigned after declaration, same-declaration delegate reassignments, recursive local delegate aliases, exhaustive `if`/`else` branch reassignments, exhaustive local-function branch rewrites, definite directly invoked block-bodied and expression-bodied local-function reassignments, method-group delegate aliases to local functions that rewrite the factory, synchronous writes before the first `await` in unawaited async local functions, and inherited keyed factory parameters across conditional delegate branches; conditional, short-circuit, switch-arm, `for` incrementor, or early-return-bypassed local-function writes stay possible instead of replacing the prior delegate, iterator local-function bodies are not treated as executed at invocation time, no-op, mixed-value, and intervening-write alias cycles preserve reachable factory snapshots, guard-clause and throwing branch exits do not keep stale factory values reachable, overwritten branch-local helper rewrites do not leak stale diagnostics, branch-local helper rewrites on the only completing path clear stale initializer values, local-function guard returns keep prior factory values reachable, nested branch writes that return before registration stay unreachable, reassigned helper delegate aliases are resolved at the invocation site, unrelated assignment left-hand-side uses including invoked anonymous delegates do not count as factory writes, unreachable `ref`/`out` writes do not make a reachable initializer opaque, conditional nested returns do not clear reachable factory values, and opaque writes including direct delegate calls, delegate `.Invoke()` calls, and reachable ref/out writes stay conservative. Existing explicit EF context/options/factory registrations keep their original lifetime when later EF helpers use `TryAdd`-style registrations, with scoped options/factory guardrails across factory and pooled helpers. Factory-created `IDbContextFactory<TContext>` and singleton `DbContextOptions<TContext>` dependencies stay quiet, while `AddDbContextFactory(..., ServiceLifetime.Transient)` reports the context dependency as transient rather than scoped. Fixer adjusts local explicit registration lifetimes with coverage across direct, keyed, `TryAdd*`, inline-factory, `typeof(...)`, `ServiceDescriptor.Describe(...)` including named lifetime arguments, `new ServiceDescriptor(...)`, descriptor factory forms, and conditional-access (`services?.AddSingleton<...>()` etc.) lifetime rewrites that preserve the trigger's null-safe shape.

### DI004 -- Use After Dispose (Warning)

**Analyzer: 8.9/10** (re-scored from 10 in the 2026-06-10 audit; raised from 8.7 after the 2.10.5 branch-awareness fix) | Tests: 54 | **Fixer: 8.8/10** | Fix Tests: 10

**2.10.5 branch-awareness fix:** the explicit-dispose path is still position-anchored at the first `Dispose()`, but the use-reporting pass now skips branches mutually exclusive with the dispose site (opposite if/else arm — else-if chains included via span containment — and different switch sections), closing the audit's false-positive shape; a use on the shared path after a conditional dispose still reports because the dispose may have run on the taken branch. Scopes stored in instance/static fields and later disposed remain invisible (`CollectExplicitScopeVariables` only collects locals) — a real-world pattern, but it sits at the cross-boundary design limit shared by every rule in this package. Strong after explicit-disposal and post-boundary state hardening within those limits. Covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, predeclared scopes, explicit `Dispose()` / `DisposeAsync()`, conditional/member uses, deconstruction, `await foreach`, keyed constants, deferred delegate capture, and mixed `GetServices<T>()` collections while keeping uncertain lifetimes silent. Conditional-access *resolutions* are tracked alongside the existing conditional-use coverage: `service = scope?.ServiceProvider.GetRequiredService<T>();`, chained `scope?.ServiceProvider?.GetRequiredService<T>()`, and scopes created via `using (var scope = factory?.CreateScope())` participate in use-after-dispose detection (the assigned local resolves through the enclosing `ConditionalAccessExpressionSyntax`, and provider receivers resolve through the `.ServiceProvider` member binding), while conditional resolutions consumed inside the scope stay quiet. Fixer now moves only simple immediate invocation-style uses whose diagnostic local was assigned inside the immediately preceding `using` block, and it offers pragma suppression for context-dependent cases. Guardrails cover unrelated adjacent scopes, escape assignments, nested-boundary assignments, invocation-argument diagnostics, awaited immediate invocations, comments, return-only suppression, and `await using` blocks.

### DI005 -- Async Disposal (Warning)

**Analyzer: 9.2/10** | Tests: 24 | **Fixer: 8.8/10** | Fix Tests: 11

Narrow rule with a clear trigger: `CreateScope()` in async flows. Coverage now includes async methods, lambdas, local functions, anonymous methods, top-level programs that use `await`, `IServiceProvider.CreateScope()`, and **conditional-access receivers** such as `_scopeFactory?.CreateScope()` and `_provider?.CreateScope()` whose invocation expression is a `MemberBindingExpressionSyntax` inside a `ConditionalAccessExpressionSyntax`. Top-level detection ignores nested async local functions, lambdas, and anonymous methods so otherwise synchronous top-level scope creation stays quiet. Fixer replaces direct safe `using` declaration/statement resources with `CreateAsyncScope()` plus `await using`, including top-level using declarations, and now skips nested `CreateScope()` arguments inside other disposable resource initializers.

### DI006 -- Static Provider Cache (Warning)

**Analyzer: 9.5/10** | Tests: 28 | **Fixer: 9.2/10** | Fix Tests: 16

Symbol-level rule with low ambiguity. Coverage now spans direct provider types, keyed providers, inherited provider types, static classes, partial members, and a wrapper-recognition layer that recurses through `Lazy<T>`, `Lazy<Task<T>>`, `Lazy<ValueTask<T>>`, `Func<T>`, `AsyncLocal<T>`, and `ThreadLocal<T>` plus dictionary-of-providers shapes (`Dictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ConcurrentDictionary<,>`) whose value type is a provider abstraction. Simple holder types whose only instance member is a provider abstraction are detected, with `dotnet_code_quality.DI006.detect_holder_pattern = false` available for intentional wrapper-heavy codebases. Negative coverage keeps non-provider dictionary values, instance holders, and multi-member holder types silent. The fixer remains conservative: it removes `static` only for private, non-partial member shapes that are not referenced from static member bodies, static local functions, or static lambdas.

### DI007 -- Service Locator Anti-Pattern (Info)

**Analyzer: 9.6/10** | Tests: 38

Informational by design, now context-aware via symbol checks rather than method-name spelling alone. Allowed contexts cover factory registrations, ASP.NET Core middleware `Invoke`/`InvokeAsync` methods with `HttpContext` as the first parameter, value-returning `Create*`/`Build*` factory methods including `Task<T>` / `ValueTask<T>` async factories, exact hosting entry points (`BackgroundService.ExecuteAsync` overrides, `IHostedService.StartAsync`/`StopAsync`, and `IHostedLifecycleService` lifecycle methods), exact options implementations (`IConfigureOptions<T>.Configure`, `IConfigureNamedOptions<T>`, `IPostConfigureOptions<T>.PostConfigure`, `IValidateOptions<T>.Validate`), and provider-aware DI/options factory delegates only when the lambda is an argument to a recognized factory boundary. Guardrails keep arbitrary `Invoke` methods, `void` or plain-`Task` `Create*`/`Build*` side-effect methods, local `Func<IServiceProvider, T>` delegates, and helper methods inside hosted/options types reportable. No code fix -- service locator elimination requires architectural decisions.

### DI008 -- Disposable Transient (Warning)

**Analyzer: 9.5/10** | Tests: 44 | **Fixer: 9.3/10** | Fix Tests: 18

Strong coverage across generic registrations, `typeof`, keyed registrations, open generics, `IDisposable`, and `IAsyncDisposable`. The latest pass extends detection to `services.Add(ServiceDescriptor.Transient<...>())`, `services.Add(ServiceDescriptor.Describe(..., ServiceLifetime.Transient))`, `services.Add(new ServiceDescriptor(..., ServiceLifetime.Transient))`, `TryAddTransient` / `TryAddKeyedTransient` from `ServiceCollectionDescriptorExtensions`, and both single-descriptor and collection-shaped `TryAddEnumerable(...)` calls. Lifetime is recognised through both descriptor factory methods and explicit `ServiceLifetime` arguments, and factory shapes (lambdas, method groups, delegate-typed arguments) continue to suppress correctly inside the descriptor path. Receiver-type analysis on `services.Add(...)` filters out arbitrary `ICollection<T>.Add` calls. Named `serviceType:` / `implementationType:` mapping uses bound Roslyn parameters so out-of-order named overloads report the disposable implementation correctly. `dotnet_code_quality.DI008.allowed_disposable_types` supports intentional disposable-transient allowlists by simple or full type name. Fixer changes transient to scoped/singleton across direct `AddTransient`, `ServiceDescriptor.Transient(...)`, `ServiceDescriptor.Describe(..., ServiceLifetime.Transient)`, and `new ServiceDescriptor(..., ServiceLifetime.Transient)` forms, with factory conversion still restricted to safe generic direct registrations.

### DI009 -- Open Generic Lifetime Mismatch (Warning)

**Analyzer: 9.2/10** | Tests: 27 | **Fixer: 9/10** | Fix Tests: 15

Strong analyzer after constructor/collection hardening. Handles optional/default-value constructor selection, ambiguous equally-greedy constructor silence, and `IEnumerable<T>` captures. The dependency-lifetime lookup now consults the closed-generic user registration first, then the open-generic user registration, and finally the shared `KnownServiceLifetimeClassifier`, so open-generic singletons that capture `IOptionsSnapshot<T>` are reported even when the application does not register Options manually, while `IOptions<T>` and `IOptionsMonitor<T>` stay silent because the classifier reports them as singletons. Explicit user registrations of a closed framework-shaped dependency such as `services.AddSingleton<IOptionsSnapshot<MyOptions>, MySnapshot>()` keep their declared lifetime and override the framework default. `IEnumerable<T>` captures combine the user registration and the framework classifier with the worst (shortest-lived) lifetime, so an explicit closed singleton element does not hide an additional open-generic framework scoped element the container still includes. Code fix refactored with `RegistrationKind`/`LifetimeKind` enum extraction and comprehensive regression suite. Defensive fix: `CreateServiceLifetimeExpression` now emits `ServiceLifetime.X` member access (not bare identifier) for `SimpleNameSyntax`, preventing uncompilable output for const-backed lifetime identifiers.

### DI010 -- Constructor Over-Injection (Info)

**Analyzer: 9.7/10** | Tests: 29

Strongest info-level rule. Follows likely public activation constructors, covers conservative factory paths, uses symbol-accurate exclusions, and supports `.editorconfig` threshold overrides via `dotnet_code_quality.DI010.max_dependencies`. Straight-line factory lambdas, method groups, and local-function method groups are analyzed when they directly return or finish with a single final `return new Service(...)` / `ActivatorUtilities.CreateInstance<T>(...)`; nested local helper/lambda returns no longer hide the final factory return, while branching multi-return factories stay quiet. Protected/internal helper constructors are ignored because the default container cannot activate them; DI018 owns registrations with no public constructor. No code fix -- reducing constructor parameters requires design decisions.

### DI011 -- Service Provider Injection (Info)

**Analyzer: 9.5/10** | Tests: 28

Uses likely-public-activation-constructor logic with good allowance coverage for factory-shaped classes, hosted services, endpoint filter factories, singleton `IServiceScopeFactory` bridge patterns, and real ASP.NET Core middleware classes whose public `Invoke`/`InvokeAsync` method returns `Task` and accepts `HttpContext` first. Factory exemptions now require a value-returning factory member on the `*Factory` type or interface, including inherited and `Task<T>` / `ValueTask<T>` async factory members; name-only factory markers, `void` factories, and plain-`Task` side-effect methods remain reportable. Provider parameters on protected/internal constructors stay quiet because the container cannot activate those constructors. Arbitrary invoker-style classes remain reportable. Stays quiet for valid implementation-instance registrations. No code fix -- alternatives depend on architectural context.

### DI012 -- Conditional Registration Misuse (Info)

**Analyzer: 9.2/10** | Tests: 32 | **Fixer: 9/10** | Fix Tests: 8

Strong after flow/barrier hardening. Follows same-collection aliases, source-defined helper/local-function wrappers, distinct object-created collection flows, keyed variants, ServiceDescriptor shapes, EF Core helper registrations that preserve earlier explicit context registrations, and opaque ordering barriers. Supports both DI012 (TryAdd ignored) and DI012b (duplicate registration) diagnostics. The fixer removes standalone block-contained or top-level ignored `TryAdd*` and `TryAddKeyed*` registrations, including the conditional-access form `services?.TryAdd*(...)` whose invocation expression is a `MemberBindingExpressionSyntax` inside a `ConditionalAccessExpressionSyntax`. Duplicate override cases and embedded single-line statement bodies stay manual.

### DI013 -- Implementation Type Mismatch (Error)

**Analyzer: 9.5/10** | Tests: 59 | **Fixer: 9/10** | Fix Tests: 14

Most comprehensive test file in the repo. Covers variance-aware closed generic assignability, open-generic projection checks, collector-fed registration shapes, named direct overload arguments, instance-backed mismatches, all registration patterns (typeof, generic forms), interfaces, base classes, and abstract classes. The only Error-severity rule -- critical that it has no false positives. The fixer intentionally offers broad assists but keeps FixAll disabled because retargeting service/implementation types requires user judgment; coverage now spans invalid implementation-instance service-type retargeting, removal, top-level removals, embedded single-line safe rewrites, `typeof(...)` implementation replacement and removal assists, and conditional-access (`services?.AddSingleton(...)`) standalone-removal. The remove-registration assist is limited to standalone block or top-level statements while embedded single-line statement bodies require a manual edit or a symbol-backed type rewrite.

### DI014 -- Root Provider Not Disposed (Warning)

**Analyzer: 9.3/10** | Tests: 84 | **Fixer: 9/10** | Fix Tests: 12

Concrete lifetime rule with coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. Conditional-access creations (`services?.BuildServiceProvider()`) participate in the consumption-shape proofs: the analyzer resolves the enclosing `ConditionalAccessExpressionSyntax` before matching initializer, assignment, return-statement, and arrow-body parents, so explicitly disposed (`provider?.Dispose()` including `finally` cleanup and predeclared reassignment), returned, and arrow-returned conditional creations stay quiet while undisposed ones still report; the fixer offers the same `using`/`await using` rewrite for that shape because the local is a nullable reference type and `ServiceProvider` implements both disposal interfaces. Fixer wraps `BuildServiceProvider()` in `using`/`await using`. Post-hardening: fixed `IsAsyncMethod` bug where it checked `MethodDeclarationSyntax` ancestors before `LocalFunctionStatementSyntax`/`LambdaExpressionSyntax`, causing async local functions inside sync methods to get plain `using` instead of `await using`. Now walks ancestors in order and returns on the first callable encountered, including synchronous lambdas inside async methods where plain `using` is required. Added tests for async local function inside sync method, sync lambda inside async method, multiple BuildServiceProvider calls, local function scopes, and chained fluent builder patterns.

Latest pass tightened local ownership proofs: conditional and catch-only `Dispose()` calls no longer suppress DI014, provider reassignments before disposal report the leaked first provider, mutually exclusive `if`/`else` assignments to the same outer provider are accepted when a later dispose covers the selected branch, branches with direct or nested `return`/`throw` exits before shared cleanup stay reportable unless same-block/branch-block `Dispose()` or awaited `DisposeAsync()` cleanup or a containing `finally` disposes the provider, repeated loop assignments disposed only after the loop stay reportable, and predeclared providers assigned inside `try` blocks are recognized when a `finally` block disposes them. The fixer now skips diagnostics that already have manual disposal code, keeping partial-disposal repairs explicit instead of layering `using` on top of an unsafe flow.

Wrapped-result pass (2.9.6): disposal and caller-owned return proofs now see through parenthesized results, provable same-instance upcasts, and the null-forgiving operator (`services.BuildServiceProvider()!`), including combinations with conditional-access creations. Proofs are rejected when the result passes through a user-defined conversion (explicit or implicit operator) or an unproven downcast (such as `(Wrapper)(object)...` or a downcast from an interface), because those flows are not proven to hand the root provider itself to the disposal or return site.

### DI015 -- Unresolvable Dependency (Warning)

**Analyzer: 9.8/10** | Tests: 121 | **Fixer: 9.2/10** | Fix Tests: 16

One of the strongest overall rules. Broad support for keyed, inherited-key, `AnyKey`, factory, wrapper, open-generic, strict-mode, implementation-instance, definite registration-mutation scenarios, and EF Core `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, and `AddPooledDbContextFactory(...)` registrations with synthetic `DbContextOptions<TContext>`, `IDbContextFactory<TContext>`, and service/implementation overload self-registration support. Full dependency resolution engine with reachability tracking. Recognized high-confidence factory requests, including stable local delegate factories with inherited keyed parameters across conditional delegate branches, still report when the same factory body also contains unrelated helper calls, preserving runtime-dangerous missing-dependency coverage. Local delegate factories follow later definite simple reassignments, predeclared delegates assigned after declaration, same-declaration delegate reassignments, recursive local delegate aliases, exhaustive `if`/`else` branch reassignments, exhaustive local-function branch rewrites, definite block-bodied or expression-bodied local-function reassignments, method-group delegate aliases to local functions that rewrite the factory, and synchronous writes before the first `await` in unawaited async local functions; conditional, short-circuit, switch-arm, `for` incrementor, or early-return-bypassed local-function writes remain possible, iterator local-function bodies are not treated as executed at invocation time, no-op, mixed-value, and intervening-write alias cycles preserve reachable factory snapshots, guard-clause and throwing branch exits do not keep stale factory values reachable, overwritten branch-local helper rewrites do not leak stale diagnostics, branch-local helper rewrites on the only completing path clear stale initializer values, local-function guard returns keep prior factory values reachable, nested branch writes that return before registration stay unreachable, reassigned helper delegate aliases are resolved at the invocation site, duplicate method-group branch requests are de-duplicated, unrelated assignment left-hand-side uses including invoked anonymous delegates do not count as factory writes, unreachable `ref`/`out` writes do not make a reachable initializer opaque, conditional nested returns do not clear reachable factory values, and opaque writes including direct delegate calls, delegate `.Invoke()` calls, and reachable ref/out writes stay conservative. Fixer adds safe unkeyed and keyed concrete self-bindings, including direct constructor-rooted, direct factory-rooted, `TryAdd*`, and local-alias registration sites, plus conditional-access (`services?.AddXxx(...)`) trigger sites where the inserted statement preserves the same null-safe shape; abstract/interface, multi-missing, transitive-only, opaque, and unsafe descriptor paths stay no-fix.

### DI016 -- BuildServiceProvider Misuse (Warning)

**Analyzer: 9.4/10** | Tests: 27

Strong after builder-flow hardening. Covers assignable `IServiceCollection` abstractions, same-boundary `.Services` aliases, helper methods that forward builder-style flows, and provider-factory guardrails. Receiver resolution unwraps the null-forgiving operator (`builder.Services!`) and same-type `IServiceCollection` casts (`(IServiceCollection)builder.Services`) when resolving registration receivers, helper return expressions, and local-variable initializers. Conditional-access invocations are now recognized too: `builder.Services?.BuildServiceProvider()` (where the invocation expression is a `MemberBindingExpressionSyntax` and `TryGetReceiverExpression` walks up to the enclosing `ConditionalAccessExpressionSyntax` for the real receiver) and `builder?.Services.BuildServiceProvider()` (where the receiver-side `.Services` is a `MemberBindingExpressionSyntax` recognized by `IsServicesPropertySource`). Provider-factory methods that return `IServiceProvider` keep their guardrail. No code fix -- the correct alternative varies by context.

### DI017 -- Circular Dependency (Warning)

**Analyzer: 8.8/10** (re-scored from 9.5, 2026-06-10) | Tests: 28

**Why the re-score:** test density does not match algorithm complexity — 28 tests over 1,319 lines of flow-aware graph code, versus DI003's 120 tests at 9.8. Specific debt: `Replace` mutation is handled in `ApplyMutation` but has zero tests (the only mutation tests cover `RemoveAll`); keyed cycles have only 2 tests despite the PR #32 keyed-dedup bug history; and `Lazy<T>` constructor parameters are not modeled at all (likely silent today because `Lazy<T>` is unregistered in default MEDI, but that silence is incidental, not proven). Memoization is correctly keyed per registration instance including `Order`. Major hardening pass applied. Cycle detection now uses reachable, flow-aware effective registrations, honors `TryAdd` plus `RemoveAll` / `Replace` removal, analyzes high-confidence factory requests including `ActivatorUtilities.CreateInstance<T>(...)` and `GetServiceOrCreateInstance<T>(...)`, inherited keyed dependencies, open-generic activation, and registered `IEnumerable<T>` elements, and keeps ambiguous constructors, dynamic keys, opaque factories, unrelated service collections, uninvoked wrappers, and mixed factory bodies with extra unclassified invocations silent. `knownNoCycle` memoization remains in place for scale. No code fix -- breaking cycles requires architectural decisions.

### DI018 -- Non-Instantiable Implementation (Warning)

**Analyzer: 9.2/10** | Tests: 34

Open-generic constructor checks use the generic definition. Direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices. Correctly detects abstract classes, interfaces, static classes, types with no accessible constructors, and delegate types registered without a factory expression including the one-`Type` self-binding overload `services.AddSingleton(typeof(MyDelegate))` (delegates carry only implicit `(object, IntPtr)` / `(object, UIntPtr)` constructors that the default DI container cannot populate, so a non-factory delegate registration fails at activation). Factory registrations and explicit delegate-instance registrations stay quiet.

The shared `RegistrationCollector` now mirrors Pattern 2's self-binding default in the operation-arguments path: when the **selected non-generic overload's signature** exposes only `serviceType` (no `implementationType`, `implementationFactory`, or `implementationInstance` parameter), `implementationType` defaults to `serviceType`. The two-`Type` overload with a non-extractable implementation argument (`services.AddSingleton(typeof(IFoo), variableHoldingType)`) keeps `implementationType = null` so DI018 stays silent rather than collapsing the registration to a false `IFoo -> IFoo` shape. This brings one-`Type` `AddSingleton(typeof(T))` / `AddScoped(typeof(T))` / `AddTransient(typeof(T))` and their `TryAdd*` variants into scope for every downstream rule, not just DI018.

### DI019 -- Root Scoped Resolution (Warning)

**Analyzer: 9.7/10** | Tests: 58 | **Fixer: 9/10** | Fix Tests: 16

Detects scoped services, known scoped framework services such as `IOptionsSnapshot<T>`, EF Core contexts registered through `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, or `AddPooledDbContextFactory(...)`, including service/implementation overload self-registrations, and service graphs that reach scoped services, resolved from a root provider. Covers known ASP.NET Core, ASP.NET test-host, and Generic Host `Services` properties including nullable `Services!` use, `ApplicationServices`, endpoint-route `ServiceProvider`, `BuildServiceProvider()`, local root-provider variables, singleton implementations, hosted services, `GetServices<T>()`, and keyed resolutions. Receiver extraction now resolves the true provider receiver before classification, so conditional-access shapes participate: `host?.Services.GetRequiredService<T>()`, chained `app?.Services?.GetRequiredService<T>()`, and local aliases of `var rootServices = app?.Services;` report, with the member-binding owner resolved from the enclosing `ConditionalAccessExpressionSyntax` (the owning conditional access is the nearest ancestor whose `WhenNotNull` contains the binding, so chained accesses resolve to the real owner). Stays silent for scoped providers from `CreateScope()`/`CreateAsyncScope()`, `HttpContext.RequestServices` including the conditional-access forms `httpContext?.RequestServices...` and `scope?.ServiceProvider...` inside singleton implementations, arbitrary holder properties named `Services`, DI factory lambdas covered by DI003, singleton-safe options abstractions, singleton-lifetime `AddDbContext(...)` registrations, existing explicit EF registrations preserved by later `TryAdd`-style helpers, factory/options services from EF factory and pooled registrations, `AddDbContextFactory(..., ServiceLifetime.Transient)` context registrations, and dynamic service-type requests.

The scope-wrapping code fix is gated against scoped-service escape (assignment/argument), type-receiver static calls, and synchronous/async context mismatches, and now also refuses resolutions evaluated inside a conditional access's `WhenNotNull` (`var s = host?.Services.GetRequiredService<T>();`), where lifting the member-binding receiver into `using var scope = ....CreateScope();` would emit non-compiling code and drop the null-shortcut semantics.

### DI020 -- Middleware Scoped Service (Warning)

**Analyzer: 9.0/10** (re-scored from 9.3 to 8.4 in the 2026-06-10 audit; raised to 9.0 after the 2.10.4 gap-closure pass) | Tests: 26

Detects conventional middleware constructors that capture scoped services — direct and transitive via the shared `ScopedDependencyGraph` — for the application lifetime, with `Invoke`/`InvokeAsync` parameter remediation guidance. Constructor selection honors explicit `UseMiddleware` arguments, optional parameters, and greedy-overload resolution.

**2.10.4 gap-closure pass:** the audit's untested paths are all covered now, and writing the tests flushed out two real issues — an explicit-argument false positive (a scoped parameter satisfied by a `UseMiddleware` argument was still reported; constructor selection now threads its argument-fill map to reporting, and a parameter must be explicitly supplied at EVERY registration site to stay quiet) and a conditional-access miss (`app?.UseMiddleware<T>()` is now recognized through the enclosing conditional access). Covered shapes: non-generic `UseMiddleware(typeof(T))`, keyed scoped dependencies (`[FromKeyedServices]` match reports, different-key singleton stays silent), `IEndpointRouteBuilder` receivers, and the extension-method (`ReducedFrom`) path. Remaining minor edge: implicit-conversion argument binding relies on `Compilation.HasImplicitConversion` without a dedicated test. No code fix — moving a dependency to `Invoke` parameters changes the middleware's contract.

### DI021 / DI022 -- Concurrent Handler Shared State (Warning / Info)

**Analyzer: 8.8/10** | Tests: 102 (shared) | **Fixer: 8.5/10** | Fix Tests: 16 (shared)

One analyzer class, two descriptors. Detects non-thread-safe services (catalog: EF Core `DbContext` + derived, `DbConnection`/`DbCommand`/`DbTransaction`/`DbDataReader` and their `System.Data` interfaces, `IDbContextTransaction`, `HttpContext`) captured into concurrently-invoked handlers via instance/static fields, closures over outer locals, enclosing-method parameters, and the captured-scope resolution channel (`_scope.ServiceProvider.GetRequiredService<T>()` against a scope captured from outside the handler — closing the "move the resolution inside the lambda" silencing loophole). Sink table v1: `ServiceBusProcessor` (config-gated on `MaxConcurrentCalls`: proven `> 1` reports DI021, proven `== 1` is silent, otherwise DI022 Info), `ServiceBusSessionProcessor` (always-concurrent with the proven `MaxConcurrentSessions == 1` escape), `EventProcessorClient`, `System.Threading.Timer` (finite-period proof: `Timeout.Infinite`/`InfiniteTimeSpan`/`FromMilliseconds(-1)` periods, provably-infinite due times, zero (one-shot) periods, and never-started single-arg constructions suppress unless a finite `Change(...)` on the same timer instance starts it), `System.Timers.Timer.Elapsed` (every `AutoReset` write must be constant `false`, or every `SynchronizingObject` write provably non-null, on this timer instance; `SynchronizingObject = null` or a later `AutoReset = true` is not proof), `Parallel.For/ForEach/ForEachAsync/Invoke` (params-array delegate extraction; proven `MaxDegreeOfParallelism == 1` suppresses), and RabbitMQ consumers (2.10.1: `EventingBasicConsumer.Received`, `AsyncEventingBasicConsumer.Received`/`ReceivedAsync` across the v6/v7 drift; `ConsumerDispatchConcurrency` evaluated with the strengthen-only containing-type scan — constant `> 1` reports DI021, everything else DI022, no sequential proof until instance-correlated chain tracing lands; knob constants count across integral types, including v7's ushort). Concurrency-knob proofs are instance-correlated: sequential proofs only come from the options object traced to this sink's own creation (event receiver → single initializer/assignment → options argument), so a `MaxConcurrentCalls = 1` on a different processor in the same type never silences this one; untraceable receivers can only strengthen to concurrent, never prove sequential. Sinks and catalog match by fully-qualified name so source stubs in tests and samples behave like the real packages, and same-named user types in other namespaces stay silent. Method-group and local-function handlers are analyzed when declared on the registration's own type in the same tree (cross-type/cross-tree bails silently, RS1030-driven), and one-hop thin-delegation lambdas (`args => HandleAsync(args)`) follow into the same-type target. Serialization guards ship in v1: per-use `lock` coverage (the monitor object must be shared — locking an object created inside the handler guards nothing), a symbol-correlated `SemaphoreSlim` bracket (the semaphore must be shared from outside the handler, wait and `finally`-release must target the same instance, and only uses inside the guarded try region — or after an in-try wait — are protected, so a use before the wait still reports), `Interlocked.CompareExchange`/`Monitor.TryEnter` early-return reentrancy guards (the guard must be a top-level handler statement and protects only what executes after it), timer re-arm (correlated to the sink's own timer instance and guarding only uses after the stop), and the disposable async-lock name heuristic (shared lock instance required; only the using region is guarded). Target-typed `new() { ... }` options initializers participate in knob proofs. Reassigned-inside-handler symbols, dispose-only references, whitelisted captures (`IDbContextFactory<T>`, `IServiceScopeFactory`, `ILogger`, `IOptions*`, `IHttpContextAccessor`), handler parameters, and in-handler creations stay quiet. Reports once per (handler, symbol) at the first use with capture site, sink registration, and remaining uses as additional locations, plus a properties bag (`SymbolName`, `ServiceTypeName`, `CaptureKind`, `HandlerIsAsync`, `SinkKind`) that drives the fixer without re-deriving analysis.

**Known under-reports (v2 targets, deliberate — do not re-litigate as bugs):** concurrency-knob proofs are containing-type-scoped plus same-tree helper-method following (2.10.2: non-virtual single-declaration helpers returning a fresh options creation prove both directions; cross-type/cross-tree wiring still reports DI022 instead of DI021). Knob-write tracking is deliberately a definite-overwrite + branch-insensitive-union lattice, not a CFG: straight-line writes in the declaring block overwrite, nested-block writes join the candidate union (any constant above 1 among candidates reports DI021), nested-function writes poison sequential proofs as unknown candidates regardless of declaration position, and compound writes are unknowable — execution-order analysis of local-function invocation is out of scope (an always-invoked mutator local function reports DI022, not DI021), alias mutations are not correlated back to the original local (poison to DI022, never silenced), and timer AutoReset proofs remain branch-insensitive all-writes-must-be-safe; PLINQ, TPL Dataflow, and EventHubs batch sinks are v2 registry rows; RabbitMQ consumers (shipped 2.10.1: `EventingBasicConsumer.Received`, `AsyncEventingBasicConsumer.Received`/`ReceivedAsync`, FQN-matched) use the strengthen-only containing-type `ConsumerDispatchConcurrency` scan — a constant above 1 anywhere in the type upgrades to DI021, everything else is DI022, and no shape proves a specific consumer sequential until factory→connection→channel→consumer instance tracing lands; the scoped-lifetime DI022 tier (RegistrationCollector-backed) is v2; `Task.Run`/`WhenAll` fan-out is parked as a future DI023; nested-lambda captures inside a handler are not attributed to the outer handler; `GetService(typeof(T))` non-generic resolutions are not tracked by the resolution channel; `Monitor.TryEnter` reentrancy guards are not correlated to a specific lock object (over-suppression edge, FN direction); one-hop delegation does not flow lambda arguments into the target method's parameters.

### DI021 fixer

Scope-per-invocation rewrite driven entirely by the diagnostic properties bag: inserts `await using var scope = _scopeFactory.CreateAsyncScope();` for async handlers or `using var scope = _scopeFactory.CreateScope();` for synchronous delegates, converts expression-bodied lambdas to blocks (return-vs-statement decided semantically), re-resolves the service, rewrites all use sites, reuses an existing `IServiceScopeFactory` field or plumbs field + constructor parameter + assignment, and removes the dead captured field, its constructor assignment, and the feeding parameter (constructor-scoped reference checks so the new handler local cannot keep the parameter alive). Name collisions for the inserted `scope`/local are avoided against all handler identifiers, which also prevents shadowing captured outer locals. Refusals: static handlers, `ScopeResolution`-kind diagnostics, non-async handlers with awaitable return types (a synchronous using-scope would dispose before the returned task completes), types without exactly one declared constructor (plumbing only one of several would leave other construction paths with a null factory), expression-bodied method handlers, and any use that no longer matches the analyzed shape. `GetFixAllProvider()` returns null.

## Code Fix Health

| Fixer | Fix Tests | Score | Risk Assessment |
|-------|-----------|-------|-----------------|
| DI001 (Scope Disposal) | 15 | 8.8 | Low -- behavior-changing rewrite now well-covered (nested, explicit types, trivia, async delegates, unsafe await-using suppression, conditional-access creation using/await-using gating) |
| DI002 (Scope Escape) | 8 | 8.5 | Low -- pragma-only suppression now covers direct, nested-boundary, alias, ref/out, property, and captured-delegate diagnostics |
| DI003 (Captive Dependency) | 13 | 9 | Low -- direct, keyed, TryAdd, inline-factory, typeof, ServiceDescriptor argument, descriptor-factory shape coverage, and conditional-access (`services?.AddXxx<...>()`) lifetime rewrite |
| DI004 (Use After Dispose) | 10 | 8.8 | Low -- move fix is gated to owning-scope immediate and awaited invocations, with unsafe escape/adjacent-scope shapes suppressed |
| DI005 (Async Scope) | 11 | 8.8 | Low -- narrow direct-resource using/await-using transformation, including top-level async using declarations and nested-resource no-fix guardrails |
| DI006 (Static Provider Cache) | 16 | 9.2 | Low -- direct and `Lazy<T>` cache shapes covered with conservative static-context fix gating |
| DI008 (Disposable Transient) | 18 | 9.3 | Low -- strong shape coverage, including named `typeof` overloads, `ServiceDescriptor.Transient(...)` method rewrites, and descriptor lifetime-argument replacement |
| DI009 (Open Generic Mismatch) | 15 | 9 | Low -- comprehensive refactor with defensive SimpleNameSyntax handling |
| DI012 (Ignored TryAdd) | 8 | 9 | Low -- narrow standalone block/top-level keyed/non-keyed statement removal (now including conditional-access `services?.TryAdd*(...)`) with embedded-statement guardrails |
| DI013 (Implementation Mismatch) | 14 | 9 | Medium -- broad assists are symbol-backed, implementation-instance retargeting/removal, top-level removals, embedded rewrites, and conditional-access standalone removal are covered; removal is standalone-only, FixAll disabled |
| DI014 (Root Provider) | 12 | 9 | Low -- reliable local disposal proofs, branch/reassignment/loop guardrails, branch-exit coverage, nearest-callable async/sync fixer boundaries, chained builders, conditional-access creation using/await-using rewrites, and wrapped-result (paren/upcast/null-forgiving) proofs covered |
| DI015 (Unresolvable Dependency) | 16 | 9.2 | Low -- keyed, factory, TryAdd, local-alias, and conditional-access (`services?.AddXxx(...)`) self-binding generation is tightly gated; inserted statement mirrors the trigger's null-safe shape; FixAll remains disabled |
| DI019 (Root Scoped Resolution) | 16 | 9 | Medium -- behavior-changing scope wrap, but escape analysis (assignment/argument/return/lambda capture), conditional-access refusal, async-context `CreateAsyncScope` selection, and name-collision handling are all covered; FixAll disabled |
| DI021/DI022 (Scope Per Invocation) | 16 | 8.5 | Medium -- behavior-changing handler rewrite with constructor plumbing and dead-field removal; static-handler/scope-resolution/no-constructor refusals and shadow-safe naming covered; FixAll disabled |

**Rules without code fixes:** DI007, DI010, DI011, DI016, DI017, DI018, DI020. These rules detect problems whose resolution requires architectural or context-dependent decisions.

## Infrastructure Health

| Component | Tests | Assessment |
|-----------|-------|------------|
| RegistrationCollector | 46 | Core engine, strong coverage, including EF Core AddDbContext, AddDbContextFactory, AddDbContextPool, AddPooledDbContextFactory, and guarded service/implementation self-registration modeling |
| WellKnownTypes | 29 | Type symbol cache, comprehensive |
| PerformanceRegression | 7 | Baseline performance guards |
| SampleDiagnosticsVerifier | 14 | SARIF contract (8) + verifier (2) + sample-docs freshness gates (4) |
| RegistrationCollector (ServiceDescriptor) | 4 + 2 | Infrastructure suite (4) plus rule-level ServiceDescriptor registration tests (2) |
| DiagnosticDescriptorSeverity | 3 + 1 Theory (23 cases) | Severity budget enforcement across all 22 rule IDs + DI012b |
| CompatibilitySmoke | 6 | Roslyn/package compatibility floor |
| CodeFixInventoryParity | 3 | Fixer inventory stays in sync with shipped rules |
| CrossRuleInteraction | 10 | Multi-rule scenario validation |
| KeyedService | 9 | DI 8.0 keyed service support |

**CI quality gates:** 85% line coverage, 70% branch coverage (enforced, CI fails on regression). Coverage badge auto-committed. PR coverage comments via sticky-pull-request-comment.

## Aggregate Metrics

| Metric | Value |
|--------|-------|
| Total tests | 1423 (verified passing 2026-06-10) |
| Analyzer tests | 1100 (per-rule 1079 + cross-rule 10 + keyed 9 + ServiceDescriptor registration 2) |
| Code fix tests | 188 |
| Infrastructure tests | 135 (112 facts + 23 severity theory cases) |
| Analyzer mean score | 9.3/10 |
| Fixer mean score | 8.9/10 |
| Rules at 9+ | 17/22 (sub-9: DI002 8.8, DI004 8.9, DI017 8.8, DI021 8.8, DI022 8.8) |
| Fixers at 8+ | 14/14 (100%) |
| Rules needing pass | 5 analyzer passes (DI020, DI021/DI022 v2, DI002, DI004, DI017 — see Work Priority), 0 fixers |
| TODO/FIXME in source | 0 (verified) |
| Skipped tests | 0 |

## Bugs Found During Hardening

| PR | Bug | Severity | Rule |
|----|-----|----------|------|
| Current | DI004 did not track services resolved through conditional access (`service = scope?.ServiceProvider.GetRequiredService<T>();`), so later uses after the scope was disposed went unreported; conditional-access scope creations (`using (var scope = factory?.CreateScope())`) were also invisible to scope collection | Medium | DI004 |
| Current | DI002 missed every conditional-access escape: `return scope?.ServiceProvider.GetRequiredService<T>();`, chained `scope?.ServiceProvider?...`, field captures, and locals resolved through `scope?.ServiceProvider...` that later escaped were silent because resolution recognition required a plain `MemberAccessExpressionSyntax` receiver and consumption checks matched the invocation's direct parent; `using var scope = factory?.CreateScope();` was also not tracked as a scope variable | Medium | DI002 |
| Current | DI014 consumption-shape checks matched the invocation's direct parent, so conditional-access creations (`var provider = services?.BuildServiceProvider();` with later `provider?.Dispose()` including `finally` cleanup and predeclared reassignment, `return services?.BuildServiceProvider();`, and arrow-bodied returns) were reported as leaks even though the provider was handled; the fixer also skipped the conditional-access initializer shape entirely | Medium | DI014 |
| Current | DI001 consumption-shape checks matched the invocation's direct parent, so conditional-access creations (`var scope = _provider?.CreateScope();` with later `scope?.Dispose()` including `finally` cleanup and predeclared reassignment, `return _provider?.CreateScope();`, and arrow-bodied returns) were reported as leaks even though the scope was handled | Medium | DI001 |
| Current | The DI001 "Add 'await using'" fix converted only `MemberAccessExpressionSyntax` receivers to `CreateAsyncScope`, so for conditional-access creations in async contexts it emitted `await using var scope = factory?.CreateScope();`, which does not compile (`IServiceScope` is not `IAsyncDisposable`; the converted form would not compile either because `factory?.CreateAsyncScope()` yields `Nullable<AsyncServiceScope>` with no `DisposeAsync`) | Medium | DI001 |
| Current | DI019 receiver extraction classified the conditional-access receiver (`host` in `host?.Services.GetRequiredService<T>()`) instead of the true provider receiver (the `.Services` member binding), so known root-provider properties accessed through conditional access -- `host?.Services...`, chained `app?.Services?...`, and `var sp = app?.Services;` aliases -- were never reported | Medium | DI019 |
| Current | DI019 scoped-provider recognition did not handle `MemberBindingExpressionSyntax`, so once conditional-access receivers became reachable, `httpContext?.RequestServices...` and `scope?.ServiceProvider...` inside singleton implementations would have fallen through to the singleton heuristic as false positives; the DI019 code fix also offered a rewrite for `var s = host?.Services.GetRequiredService<T>();` that emitted a standalone member binding (`using var scope = .Services.CreateScope();`) which does not compile | Medium | DI019 |
| Current | DI016 missed `BuildServiceProvider()` misuse when the receiver chain used conditional access (`builder.Services?.BuildServiceProvider()` or `builder?.Services.BuildServiceProvider()`), because `TryGetReceiverExpression` only matched `MemberAccessExpressionSyntax` and `IsServicesPropertySource` did not recognize `MemberBindingExpressionSyntax` as a Services source | Medium | DI016 |
| Current | DI005 missed `CreateScope()` calls whose invocation expression was a `MemberBindingExpressionSyntax` (the conditional-access form `provider?.CreateScope()` and `_scopeFactory?.CreateScope()`) because the fast-path filter only matched `MemberAccessExpressionSyntax` and `IdentifierNameSyntax` receivers | Medium | DI005 |
| Current | DI018 silently accepted delegate-type registrations such as `services.AddSingleton<MyHandler>()` because delegates have implicit public `(object, IntPtr)` constructors that satisfy the public-constructor check, yet the default DI container cannot populate those parameters and the registration fails at activation | Low | DI018 |
| Current | `RegistrationCollector` discarded one-`Type` non-generic self-binding registrations such as `services.AddSingleton(typeof(MyService))` because the operation-arguments extractor never set `implementationType`, leaving the registration invisible to DI018 (and other downstream rules) even though the DI extension self-binds | Low | Infrastructure (RegistrationCollector) |
| Current | DI009 captive-dependency analysis consulted only user registrations for parameter lifetime, missing open-generic singleton captures of known scoped framework services such as `IOptionsSnapshot<T>` whose lifetime is provided by the framework Options registration helpers | Medium | DI009 |
| Current | DI016 missed `BuildServiceProvider()` misuse when builder `.Services` flows were wrapped in the null-forgiving operator (`builder.Services!`) or a same-type `IServiceCollection` cast (`(IServiceCollection)builder.Services`) at the call site, in helper return expressions, or in local-variable initializers | Low | DI016 |
| Current | DI019 missed scoped resolutions from nullable known root-provider properties when the receiver used the null-forgiving operator, such as `app.Services!.GetRequiredService<T>()` | Low | DI019 |
| Current | DI001 accepted later shared scope disposal even when a branch-level `return` or straight-line early `return` after creation could bypass the cleanup | Medium | DI001 |
| Current | DI008 reported descriptor-based disposable transients in `ServiceDescriptor.Describe(...)` and `new ServiceDescriptor(...)` but did not offer the same scoped/singleton lifetime rewrite available for direct and `ServiceDescriptor.Transient(...)` registrations | Low | DI008 |
| Current | DI007 treated any method named `Create*` or `Build*` as a factory boundary, suppressing service-locator diagnostics in `void` or plain-`Task` side-effect methods | Low | DI007 |
| Current | DI011 treated any `*Factory` type or interface name as a provider-injection exception, suppressing diagnostics in name-only factory markers and side-effect-only classes | Low | DI011 |
| Current | DI011 reported singleton `IServiceScopeFactory` bridge patterns that DI003 recommends for deliberate scoped work from singleton services | Low | DI011 |
| Current | Shared constructor selection treated protected/internal constructors as activatable, risking DI010/DI011 false positives on helper constructors the default container cannot call | Low | DI010/DI011 |
| Current | DI010 missed straight-line factory blocks and local-function factory method groups that perform setup statements before a single final `return new Service(...)` | Low | DI010 |
| Current | DI011 treated any class with a public `Invoke` or `InvokeAsync` method as middleware, suppressing `IServiceProvider` injection diagnostics in arbitrary registered services | Low | DI011 |
| Current | DI007 treated any method named `Invoke` or `InvokeAsync` as middleware, suppressing service-locator diagnostics in arbitrary app methods | Low | DI007 |
| Current | DI012/DI013 standalone-removal fixers initially protected embedded single-line bodies by requiring `BlockSyntax`, which accidentally dropped safe top-level `Program.cs` removals | Low | DI012/DI013 |
| Current | Synthetic EF helper registrations overwrote earlier explicit context/options/factory registrations even though EF Core uses `TryAdd` for those services, risking DI003/DI019 false positives | Medium | DI003/DI019 |
| Current | EF Core `AddDbContext<TService,TImplementation>(...)` and `AddDbContextPool<TService,TImplementation>(...)` implementation self-registrations were invisible, so direct `TImplementation` dependencies could evade DI003/DI019 or be reported missing by DI015 | Medium | DI003/DI019/DI015 |
| Current | Predeclared local delegate factories assigned after declaration were ignored, hiding scoped captures or missing dependencies from DI003 and DI015 | Medium | DI003/DI015 |
| Current | Same-declaration local delegate reassignments were ignored, leaving stale initializer values reachable or dropping first assignments from later declarators | Medium | DI003/DI015 |
| #32 | DI017 constructor selection treated scalar params as resolvable, suppressing real cycles | Medium | DI017 |
| #32 | DI009 fixer emitted bare identifier for SimpleNameSyntax lifetime, producing uncompilable code | Medium | DI009 |
| #32 | DI017 keyed cycle dedup collapsed keys with same string representation (int `1` vs string `"1"`) | Medium | DI017 |
| #33 | DI014 `IsAsyncMethod` checked method before nearest callable, wrong `using`/`await using` in nested async | Medium | DI014 |
| #34 | DI002 fixer didn't check for existing TODO, causing duplicate TODOs on iterative application | Low | DI002 |
| Current | DI017 reported speculative cycles for ambiguous equally greedy constructor sets | Medium | DI017 |
| Current | DI003 missed captive scoped dependencies captured through `IEnumerable<T>` / `GetServices<T>()` | Medium | DI003 |
| Current | DI004 move fix could be offered for an unrelated immediately preceding `using` block or for an escape assignment | Medium | DI004 |
| Current | DI002 missed scoped services captured by delegates that escaped through later return, field, property, or ref/out sinks | Medium | DI002 |
| Current | DI003 missed scoped captures hidden behind stable local delegate factory variables passed to singleton registrations | Medium | DI003 |
| Current | Stable local delegate factory analysis still used the original delegate after a directly invoked local function reassigned it before registration | Low | DI003/DI015 |
| Current | Stable local delegate factory analysis suppressed diagnostics for later definite unsafe delegate reassignments before registration | Medium | DI003/DI015 |
| Current | Stable local delegate factory analysis treated conditional local-function reassignments as definite replacement, suppressing unsafe initializer diagnostics | Medium | DI003/DI015 |
| Current | Conditional keyed local delegate factories lost inherited registration-key binding because key-context analysis only accepted a single possible factory expression | Medium | DI003/DI015 |
| Current | Expression-bodied local-function delegate reassignments were treated as conditional writes, causing stale initializer false positives | Low | DI003/DI015 |
| Current | Early-returning local-function delegate reassignments were treated as definite writes, suppressing reachable unsafe initializer diagnostics | Medium | DI003/DI015 |
| Current | Exhaustive `if`/`else` delegate reassignments kept stale initializer values reachable, causing false positives after every branch replaced the factory | Low | DI003/DI015 |
| Current | Nested exhaustive `if`/`else` delegate reassignments cleared stale initializer values even when an outer condition could skip the replacement | Medium | DI003/DI015 |
| Current | Local delegate usage inside an unrelated assignment left-hand side was treated as a delegate write, hiding stable factory diagnostics | Medium | DI003/DI015 |
| Current | Invoked anonymous delegates treated unrelated assignment left-hand-side uses such as `metadata[factory] = ...` as opaque factory writes, hiding stable factory diagnostics | Medium | DI003/DI015 |
| Current | Branch delegate values from paths that returned before registration were still considered reachable at the registration site | Low | DI003/DI015 |
| Current | Unawaited async local-function delegate reassignments were treated as synchronous replacement before the following registration | Medium | DI003/DI015 |
| Current | Local delegate reassignments before a later conditional `return` were skipped even though the non-returning path still reached the registration | Medium | DI003/DI015 |
| Current | Stable local delegate factory analysis kept an unsafe initializer reachable when every path that reached registration first replaced the delegate and the other branch returned | Medium | DI003/DI015 |
| Current | Delegate reassignment exit checks looked past the registration site, so common extension methods that `return services;` after registration could hide reachable unsafe factories | Medium | DI003/DI015 |
| Current | Exhaustive `if` / `else if` / `else` delegate reassignments were not recognized, leaving stale initializer false positives after every completing branch replaced the factory | Low | DI003/DI015 |
| Current | Local-function delegate reassignments inside branches that returned before registration were still treated as reachable factory values | Low | DI003/DI015 |
| Current | Returned registrations such as `return services.AddScoped(..., factory)` were mistaken for exits before the factory use, hiding reachable delegate reassignments | Medium | DI003/DI015 |
| Current | Local functions with both a direct delegate assignment and a ref/out delegate write were treated as stable, hiding opaque factory writes | Medium | DI003/DI015 |
| Current | Delegate branch analysis treated block returns too broadly, allowing conditional nested returns to clear reachable factory values | Medium | DI003/DI015 |
| Current | Exhaustive `if` / `else if` / `else` local factory branches were walked twice, producing duplicate DI015 diagnostics for the same missing dependency request | Medium | DI015 |
| Current | Guard-clause branch exits before a definite delegate replacement kept stale initializer factories reachable, causing false positives | Low | DI003/DI015 |
| Current | Unawaited async local-function delegate writes before the first await were ignored, hiding synchronous factory replacements | Medium | DI003/DI015 |
| Current | Invoked anonymous delegates that reassigned local factories were ignored, keeping stale initializer factories reachable | Low | DI003/DI015 |
| Current | Anonymous delegate reassignments invoked through `.Invoke()` were ignored, keeping stale initializer factories reachable | Low | DI003/DI015 |
| Current | Delegate locals initialized from local-function method groups were ignored when invoked, keeping stale initializer factories reachable or hiding unsafe replacements | Medium | DI003/DI015 |
| Current | Throwing delegate branches were not treated as exits, keeping stale initializer factories reachable after all completing paths replaced them | Low | DI003/DI015 |
| Current | Directly invoked local functions with exhaustive branch delegate rewrites were abandoned, hiding unsafe branch factories | Medium | DI003/DI015 |
| Current | Exhaustive branch analysis treated unrelated assignment left-hand-side uses such as `metadata[factory] = ...` as factory writes, keeping stale initializer values reachable | Low | DI003/DI015 |
| Current | Nested branch delegate rewrites inside a branch that returned before registration were treated as reachable factory values | Medium | DI003/DI015 |
| Current | Identical method-group factory values from exhaustive branches could duplicate DI015 missing-dependency diagnostics | Low | DI015 |
| Current | Local-function branch returns after delegate reassignment were treated as exits before the caller's later registration, hiding reachable unsafe factory values | Medium | DI003/DI015 |
| Current | Reassigned helper delegate aliases were analyzed from their original initializer, hiding reachable unsafe or missing local factory initializers | Medium | DI003/DI015 |
| Current | Local-function guard returns inside exhaustive branch rewrites were treated as definite caller exits, hiding reachable prior factory values | Medium | DI003/DI015 |
| Current | Branch-local helper rewrites overwritten by a later direct factory assignment stayed reachable and produced stale DI003/DI015 diagnostics | Medium | DI003/DI015 |
| Current | Simple local delegate aliases such as `factory = inner` were not resolved recursively, hiding scoped captures or missing dependencies | Medium | DI003/DI015 |
| Current | Iterator local-function delegate rewrites were treated as executing at invocation time, hiding reachable scoped captures or missing dependencies left in the initializer | Medium | DI003/DI015 |
| Current | Branch-local helper rewrites on the only completing path failed to clear stale initializer values, causing false positives after every path reaching registration replaced the factory | Low | DI003/DI015 |
| Current | Unreachable `ref`/`out` delegate writes behind exits before registration made reachable initializer analysis opaque, hiding scoped captures or missing dependencies | Medium | DI003/DI015 |
| Current | No-op local delegate alias cycles such as `alias = factory; factory = alias` dropped the original reachable factory value, hiding scoped captures or missing dependencies | Medium | DI003/DI015 |
| Current | Short-circuit expression assignments such as `useSafe && ((factory = safe) != null)` were treated as definite replacements, hiding reachable unsafe or missing initializer factories | Medium | DI003/DI015 |
| Current | Switch-arm and `for` incrementor delegate assignments were treated as definite replacements, hiding reachable unsafe or missing initializer factories | Medium | DI003/DI015 |
| Current | Alias cycles after intervening factory writes used the current factory value instead of the alias snapshot, hiding unsafe snapshots or reporting stale missing values | Medium | DI003/DI015 |
| Current | DI001 accepted conditional or catch-only dispose calls as disposal proofs, suppressing real scope leaks | Medium | DI001 |
| Current | DI014 accepted conditional, catch-only, post-reassignment, repeated-loop, or branch-exit root-provider disposal as reliable disposal proof | Medium | DI014 |
| Current | DI001 treated conditionally assigned nullable scope locals as leaked when later conditional-access or non-null-guarded cleanup closed ownership | Low | DI001 |
| Current | DI005 missed `CreateScope()` in top-level programs that use `await`, leaving async disposal guidance silent in common minimal-hosting and console entry points | Medium | DI005 |
| Current | DI008 read non-generic `typeof` overload arguments by source order, missing out-of-order named `implementationType:` calls and risking keyed factory false positives | Medium | DI008 |
| Current | DI006 missed deferred static provider caches hidden behind `Lazy<IServiceProvider>`, `Lazy<IServiceScopeFactory>`, or `Lazy<IKeyedServiceProvider>` wrappers | Medium | DI006 |
| Current | Hidden framework registrations left `IOptionsSnapshot<T>` and EF Core `AddDbContext(...)` lifetimes invisible to DI003/DI019 and could make DI015 report normal `DbContextOptions<TContext>` constructor dependencies as missing | Medium | DI003/DI019/DI015 |
| Current | DI008 missed `services.Add(ServiceDescriptor.Transient<...>())`, `ServiceDescriptor.Describe(..., ServiceLifetime.Transient)`, and `new ServiceDescriptor(..., ServiceLifetime.Transient)` registrations because detection was scoped to `AddTransient`/`AddKeyedTransient` only | Medium | DI008 |
| Current | DI008 missed `TryAddTransient`, `TryAddKeyedTransient`, and `TryAddEnumerable(ServiceDescriptor.Transient<...>())` registrations on `ServiceCollectionDescriptorExtensions`, leaving common helper-extension shapes silent | Medium | DI008 |
| Current | DI006 missed nested provider wrappers (`Lazy<Task<IServiceProvider>>`, `Lazy<ValueTask<IKeyedServiceProvider>>`, `AsyncLocal<IServiceProvider>`, `ThreadLocal<IServiceScopeFactory>`, `Func<IServiceProvider>`) because the wrapper layer only matched a single-level `Lazy<provider>` | Medium | DI006 |
| Current | DI006 missed dictionary-of-providers caches (`Dictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ConcurrentDictionary<,>` whose `TValue` is a provider type) used as multi-tenant service-provider tables | Medium | DI006 |
| Current | DI007 only allowlisted middleware/factory contexts by method-name spelling, flagging legitimate service-locator inside `BackgroundService.ExecuteAsync`, `IHostedService.StartAsync`/`StopAsync`, `IConfigureOptions<T>.Configure`, `IPostConfigureOptions<T>`, and `IValidateOptions<T>` even when the surrounding type makes them factory boundaries | Medium | DI007 |
| Current | DI007 missed convention-based provider-factory delegates from third-party `IServiceCollection` extensions, treating any non-`ServiceCollectionServiceExtensions` `Add*` overload accepting `Func<IServiceProvider, T>` as a service-locator site rather than a factory boundary | Medium | DI007 |
| Current | DI008 initially treated generic `ServiceDescriptor.Transient<T>(factory)` calls as container-created transients because the generic descriptor path returned before inspecting factory arguments | Medium | DI008 |
| Current | DI008 initially missed `TryAddEnumerable(IEnumerable<ServiceDescriptor>)` descriptor collections such as arrays and `List<ServiceDescriptor>` initializers | Medium | DI008 |
| Current | DI007 initially suppressed local `Func<IServiceProvider, T>` delegates outside registration/options boundaries because delegate conversion alone was treated as a factory context | Medium | DI007 |
| Current | DI007 initially allowed any helper method named like a hosting/options method inside a hosted/options type instead of verifying the method actually implemented or overrode the framework contract | Medium | DI007 |
| Current | EF Core `AddDbContextFactory<TContext>()` registrations were invisible to DI003/DI019 and could make DI015 report normal `TContext`, `DbContextOptions<TContext>`, or `IDbContextFactory<TContext>` dependencies as missing | Medium | DI003/DI019/DI015 |
| Current | EF Core pooled registrations were invisible to DI003/DI019/DI015, and `AddDbContextFactory(..., ServiceLifetime.Transient)` was modeled as scoped, risking root-provider false positives and imprecise DI003 wording | Medium | DI003/DI019/DI015 |

## Importance Ranking

Which rules matter most *to catch*, independent of how healthy each currently is. Ranked by three factors: severity of the runtime consequence (silent data corruption > crash > leak > design smell), detectability without the analyzer (silent/intermittent bugs rank above fail-fast startup crashes, because fail-fast bugs get caught in dev anyway — the analyzer's value-add there is shifting discovery left, not preventing shipped bugs), and how commonly real codebases make the mistake.

### Tier 1 — Silent state corruption (highest stakes: wrong data, cross-request bleed, intermittent)

| Rank | Rule | Why it tops the list |
|------|------|----------------------|
| 1 | DI003 Captive Dependency | The flagship bug class of this package: a scoped service (typically a `DbContext`) captured by a singleton silently shares state across requests. No exception, no log line — just wrong data and cross-user leakage. Extremely common mistake. |
| 2 | DI021/DI022 Concurrent Handler Shared State | Concurrent use of a non-thread-safe service (DbContext, DbConnection) from message handlers/timers: intermittent corruption and `InvalidOperationException` only under production load. The hardest bug class on this list to diagnose after the fact. |
| 3 | DI019 Root Scoped Resolution | Scoped-from-root behaves as an accidental singleton (same corruption class as DI003) *and* the root provider holds every resolved disposable forever — silent unbounded memory growth. |
| 4 | DI020 Middleware Scoped Service | DI003's bug class specialized to middleware: a scoped service captured at pipeline-build time lives for the application lifetime. Common in real ASP.NET Core apps. |
| 5 | DI009 Open Generic Lifetime Mismatch | Same captive-dependency consequence as DI003 via the open-generic registration shape; rarer in practice, hence below its siblings. |

### Tier 2 — Resource leaks and disposal crashes (degrade over time, or crash on a specific path)

| Rank | Rule | Why |
|------|------|-----|
| 6 | DI004 Use After Dispose | `ObjectDisposedException` in production on the affected path; silent until that path runs. |
| 7 | DI014 Root Provider Not Disposed | Leaks an entire container graph per occurrence (worst per-instance leak); common in tests, tools, and factory misuse. |
| 8 | DI001 Scope Disposal | Leaks the scope's disposables; the most common leak shape day-to-day. |
| 9 | DI008 Disposable Transient | Root-resolved disposable transients are held by the container for its lifetime — slow, invisible memory growth. |
| 10 | DI002 Scope Escape | The precursor bug to DI004/DI001: a service smuggled past its scope boundary. Silent; consequence arrives later and far from the cause. |
| 11 | DI005 Async Disposal | Sync-over-async disposal: deadlock risk and lost async cleanup; narrower trigger than the rest of this tier. |

### Tier 3 — Fail-fast activation errors (real bugs, but runtime catches them loudly; analyzer value is shift-left)

| Rank | Rule | Why |
|------|------|-----|
| 12 | DI015 Unresolvable Dependency | Highest in this tier because resolution crashes on *rarely-executed paths* do ship to production — it is only fail-fast when the path runs. |
| 13 | DI013 Implementation Mismatch (Error) | Crashes at first resolution and is never correct code — which is exactly why it's the only Error-severity rule, and also why it rarely survives dev. Zero false positives is the non-negotiable here. |
| 14 | DI017 Circular Dependency | Exception at first resolution of the cycle; usually caught in dev unless the cycle is on a rare path. |
| 15 | DI018 Non-Instantiable Implementation | Activation failure, almost always caught immediately. |

### Tier 4 — Configuration/structure correctness smells

| Rank | Rule | Why |
|------|------|-----|
| 16 | DI016 BuildServiceProvider Misuse | Duplicate singleton universes and leaked providers — real consequences, but usually a code-review-visible pattern. |
| 17 | DI012 Conditional Registration | "Wrong service wins" surprises; consequence is usually functional and discoverable. |
| 18 | DI006 Static Provider Cache | Hidden global container state; primarily testability and lifetime hygiene. |

### Tier 5 — Design guidance (Info severity, by design)

| Rank | Rule |
|------|------|
| 19 | DI007 Service Locator |
| 20 | DI011 Service Provider Injection |
| 21 | DI010 Constructor Over-Injection |

## Work Priority (importance × health gap)

Work should go where a high-importance rule has a low honest score. Combining the ranking above with the re-audited scores:

| Priority | Target | Importance | Health | What to do |
|----------|--------|------------|--------|------------|
| 1 | DI020 hardening | Tier 1 (#4) | 9.0 | Shipped 2.10.4: all audit gaps covered, explicit-argument FP fixed, conditional-access detection added. Maintenance-only pending real-world feedback. |
| 2 | DI021/DI022 v2 | Tier 1 (#2) | 8.8 | RabbitMQ consumer sinks shipped in 2.10.1. Remaining v2 roadmap: PLINQ, TPL Dataflow, EventHubs batch sinks; scoped-lifetime DI022 tier via RegistrationCollector; same-tree helper-method knob proofs shipped 2.10.2 — remaining proof work is the RabbitMQ factory→connection→channel→consumer instance chain and cross-tree wiring; `IDbContextFactory<TContext>` second code action. |
| 3 | DI002 sink expansion | Tier 2 (#10) | 8.8 | Collection-mutation and event-subscription sinks shipped 2.10.3 (indexer was already covered and is now pinned). Remaining: tuple/anonymous-object composite returns — bounded and testable, lower frequency than the shipped shapes. |
| 4 | DI004 dispose-flow precision | Tier 2 (#6) | 8.9 | Shipped 2.10.5: mutually-exclusive-branch uses no longer report; branch-shape tests added. Field-stored scopes remain a separate, larger pass if cross-boundary tracking is ever taken on. |
| 5 | DI017 test density | Tier 3 (#14) | 8.8 | Cheapest item: this is test debt, not suspected wrongness. Add `Replace` mutation tests, keyed permutations (mixed keyed/unkeyed edges, int-vs-string keys), and a `Lazy<T>` parameter test pinning current behavior. |

DI003/DI019 (Tier 1, scores 9.8/9.7) stay maintenance-only: they earned their scores with 120/58 tests and repeated hardening; new work there should be feedback-driven, not speculative.

## Watchlist

| Item | Reason | Priority |
|------|--------|----------|
| EF/options real-world feedback | Watch for uncommon EF registration overload or options-lifetime shapes that need conservative modeling tweaks | Low |
| DI021 sink/catalog v2 expansion | PLINQ, TPL Dataflow, EventHubs batch sinks (RabbitMQ shipped 2.10.1); scoped-lifetime DI022 tier via RegistrationCollector; instance-correlated chain proofs (same-tree helper methods shipped 2.10.2) | Medium |
| DI022 default-on Info volume | DI022 fires on every config-bound ServiceBusProcessor capture by design; watch adoption feedback for whether default-on Info is the right noise budget | Medium |

## Recommended Next Actions

Ordered by the Work Priority table above:

1. **DI020 hardening pass** -- shipped 2.10.4 (all audit gaps covered, explicit-argument FP fixed, conditional-access detection added)
2. **DI021 v2 pass (continued)** -- PLINQ/Dataflow/EventHubs-batch sink rows (RabbitMQ shipped 2.10.1), scoped-lifetime DI022 tier, instance-correlated knob proofs, and an `IDbContextFactory<TContext>` second code action
3. **DI002 sink expansion** -- collection/indexer/event/composite-return escape sinks
4. **DI004 branch-aware explicit-dispose tracking** -- shipped 2.10.5 (mutually-exclusive branch skip)
5. **DI017 test-density pass** -- `Replace` mutation, keyed permutations, `Lazy<T>` behavior pin
6. **Watch EF/options real-world feedback** -- add only source-backed registration modeling tweaks that can be guarded across DI003, DI015, and DI019
