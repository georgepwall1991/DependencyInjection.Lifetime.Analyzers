---
name: new-analyzer-rule
description: Add a new DIxxx rule to this analyzer repo end to end — analyzer, tests, sample, contract claims, docs, release tracking, and the version/tag cadence. Use when adding, splitting, or retiring a diagnostic ID here. Not for hardening an existing rule (that is analyzer-hardening-loop) or auditing the health document (analyzer-health-judge).
---

# Adding a Rule to DependencyInjection.Lifetime.Analyzers

A new rule is eight artifacts, not one file. The parity tests fail loudly for each one you miss, but they fail *after* a full test run, so working the checklist up front is much faster than discovering them one at a time.

## Before writing anything

**Prove the gap.** Check `docs/RULES.md` and `DiagnosticDescriptors.cs` for an existing rule that already reports the shape. Overlaps found the hard way in 3.0.2–3.5.0:

- DI008 owns **transient** disposables. A disposal rule that also fires on transients double-reports (DI032 was restricted to singleton/scoped for exactly this).
- DI012 owns duplicates of the **same** service type; a different service type with the same implementation is a separate claim (DI031).
- DI021 owns `Parallel.*` bodies and framework message handlers; `Task.WhenAll` projections were left unclaimed (DI035).
- DI023 needs a `using` scope in the method. Anything torn down on someone else's schedule (a request context) is not covered by it (DI034).

Record the boundary decision in `docs/ANALYZER_HEALTH.md` so a later pass does not re-litigate it.

**Pick the severity against the noise budget.** Warning = a defect that will bite (a runtime throw, a leak, a crash). Info = a claim that is conditional or occasionally deliberate. `DiagnosticDescriptorSeverityTests` enforces that **every descriptor sharing one ID has the same severity** — a Warning tier and an Info tier need two IDs, never two descriptors under one.

## The eight artifacts

1. `src/.../Rules/DIxxx_NameAnalyzer.cs` — the analyzer.
2. `src/.../DiagnosticIds.cs` — the `const string`, with a doc comment.
3. `src/.../DiagnosticDescriptors.cs` — the descriptor. Add `customTags: WellKnownDiagnosticTags.CompilationEnd` if you report from a compilation-end action.
4. `tests/.../Rules/DIxxx_NameAnalyzerTests.cs` — positive **and** negative tests.
5. `tests/.../DiagnosticDescriptorSeverityTests.cs` — the `["DIxxx", DiagnosticSeverity.X]` row.
6. `src/.../AnalyzerReleases.Unshipped.md` — the new-rule row. **Also rotate the previous release's rule from Unshipped into Shipped** under its `## Release x.y.z` heading; this is the single most-missed step.
7. `samples/SampleApp/Diagnostics/DIxxx/…Examples.cs` plus a `folderClaims` entry in `samples/SampleApp/sample-diagnostics-contract.json`, and the `DIxxx` block in `tools/generate-growth-assets.mjs`.
8. `docs/RULES.md` (index row **and** a full section) plus the README index row.

## Sample and contract gotchas

The sample app is compiled and every diagnostic it raises must be claimed:

- **Secondary diagnostics need explicit approval.** A sample resolving through `scope.ServiceProvider` raises DI007; injecting `IServiceScopeFactory` raises DI011. Add them to `approvedSecondaryDiagnostics` with the anchor line, or the contract test fails.
- **Info severity is `"note"`** in claim entries, not `"info"`.
- **An `async` lambda that calls `CreateScope` raises DI005.** Use `await using … CreateAsyncScope()` in "good" examples.
- **Reuse existing framework stubs.** `HttpContext` is already declared beside the DI020 sample; re-declaring it is a CS0101 build break. Grep `samples/SampleApp` before adding a stub, and add only the missing member.
- The anchor must match the sample source **exactly**, so update the contract if you edit the sample line.

## Verification loop

LSP diagnostics are unreliable in this repo (phantom CS0234/CS0246 on every file) — ignore them and gate on the real build:

```bash
TMPDIR=$(pwd)/.tmp-test dotnet test --filter "FullyQualifiedName~DIxxx"   # tight loop
TMPDIR=$(pwd)/.tmp-test dotnet test                                       # full suite before commit
TMPDIR=$(pwd)/.tmp-test dotnet build -c Release                           # RS1030/NU1903 only show here
```

Always set `TMPDIR` to a repo-local path; the shared temp package cache corrupts and produces 1000+ phantom failures. A lone `PerformanceRegressionTests` timeout under load is flaky — rerun it in isolation before investigating.

**Edit existing `.cs` files through a Bash python script, never Edit/Write** — the format hook reformats the whole file and buries the diff. New files are fine to Write. Write the file inside each patch step so a later assertion cannot silently discard earlier edits.

**netstandard2.0 constrains the analyzer project**: list patterns (`[x]`) need `System.Index` and do not compile. Use `block.Statements.Count == 1 && block.Statements[0] is …`.

## Review protocol

Run `codex exec --sandbox read-only` against **the newest commit only** (`git show HEAD`), scoped to the analyzer file, with `Do NOT run the test suite` — an unscoped review times out and, when auto-backgrounded, dies silently producing an empty output file.

Each round: fix every **false positive**, add a regression test for each, then re-review — fixes introduce new bugs, so a clean round only counts after the last fix. **Stop when a round returns no false positives**, not when it returns nothing; findings that are purely false-*negative* on exotic shapes get documented in the CHANGELOG next to the rule and merged.

Verify a reviewer's runtime claims against real MEDI semantics rather than accepting or dismissing them — the DI032 "factories are excluded" finding was correct (the container tracks and disposes what a factory returns) and reversed a design decision.

**Mutation-test your guards.** A passing `VerifyNoDiagnosticsAsync` proves nothing until deleting the guard makes that exact test fail. See the `mutation-test-negative-guards` memory.

## Release

Version lives in **four** places: `<Version>` in the csproj and three README install snippets (`--version`, `Version="x"`, `Version="x" />`). CI validates the tag against the csproj and validates README sync, so a missed snippet fails the release build.

Then: CHANGELOG entry (including accepted false negatives) → PR → wait for CI green → squash merge → `git tag vX.Y.Z && git push origin vX.Y.Z`. **Push tags one at a time** — pushing more than three at once silently triggers no workflows.
