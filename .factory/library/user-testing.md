# User Testing

Testing surfaces, tools, and validation concurrency for this mission.

**What belongs here:** testable surfaces, required validation commands, environment caveats, concurrency guidance.
**What does NOT belong here:** implementation details of analyzers or code-fix logic.

---

## Validation Surface

### Surface: Solution validation
- Tool: `dotnet`
- Primary command: `dotnet test DependencyInjection.Lifetime.Analyzers.sln --configuration Release`
- Purpose: full analyzer/code-fix/unit regression coverage

### Surface: Sample diagnostics verification
- Tool: `dotnet build` plus SARIF-consuming verifier
- Primary targets:
  - `samples/SampleApp/SampleApp.csproj`
  - `samples/DI015InAction/DI015InAction.csproj`
- Purpose: verify outward-facing sample diagnostics, broken-vs-fixed scenarios, and drift detection
- Constraint: prefer `dotnet build` and SARIF over `dotnet run`

### Surface: Docs/readme freshness
- Tool: `node`
- Current validated command: `node tools/generate-growth-assets.mjs sync-readme --check`
- Mission expectation: sample-backed docs freshness checks become part of the same validation gate

## Validation Concurrency

### Shared checkout
- Max concurrent validators: `1`
- Reason: concurrent `dotnet build/test` on the same checkout can contend on shared `bin/obj` outputs

### Isolated worktrees/checkouts
- Max concurrent validators: `4`
- Basis:
  - CPU headroom after dry run supports `4` safe concurrent validators
  - Observed heaviest validation flow used roughly `200 MiB RSS`
  - 70% headroom rule applied during planning

## Dry Run Notes

- `dotnet test DependencyInjection.Lifetime.Analyzers.sln --configuration Release` passed during planning.
- Sample-project SARIF emission succeeded during planning.
- `node tools/generate-growth-assets.mjs sync-readme --check` passed during planning.
- No web/browser validation surface is needed for this mission.
