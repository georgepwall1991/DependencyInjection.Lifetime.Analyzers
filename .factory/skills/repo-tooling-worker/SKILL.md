---
name: repo-tooling-worker
description: Implement sample verification, docs-generation checks, CI wiring, and other repository tooling changes for the analyzer package.
---

# Repo Tooling Worker

NOTE: Startup and cleanup are handled by `worker-base`. This skill defines the work procedure.

## When to Use This Skill

Use this skill for features that change:
- sample diagnostics verification
- sample/docs freshness checks
- Node generation tooling
- CI/workflow validation wiring
- helper projects or test helpers that exist to validate repo surfaces

## Required Skills

None.

## Work Procedure

1. Read `mission.md`, `AGENTS.md`, `.factory/library/sample-verification.md`, `.factory/library/user-testing.md`, and `.factory/library/architecture.md`.
2. Identify the maintainer-facing workflow that must change:
   - sample verification
   - docs/readme freshness
   - canonical validation gate
   - CI/workflow command wiring
3. Write failing verification first:
   - add failing tests/helpers/verifier cases before implementation
   - if adding a helper project, keep it repo-local and CI-safe
4. Implement the tooling change with clean temp-file behavior:
   - use SARIF rather than console scraping for diagnostic truth
   - use stable anchors/allowlists rather than brittle raw line checks where possible
   - keep temp outputs outside the repo or auto-cleaned
5. If the change affects docs/sample generation, update the owning tooling and any checked-in mapping/config the verifier depends on.
6. Validate the new tooling in the same way maintainers/CI will use it:
   - targeted verifier/tests
   - sample build flows
   - root repo validators from `.factory/services.yaml`
7. Confirm the canonical validation path is documented by code/config, not just by one-off commands.

## Example Handoff

```json
{
  "salientSummary": "Added repo-owned sample diagnostics verification, wired it into the normal validation path, and made sample-backed docs checks fail loudly on drift instead of silently dropping missing snippets.",
  "whatWasImplemented": "Created a sample diagnostics verifier that rebuilds SampleApp and DI015InAction with SARIF output, matches claimed diagnostics by stable anchors and severity, supports approved secondary diagnostics, and integrated the verifier plus sample/docs freshness checks into the repo's standard validation commands and CI wiring.",
  "whatWasLeftUndone": "",
  "verification": {
    "commandsRun": [
      {
        "command": "dotnet test tests/DependencyInjection.Lifetime.Analyzers.Tests/DependencyInjection.Lifetime.Analyzers.Tests.csproj --configuration Release --filter \"FullyQualifiedName~SampleDiagnosticsVerifier\"",
        "exitCode": 0,
        "observation": "Verifier-specific regression suite passed."
      },
      {
        "command": "node tools/generate-growth-assets.mjs sync-readme --check",
        "exitCode": 0,
        "observation": "README/docs generation check passed."
      },
      {
        "command": "dotnet build DependencyInjection.Lifetime.Analyzers.sln --no-restore --configuration Release && dotnet test DependencyInjection.Lifetime.Analyzers.sln --no-build --configuration Release --verbosity normal",
        "exitCode": 0,
        "observation": "Root validation path passed with sample verification integrated."
      }
    ],
    "interactiveChecks": []
  },
  "tests": {
    "added": [
      {
        "file": "tests/DependencyInjection.Lifetime.Analyzers.Tests/Infrastructure/SampleDiagnosticsVerifierTests.cs",
        "cases": [
          {
            "name": "SampleApp_ClaimedDiagnosticsMatchSarif",
            "verifies": "Claimed sample diagnostics are matched against real SARIF output."
          },
          {
            "name": "MissingSampleHighlight_FailsFreshnessCheck",
            "verifies": "Sample-backed docs drift fails loudly instead of being silently dropped."
          }
        ]
      }
    ]
  },
  "discoveredIssues": []
}
```

## When to Return to Orchestrator

- The verifier contract requires a product/policy decision about public diagnostic ownership, approved aliases, or approved secondary diagnostics
- CI/workflow changes would conflict with mission boundaries or require external services
- A tooling feature depends on analyzer behavior that has not been implemented yet and would force brittle temporary assumptions
