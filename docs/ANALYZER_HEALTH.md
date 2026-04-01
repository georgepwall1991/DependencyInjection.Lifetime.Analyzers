# Analyzer Health Snapshot

Date: 2026-04-01

Validation:
- `dotnet test DependencyInjection.Lifetime.Analyzers.sln`
- Result: `603/603` tests passing

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
| DI002 | Warning | 20 | 8/10 | No | 8/10 still feels right after the re-pass. The recent alias and predeclared-scope hardening landed well; the remaining debt is mainly the method-only entry surface and fairly syntax-shaped escape sinks. |
| DI003 | Warning | 31 | 9/10 | No | Strong runtime-correctness rule. Instance-backed registrations are now explicitly excluded from constructor analysis, and direct + `ServiceDescriptor` regressions are covered. |
| DI004 | Warning | 22 | 8/10 | No | 8/10 still feels right after the re-pass. The recent alias/predeclared-scope work and overwrite guardrails materially improved it; the remaining risk is mostly method-only coverage and nested-boundary/dataflow complexity. |
| DI005 | Warning | 17 | 8/10 | No | Narrow rule with a clear trigger. Async methods, lambdas, local functions, and `IServiceProvider.CreateScope()` are covered well enough. |
| DI006 | Warning | 11 | 8/10 | No | Simple symbol rule with low ambiguity. Focused tests cover fields, properties, inherited provider types, and static classes. |
| DI007 | Info | 22 | 8/10 | No | Informational by design and already looks noise-hardened. Good factory and lambda handling for the current scope. |
| DI008 | Warning | 19 | 8/10 | No | Solid disposable transient coverage across generic registrations, `typeof`, `IDisposable`, and `IAsyncDisposable`. |
| DI009 | Warning | 18 | 8/10 | No | Stronger now. Direct coverage includes `TryAddSingleton`, `ServiceDescriptor.Singleton`, keyed open-generic singleton paths, constructor-selection guardrails, and ineffective-`TryAdd` silence. |
| DI010 | Info | 24 | 9.5/10 | No | Strong current state after the DI010 hardening pass. The rule now follows likely activation constructors, covers conservative factory paths, uses symbol-accurate exclusions, and supports `.editorconfig` threshold overrides. |
| DI011 | Info | 19 | 9/10 | No | Strong current state. Uses likely-activation-constructor logic, has good allowance coverage, and now stays quiet for valid implementation-instance registrations. |
| DI012 | Info | 26 | 8/10 | No | Strong registration-history rule. `TryAdd`, duplicates, wrappers, keyed variants, and `ServiceDescriptor` shapes are covered. |
| DI013 | Error | 51 | 9/10 | No | Strong current state after the DI013 hardening pass. Open-generic projection checks, collector-fed registration shapes, and instance-backed mismatches still have broad direct coverage. |
| DI014 | Warning | 13 | 8/10 | No | Concrete lifetime rule with decent coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. |
| DI015 | Warning | 53 | 9/10 | No | One of the strongest analyzers in the repo. Broad support for keyed, factory, wrapper, open-generic, and now implementation-instance scenarios. |
| DI016 | Warning | 14 | 8/10 | No | Improved after the DI016 hardening pass. Top-level builder-style `.Services` registration flows are now covered, standalone top-level `ServiceCollection` usage has a no-diagnostic guardrail, and direct regression coverage is in better shape. |
| DI017 | Warning | 11 | 8/10 | No | Much healthier now. Cycle detection uses stable effective registrations instead of concurrent discovery order, and mixed instance-plus-constructed graphs have direct coverage. |
| DI018 | Warning | 28 | 9/10 | No | Strong current state. Open-generic constructor checks now use the generic definition, and direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices, and sample/docs parity. |

## Suggested Pass Order

No urgent targeted hardening pass stands out after the 2026-04-01 re-pass. If I had to force one anyway, I would start with a combined DI002/DI004 entry-surface widening pass, then revisit DI016 if heuristic registration-context drift shows up again, then DI009 if constructor-selection-sensitive open-generic coverage turns out to have more edge cases.

## Watchlist

- DI002: materially healthier now, but it still only enters through ordinary methods and mainly recognizes direct return/field/property-style escape sinks.
- DI004: materially healthier now, but it still only enters through ordinary methods and its post-disposal reasoning is inherently sensitive to nested boundaries and broader cross-block flow.
- DI016: materially healthier now, but still a heuristic rule, so keep an eye on future registration-shape drift rather than scheduling an immediate pass.
- DI009: current registration-shape coverage is much better, but constructor-selection-sensitive open-generic behavior is still worth watching over time.
