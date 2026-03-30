# Architecture

High-level system shape for `DependencyInjection.Lifetime.Analyzers`.

**What belongs here:** major components, relationships, data flow, public rule surfaces, mission-specific architectural boundaries.
**What does NOT belong here:** line-by-line algorithm notes or exhaustive code listings.

---

## Current State

### Analyzer package
- Path: `src/DependencyInjection.Lifetime.Analyzers`
- Contains:
  - `Rules/` — one analyzer per diagnostic family
  - `CodeFixes/` — safe rewrite/fix providers
  - `Infrastructure/` — shared registration/dependency/lifetime analysis
  - `DiagnosticIds.cs` / `DiagnosticDescriptors.cs` — public rule inventory and metadata

### Shared analysis infrastructure
- `RegistrationCollector`
  - Extracts DI registrations from `IServiceCollection` calls and supported `ServiceDescriptor` shapes
- `WellKnownTypes`
  - Caches important DI/Roslyn symbols
- `ConstructorSelection`
  - Chooses constructors/activation candidates for analyzer reasoning
- `FactoryAnalysis` / `FactoryDependencyAnalysis`
  - Extract supported dependency requests from factory registrations
- `DependencyResolutionEngine`
  - Resolves DI dependency graphs conservatively
- `ServiceCollectionReachabilityAnalyzer`
  - Limits DI015-style analysis to reachable registration flows

### Tests
- Path: `tests/DependencyInjection.Lifetime.Analyzers.Tests`
- Current strengths:
  - focused analyzer/unit-style tests
  - code-fix tests
  - DI015 heavy regression coverage
- Mission additions are expected to live here unless a dedicated verifier project becomes clearly better

### Samples
- Path: `samples/`
- Roles:
  - `SampleApp` = outward-facing sample matrix
  - `DI015InAction` = focused broken-vs-fixed unresolved-dependency sample

### Docs / asset generation
- Path: `tools/generate-growth-assets.mjs`
- Owns README install snippets, rule/problem pages, and sample-backed content generation
- Mission-critical because sample/docs drift is part of the desired validation surface

## Mission Target State

- Add repo-owned sample diagnostics verification that consumes real sample-project SARIF output
- Add public diagnostics planned by this mission:
  - `DI017` — cycle detection
  - `DI018` — non-instantiable implementation / constructibility failures
- Add a canonical validation gate that includes analyzer tests, sample diagnostics verification, and sample/docs freshness checks
- Add an automated stress/performance regression gate for shared hot paths touched by the mission

## Data Flow

1. Source code invokes analyzer rules during build/test.
2. Rules consume shared infrastructure to reason about registrations, scopes, factories, and dependency graphs.
3. Tests validate individual rules and cross-rule behavior.
4. Sample projects provide outward-facing proof of rule behavior.
5. Node generation tooling maps rule/sample content into README/docs outputs.
6. Mission work adds sample diagnostics verification so sample truth is validated from real builds instead of manual expectations.

## Mission-Level Architectural Decisions

- New coverage-expansion work in this mission is treated as **public diagnostics**:
  - `DI017` — cycle detection
  - `DI018` — non-instantiable implementation / constructibility failures
- Outward-facing diagnostics must be represented across:
  - analyzer tests
  - samples
  - sample/docs generation inputs
- Public diagnostic work must also update release tracking files:
  - `AnalyzerReleases.Unshipped.md`
  - `AnalyzerReleases.Shipped.md` when appropriate for the release flow
- Sample verification should be implemented as repo-owned automation, defaulting to the existing test/tooling surface unless a dedicated helper project is justified by SARIF or fixture needs
- Performance work should target shared infrastructure hot paths and be backed by an automated stress/performance regression gate
- Workers should read companion guidance:
  - `.factory/library/sample-verification.md`
  - `.factory/library/user-testing.md`

## Invariants

- Prefer conservative diagnostics over speculative ones
- Avoid duplicate diagnostics for one underlying invalid registration path
- Keep sample verification build-based and CI-safe
- Keep repo validation runnable with normal `dotnet` and `node` commands
