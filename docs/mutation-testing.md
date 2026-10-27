# Mutation testing

The repository's mutation gate is deliberately scoped to the newest shared helper,
`TestCodeSuppressor`, and keeps a break threshold of **80%**. The threshold is not a parameter in
the runner; lowering it to make a change pass is not allowed.

```powershell
pwsh ./scripts/Invoke-MutationTests.ps1
```

The runner restores the pinned `dotnet-stryker` tool from `.config/dotnet-tools.json`, uses
`stryker-config.json`, and fails when the score drops below 80%. Reports are written under
`StrykerOutput/`.

## Current result

| Iteration | Score | What changed |
| --- | ---: | --- |
| 1 | 61.68% | Initial suppressor tests |
| 2 | 87.39% | Convention matrix for paths, assemblies, types, namespaces, attributes, and `IsDiRule` |
| 3 | 92.23% | Removed redundant assembly/namespace arms and added `TryGetSuppression` tests |
| 4 | 95.10% | Split equivalent guards and added a generic-namespace fallback test |

The remaining survivors are equivalent mutants: the `getInnermostNodeForTie` flag for token-sized
locations, and the `"test"`/`"acceptance"` fast-path strings where `IndexOf("")` has the same
observable result as the real substring. The two no-coverage mutants are inside
`ReportSuppressions`, whose behavior is covered end-to-end by
`Scanner_SuppressesFindingsInTestProjects` in the scanner test suite.

## Why this scope

The analyzer project creates more than 26,000 mutants when every rule and shared helper is in scope,
which makes a full run too slow for a routine gate. The rule analyzers already have per-rule test
suites and adversarial notes. The mutation gate focuses on the new shared helper that every rule
depends on, where a silent regression would affect the whole package. To run a broader mutation
surface, add files to the `mutate` array in `stryker-config.json`; keep the `break` threshold at 80.
