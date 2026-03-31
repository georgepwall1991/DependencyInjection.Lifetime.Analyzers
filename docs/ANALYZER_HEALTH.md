# Analyzer Health Snapshot

Date: 2026-03-31

Validation:
- `dotnet test DependencyInjection.Lifetime.Analyzers.sln`
- Result: `539/539` tests passing

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
| DI003 | Warning | 29 | 9/10 | No | One of the strongest runtime-correctness rules in the repo. Good graph-based implementation and strong direct coverage. |
| DI004 | Warning | 15 | 7/10 | No | Good baseline protection for use-after-dispose, but lifetime/path reasoning is inherently tricky and coverage is only mid-depth. |
| DI005 | Warning | 17 | 8/10 | No | Narrow rule with a clear trigger. Async methods, lambdas, local functions, and `IServiceProvider.CreateScope()` are covered well enough. |
| DI006 | Warning | 11 | 8/10 | No | Simple symbol rule with low ambiguity. Focused tests cover fields, properties, inherited provider types, and static classes. |
| DI007 | Info | 22 | 8/10 | No | Informational by design and already looks noise-hardened. Good factory and lambda handling for the current scope. |
| DI008 | Warning | 19 | 8/10 | No | Solid disposable transient coverage across generic registrations, `typeof`, `IDisposable`, and `IAsyncDisposable`. |
| DI009 | Warning | 10 | 7/10 | Yes | Important open-generic correctness rule, but the direct test surface is still light for a problem space that depends on constructor selection and registration shape. |
| DI010 | Info | 12 | 7/10 | No | Reasonable heuristic smell detector. Limited downside because it is info-only, and the current scope is covered well enough. |
| DI011 | Info | 17 | 8/10 | No | Good current state. Uses likely-activation-constructor logic instead of a blunt scan, and already has useful allowance cases. |
| DI012 | Info | 26 | 8/10 | No | Strong registration-history rule. `TryAdd`, duplicates, wrappers, keyed variants, and `ServiceDescriptor` shapes are covered. |
| DI013 | Error | 51 | 9/10 | No | Strong current state after the DI013 hardening pass. Open-generic projection checks, collector-fed registration shapes, and instance-backed mismatches now have broad direct coverage. |
| DI014 | Warning | 13 | 8/10 | No | Concrete lifetime rule with decent coverage across `using`, explicit dispose, fields, properties, returns, and shadowing. |
| DI015 | Warning | 51 | 9/10 | No | Best-developed analyzer in the repo right now. Broad support for keyed, factory, wrapper, and open-generic scenarios. |
| DI016 | Warning | 11 | 6/10 | Yes | Useful rule, but intentionally heuristic. Registration-context detection is shape-based, so false negatives are still plausible. |
| DI017 | Warning | 9 | 6/10 | Yes | High-value analyzer with comparatively low confidence. The graph walk is more complex than the current direct test surface suggests. |
| DI018 | Warning | 11 | 5/10 | Yes | Important activation-failure rule with an explicit blind spot around unbound generic constructor accessibility. |

## Suggested Pass Order

1. DI018
2. DI017
3. DI016
4. DI009
5. DI002

## Watchlist

- DI002: likely next in line after the top 5 if you want more precision around escape analysis.
- DI004: worth revisiting after the higher-value activation and graph rules are hardened.
- DI010: okay for now, but it would benefit from clearer policy if you ever want to move beyond a light info-level heuristic.
