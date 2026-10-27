# Mutation testing

The repository's mutation gate is deliberately scoped to two high-risk surfaces: the shared
test-code suppressor and the shared resolution helpers. Both keep a break threshold of **80%**.
The threshold is not a parameter in the runner; lowering it to make a change pass is not allowed.

```powershell
pwsh ./scripts/Invoke-MutationTests.ps1
pwsh ./scripts/Invoke-MutationTests.ps1 -ConfigFile stryker-config.helpers.json
```

The runner restores the pinned `dotnet-stryker` tool from `.config/dotnet-tools.json`, uses
`stryker-config.json` for the suppressor and `stryker-config.helpers.json` for the helpers, and
fails when either score drops below 80%. Reports are written under `StrykerOutput/`.

## Current result

| Iteration | Score | What changed |
| --- | ---: | --- |
| 1 | 61.68% | Initial suppressor tests |
| 2 | 87.39% | Convention matrix for paths, assemblies, types, namespaces, attributes, and `IsDiRule` |
| 3 | 92.23% | Removed redundant assembly/namespace arms and added `TryGetSuppression` tests |
| 4 | 95.10% | Split equivalent guards and added a generic-namespace fallback test |
| 5 | 80.00% | Added the shared-resolution-helper shard (`ImplementationAssignability`, `ConstructorSelection`, `KeyedServiceHelpers`) |
| 6 | 94.92% | Restored the full three-file helper surface, added direct tests for assignability, constructor selection, and keyed-service extraction, and refactored the nullable check into explicit guards |

The helper shard clears the 80% threshold at 94.92%. Its three survivors are equivalent: the two empty-candidate early returns in `ConstructorSelection` return the same empty sequence either way, and `KeyedServiceHelpers`'s inherited-key flag only changes `IsUnknown`, which the two-value overload discards. The assignability surface alone scores 100% in a focused run.

The remaining suppressor survivors are equivalent mutants: the `getInnermostNodeForTie` flag for token-sized
locations, and the `"test"`/`"acceptance"` fast-path strings where `IndexOf("")` has the same
observable result as the real substring. The two no-coverage mutants are inside
`ReportSuppressions`, whose behavior is covered end-to-end by
`Scanner_SuppressesFindingsInTestProjects` in the scanner test suite.

## Shards

The gate runs as two independent shards in `.github/workflows/mutation.yml`:

| Shard | Config | Surface |
| --- | --- | --- |
| Suppression | `stryker-config.json` | `TestCodeSuppressor` |
| Shared resolution helpers | `stryker-config.helpers.json` | `ImplementationAssignability`, `ConstructorSelection`, `KeyedServiceHelpers` |

Both shards keep the same 80% break threshold. Running them separately avoids one 26,000-mutant
run that never finishes while still covering the shared helpers the rules depend on.

## Why this scope

The analyzer project creates more than 26,000 mutants when every rule and shared helper is in scope,
which makes a full run too slow for a routine gate. The rule analyzers already have per-rule test
suites and adversarial notes. The mutation gate focuses on the new shared helper that every rule
depends on, where a silent regression would affect the whole package. To run a broader mutation
surface, add files to the `mutate` array in the relevant Stryker config; keep the `break` threshold at 80.
