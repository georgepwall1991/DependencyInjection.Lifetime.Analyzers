# Analyzer Health Snapshot

Date: 2026-04-01 (post-v2.4.3)

Validation:
- `dotnet test DependencyInjection.Lifetime.Analyzers.sln`
- Result: `629/629` tests passing

Scoring:
- `10/10` = strong implementation, strong tests, no obvious short-term hardening need.
- `7-8/10` = solid, but with some precision or coverage debt.
- `5-6/10` = important enough that a targeted hardening pass should be scheduled.

Scoring factors:
- runtime impact if the analyzer misses or misreports
- implementation complexity versus current direct test coverage
- whether recent hardening suggests the rule is already in a stable place

`Needs pass` means I would put explicit hardening time against the rule now rather than just keep it on a watchlist.

| ID | Severity | Rule tests | Score | Needs pass | Current state |
| --- | --- | --- | --- | --- | --- |
| DI001 | Warning | 23 | 8/10 | No | Mature disposal rule. Operation-based tracking covers lambdas, fields, conditionals, and nested scopes. |
| DI002 | Warning | 27 | 9/10 | No | Strong current state after the executable-boundary hardening pass. It now covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, and predeclared scopes without crossing nested executable boundaries. Remaining debt is mostly conservative sink breadth, not entry coverage. |
| DI003 | Warning | 31 | 9/10 | No | Strong runtime-correctness rule. Instance-backed registrations are now explicitly excluded from constructor analysis, and direct + `ServiceDescriptor` regressions are covered. |
| DI004 | Warning | 29 | 9/10 | No | Strong current state after the executable-boundary hardening pass. It now covers constructors, accessors, local functions, lambdas, anonymous methods, provider aliases, and predeclared scopes while keeping post-disposal reasoning inside the owning executable boundary. Remaining risk is broader dataflow complexity, not basic boundary coverage. |
| DI005 | Warning | 17 | 8/10 | No | Narrow rule with a clear trigger. Async methods, lambdas, local functions, and `IServiceProvider.CreateScope()` are covered well enough. |
| DI006 | Warning | 11 | 8/10 | No | Simple symbol rule with low ambiguity. Focused tests cover fields, properties, inherited provider types, and static classes. |
| DI007 | Info | 22 | 8/10 | No | Informational by design and already looks noise-hardened. Good factory and lambda handling for the current scope. |
| DI008 | Warning | 19 | 8/10 | No | Solid disposable transient coverage across generic registrations, `typeof`, `IDisposable`, and `IAsyncDisposable`. |
| DI009 | Warning | 22 | 9/10 | No | Strong current state after the constructor/collection hardening pass. It now handles optional/default-value constructor selection, ambiguous equally-greedy constructor silence, and `IEnumerable<T>` captures on top of the earlier `TryAddSingleton`, `ServiceDescriptor.Singleton`, keyed, and ineffective-`TryAdd` coverage. |
| DI010 | Info | 24 | 9.5/10 | No | Strong current state after the DI010 hardening pass. The rule now follows likely activation constructors, covers conservative factory paths, uses symbol-accurate exclusions, and supports `.editorconfig` threshold overrides. |
| DI011 | Info | 19 | 9/10 | No | Strong current state. Uses likely-activation-constructor logic, has good allowance coverage, and now stays quiet for valid implementation-instance registrations. |
| DI012 | Info | 30 | 9/10 | No | Strong current state after the flow/barrier hardening pass. It now follows same-collection aliases, source-defined helper/local-function wrappers, distinct object-created collection flows, keyed variants, `ServiceDescriptor` shapes, and opaque ordering barriers more reliably. |
| DI013 | Error | 51 | 9/10 | No | Strong current state after the DI013 hardening pass. Open-generic projection checks, collector-fed registration shapes, and instance-backed mismatches still have broad direct coverage. |
| DI014 | Warning | 13 | 8/10 | No | Concrete lifetime rule with decent coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. |
| DI015 | Warning | 53 | 9/10 | No | One of the strongest analyzers in the repo. Broad support for keyed, factory, wrapper, open-generic, and now implementation-instance scenarios. |
| DI016 | Warning | 18 | 9/10 | No | Strong current state after the builder-flow hardening pass. It now covers assignable `IServiceCollection` abstractions, same-boundary `.Services` aliases including later assignment, helper methods that forward builder-style `.Services` flows, and still keeps provider-factory and standalone top-level `ServiceCollection` guardrails. |
| DI017 | Warning | 11 | 8/10 | No | Much healthier now. Cycle detection uses stable effective registrations instead of concurrent discovery order, and mixed instance-plus-constructed graphs have direct coverage. |
| DI018 | Warning | 28 | 9/10 | No | Strong current state. Open-generic constructor checks now use the generic definition, and direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices, and sample/docs parity. |

## Suggested Pass Order

No urgent targeted hardening pass stands out after the 2026-04-01 post-v2.4.3 re-pass. The previous DI016, DI009, and DI012 priority set looks materially healthier now. If I had to force one anyway, I would start with DI001 or DI014 for remaining lifetime/disposal edge debt, then look at DI017 for more cycle-precision polish.

## Watchlist

- DI001: disposal-proof edge cases are still the most plausible place for future leak false positives or misses.
- DI014: root-provider ownership is a narrower rule, but explicit-disposal edge cases are still worth keeping on the radar.
- DI017: cycle detection is healthier now, but graph-shape precision and keyed-path breadth are the next likely refinement area.
