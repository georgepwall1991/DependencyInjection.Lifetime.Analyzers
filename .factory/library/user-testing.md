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
- Canonical local gate commands:
  - `node tools/generate-growth-assets.mjs sync-readme --check`
  - `node tools/generate-growth-assets.mjs check-freshness`
  - `node tools/generate-growth-assets.mjs site --output-dir ./artifacts/site`
- Mission expectation: generated-site validation is part of the same maintainer-facing local gate as README/sample freshness

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

## Flow Validator Guidance: command-line validation

- Isolation boundary: use the shared checkout at `/Users/georgewall/RiderProjects/DependencyInjection.Lifetime.Analyzers` only; do not create extra worktrees for this milestone.
- Concurrency: shared-checkout command-line validators must run one at a time because `dotnet build` and `dotnet test` contend on shared `bin/obj` outputs.
- Allowed tools: shell commands (`dotnet`, `node`, `python3`, `mktemp`) and file reads/writes needed for flow reports and evidence only.
- Temp outputs: create SARIF and generated-site outputs in system temp directories or the assigned mission evidence directory, not under tracked repo paths.
- Scope: validate repo commands and sample/docs flows only; do not start long-running services, open ports, or use `dotnet run`.
- Evidence expectations: record the exact commands run, their exit codes, and the key observed output proving each assigned assertion passed or failed.
