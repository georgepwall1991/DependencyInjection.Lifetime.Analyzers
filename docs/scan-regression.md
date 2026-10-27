# Scan regression corpus

`tests/scan-corpus/corpus.json` pins public repositories that genuinely use
`Microsoft.Extensions.DependencyInjection`. Every entry is checked out at a full commit SHA, so
analyzer changes — not upstream drift — move the baseline. The harness
[`scripts/Invoke-ScanRegression.ps1`](../scripts/Invoke-ScanRegression.ps1) scans each target and
fails when a finding appears, disappears, or the scanner exit code changes.

The baselines are a regression signal, not a correctness oracle. A diff means behavior moved;
decide whether the move is correct before regenerating.

## Current corpus

| Repository | Target | Findings | Why it is here |
| --- | --- | ---: | --- |
| `dotnet-architecture/eShopOnWeb` | `eShopOnWeb.sln` | 7 | ASP.NET Core reference app with scopes, hosted services, and Blazor admin |
| `App-vNext/Polly` | `Polly.slnx` | 2 | Resilience library with DI extensions; docs snippets and benchmarks excluded |
| `DuendeSoftware/IdentityServer` | `bff/src/Bff/Bff.csproj` | 37 | Framework BFF composition; partial composition root by design |
| `Xabaril/AspNetCore.Diagnostics.HealthChecks` | `AspNetCore.Diagnostics.HealthChecks.sln` | 63 | Large health-check library with many DI extension methods |
| `dapr/dotnet-sdk` | `src/Dapr.Actors/Dapr.Actors.csproj` | 1 | Actor SDK; project graph covers Dapr.Client and Dapr.Common |
| `microsoft/dotnet-podcasts` | `NetPodcast.Services.sln` | 4 | APIs, workers, and shared service defaults |
| `openiddict/openiddict-core` | `src/OpenIddict.Client/OpenIddict.Client.csproj` | 55 | Framework library with DI registrations and options |
| `ThreeMammals/Ocelot` | `Ocelot.slnx` | 37 | ASP.NET Core gateway with registrations and middleware; benchmark excluded |
| `ardalis/CleanArchitecture` | `Clean.Architecture.slnx` | 7 | Clean Architecture app template with scoped services |
| `MassTransit/MassTransit` | `src/MassTransit/MassTransit.csproj` | 90 | Generic `Bind<TBus, T>` wrappers stress open-generic and type-parameter handling |
| `khellang/Scrutor` | `Scrutor.sln` | 0 | Clean baseline: a DI scanning library that does not trip the rules |
| `Autofac/Autofac.Extensions.DependencyInjection` | `Autofac.Extensions.DependencyInjection.sln` | 2 | Alternate-container bridge; guards against treating Autofac APIs as MEDI evidence |
| `jasontaylordev/CleanArchitecture` | `CleanArchitecture.slnx` | 6 | Full Web/Application/Infrastructure solution; cross-method composition and third-party helpers |
| `elsa-workflows/elsa-core` | `core/src/modules/Elsa.Workflows.Runtime/Elsa.Workflows.Runtime.csproj` | 160 | Workflow runtime; exercises custom scope wrappers and feature-based registration |

The manifest records per-repository excludes and their reason. Current excludes:

| Repository | Exclude | Reason |
| --- | --- | --- |
| `App-vNext/Polly` | `Snippets`, `bench` | Documentation snippets and benchmark-only composition are not a production composition root. |
| `ThreeMammals/Ocelot` | `benchmark` | Benchmark-only composition is not the production gateway. |

There are no wholly excluded repositories at this commit.

## Triage history

The corpus is not just a scoreboard; each drift review either fixed a rule or recorded why the
shape is intentional.

