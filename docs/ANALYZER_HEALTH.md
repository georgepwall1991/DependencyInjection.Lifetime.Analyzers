# Analyzer Health Snapshot

Date: 2026-03-31

Validation:
- `dotnet test DependencyInjection.Lifetime.Analyzers.sln`
- Result: `568/568` tests passing

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
| DI002 | Warning | 14 | 7/10 | No | Useful scope-escape rule, but escape analysis is still dataflow-heavy and only moderately covered. Worth watching, not a top priority. |
| DI003 | Warning | 31 | 9/10 | No | Strong runtime-correctness rule. Instance-backed registrations are now explicitly excluded from constructor analysis, and direct + `ServiceDescriptor` regressions are covered. |
| DI004 | Warning | 15 | 7/10 | No | Good baseline protection for use-after-dispose, but lifetime/path reasoning is inherently tricky and coverage is only mid-depth. |
| DI005 | Warning | 17 | 8/10 | No | Narrow rule with a clear trigger. Async methods, lambdas, local functions, and `IServiceProvider.CreateScope()` are covered well enough. |
| DI006 | Warning | 11 | 8/10 | No | Simple symbol rule with low ambiguity. Focused tests cover fields, properties, inherited provider types, and static classes. |
| DI007 | Info | 22 | 8/10 | No | Informational by design and already looks noise-hardened. Good factory and lambda handling for the current scope. |
| DI008 | Warning | 19 | 8/10 | No | Solid disposable transient coverage across generic registrations, `typeof`, `IDisposable`, and `IAsyncDisposable`. |
| DI009 | Warning | 10 | 7/10 | Yes | Important open-generic correctness rule, but the direct test surface is still light for a problem space that depends on constructor selection and registration shape. |
| DI010 | Info | 13 | 7/10 | No | Reasonable heuristic smell detector. Limited downside because it is info-only, and instance-backed registrations are now guarded explicitly. |
| DI011 | Info | 19 | 9/10 | No | Strong current state. Uses likely-activation-constructor logic, has good allowance coverage, and now stays quiet for valid implementation-instance registrations. |
| DI012 | Info | 26 | 8/10 | No | Strong registration-history rule. `TryAdd`, duplicates, wrappers, keyed variants, and `ServiceDescriptor` shapes are covered. |
| DI013 | Error | 51 | 9/10 | No | Strong current state after the DI013 hardening pass. Open-generic projection checks, collector-fed registration shapes, and instance-backed mismatches still have broad direct coverage. |
| DI014 | Warning | 13 | 8/10 | No | Concrete lifetime rule with decent coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. |
| DI015 | Warning | 53 | 9/10 | No | One of the strongest analyzers in the repo. Broad support for keyed, factory, wrapper, open-generic, and now implementation-instance scenarios. |
| DI016 | Warning | 11 | 6/10 | Yes | Useful rule, but intentionally heuristic. Registration-context detection is shape-based, so false negatives are still plausible. |
| DI017 | Warning | 11 | 8/10 | No | Much healthier now. Cycle detection uses stable effective registrations instead of concurrent discovery order, and mixed instance-plus-constructed graphs have direct coverage. |
| DI018 | Warning | 28 | 9/10 | No | Strong current state. Open-generic constructor checks now use the generic definition, and direct coverage spans keyed registrations, `TryAdd`, `ServiceDescriptor.Singleton`/`Describe`, factory and instance silence, constructor accessibility matrices, and sample/docs parity. |

## Suggested Pass Order

1. DI016
2. DI009
3. DI002
4. DI004

## Watchlist

- DI002: likely next in line after the top 5 if you want more precision around escape analysis.
- DI004: worth revisiting after the higher-value activation and registration-shape rules are hardened.
- DI010: okay for now, but it would benefit from clearer policy if you ever want to move beyond a light info-level heuristic.