| Shape | Result |
| --- | --- |
| Test projects in Polly and HealthChecks | Added `TestCodeSuppressor`; test-code findings are suppressed by project name, directory, type/namespace, base type, and test attributes. |
| MassTransit `TBus` / `Bind<TBus, …>` missing dependencies | DI015 now skips type parameters and closed generics containing them. |
| dotnet-podcasts `DbContextOptions` base dependency | DI015 now treats any registered `DbContextOptions<T>` as satisfying the non-generic `DbContextOptions` base type. |
| MassTransit scopes disposed through `if (scope is IAsyncDisposable …) … else scope.Dispose()` | DI001 now recognizes the canonical conditional async-dispose shape. |
| MassTransit `FilterScopeProvider` field-owned scope | DI001 now recognizes a scope stored in a field and disposed by the owning type's `Dispose`/`DisposeAsync`. |
| MassTransit stateless `new EmptyServiceProvider()` static field | DI006 now skips custom provider initializers with no instance state; stateful custom providers still report. |
| Elsa `tenantScope.ServiceProvider` custom wrapper | DI019 now treats only the singleton's own/inherited provider properties, known host properties, and real scope providers as root evidence. |
| Elsa `return new TenantScope(scope, …)` | DI001 now treats a scope wrapped in a directly returned object as caller-owned. |
| Polly docs snippets and benchmarks, Ocelot benchmarks | Excluded from the production baseline; recorded in the manifest and baseline header. |

Known intentional baseline noise:

- **DI003 transient captures** in stateless accessors/controllers are design-level lifetime warnings; the analyzer cannot prove statelessness, so it keeps reporting them.
- **DI015 partial composition roots** (IdentityServer BFF) and third-party dependencies (HealthChecks
  InfluxDB/Datadog) are recorded as baseline signal, not proof of a missing app registration.
- **DI001 ownership transfer** to a scope-context factory (`BaseConsumeScopeProvider`) remains a
  documented false positive; the scope's owner is outside the analyzed method.
- **DI032 async-only singleton disposables** are warnings for apps that dispose the provider
  synchronously; a host that calls `DisposeAsync` is safe.

## Run it

Build the scanner and scan every pinned repository:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1
```

Run selected entries:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1 -Name App-vNext_Polly -Name ThreeMammals_Ocelot
```

Reuse an existing clone cache or an existing scanner build:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1 -NoBuild -CachePath ./artifacts/scan-corpus/repos
```

Regenerate the affected baselines after a reviewed behavior change:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines
git diff tests/scan-corpus/baselines
```

Never hand-edit a baseline to make a failing scan pass.

## Baseline format

Each file starts with the repository, commit, target, and scanner exit code, followed by one finding
per line:

```text
# DI scan regression baseline. Regenerate with scripts/Invoke-ScanRegression.ps1 -UpdateBaselines.
# repository: App-vNext/Polly
# commit: 4c58a76cbcae3d43d2cf2198246335d8c4313c7e
# target: Polly.slnx
# exit: 0
DI014 bench/Polly.Core.Benchmarks/ResiliencePipelineProviderBenchmark.cs:11:13
DI016 src/Polly.Extensions/DependencyInjection/PollyServiceCollectionExtensions.cs:42:17
```

Paths are relative to the repository root. The harness compares the finding set exactly; it does
not compare ordering or severity.

## Triage policy

For every new, missing, or changed finding:

1. **False positive** — narrow the rule. Add a regression test for the shape. Re-run the corpus;
   only the affected baselines should move.
2. **False negative** — extend detection only when the failure is provable from the compilation.
   Add a test for the positive shape and a test for the nearest lookalike.
3. **True positive** — keep it and document the pattern in the rule page or its adversarial notes.
4. **Partial composition root** — a project that is only one part of a larger composition can report
   dependencies registered by referenced projects. Record that in the manifest `notes`; do not
   weaken the rule for every caller.
5. **Test code** — test projects are suppressed by `TestCodeSuppressor`. If a test finding remains,
   check the test project name, directory, namespace, base type, and attributes before changing the
   suppressor.

After a reviewed change, regenerate only the baselines that moved, and explain in the commit which
shape moved and why the new result is correct.

## CI

The corpus is too network- and build-heavy for every pull request. Run it on a schedule and on
demand with the `Scan regression` workflow (`.github/workflows/scan-regression.yml`). The workflow
builds the scanner, restores the cache, scans all pinned repositories, and fails on any drift.
