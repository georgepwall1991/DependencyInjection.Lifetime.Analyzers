---
name: analyzer-worker
description: Implement analyzer, code-fix, sample, and regression-test changes for DI rules and shared analysis infrastructure.
---

# Analyzer Worker

NOTE: Startup and cleanup are handled by `worker-base`. This skill defines the work procedure.

## When to Use This Skill

Use this skill for features that change:
- analyzer rule behavior
- shared Roslyn/DI infrastructure
- code fixes
- analyzer regression tests
- outward-facing samples required by public diagnostic behavior
- stress/performance regression tests for analyzer hot paths

## Required Skills

None.

## Work Procedure

1. Read `mission.md`, `AGENTS.md`, `.factory/library/architecture.md`, `.factory/library/sample-verification.md`, and `.factory/library/user-testing.md`.
2. Identify the exact rule IDs, shared infrastructure, and outward-facing surfaces touched by the feature.
3. Write failing tests first:
   - analyzer/code-fix regression tests before implementation
   - add/update sample-facing tests when the feature changes public diagnostic behavior
   - add/update stress/performance tests first when the feature touches shared hot paths
4. Implement the analyzer/infrastructure change only after the new tests fail for the intended reason.
5. If the feature changes a public diagnostic surface, update all required public touchpoints within scope:
   - `DiagnosticIds.cs`
   - `DiagnosticDescriptors.cs`
   - `AnalyzerReleases.Unshipped.md` / shipped tracking as appropriate
   - samples
   - sample/docs mappings or verifier expectations
6. Run focused validation during iteration:
   - targeted `dotnet test` filters for the changed rule/infrastructure
   - sample builds when public diagnostics or sample behavior are affected
7. Before handoff, run the repo validators from `.factory/services.yaml`:
   - `build`
   - `test`
   - `lint`
8. If the feature touched performance-sensitive code, run the performance/stress regression command required by the feature.
9. Leave no temp files in the repo and report any unresolved uncertainty or discovered drift explicitly.

## Example Handoff

```json
{
  "salientSummary": "Added DI017 cycle detection for direct/transitive constructor cycles, updated shared resolution logic conservatively around opaque factories, and added outward-facing sample coverage plus targeted regression tests.",
  "whatWasImplemented": "Introduced the new public diagnostic DI017, added analyzer tests for direct and transitive cycles plus no-diagnostic opaque-factory cases, updated the sample app and rule-surface wiring so the new diagnostic is outward-facing, and preserved existing DI015 conservative wrapper behavior.",
  "whatWasLeftUndone": "",
  "verification": {
    "commandsRun": [
      {
        "command": "dotnet test tests/DependencyInjection.Lifetime.Analyzers.Tests/DependencyInjection.Lifetime.Analyzers.Tests.csproj --configuration Release --filter \"FullyQualifiedName~DI017|FullyQualifiedName~DI015_UnresolvableDependencyAnalyzerTests\"",
        "exitCode": 0,
        "observation": "Targeted cycle and DI015 regression tests passed."
      },
      {
        "command": "dotnet build samples/SampleApp/SampleApp.csproj -t:Rebuild --configuration Release -p:RunAnalyzersDuringBuild=true",
        "exitCode": 0,
        "observation": "Sample app build completed and emitted the expected public diagnostics."
      },
      {
        "command": "dotnet build DependencyInjection.Lifetime.Analyzers.sln --no-restore --configuration Release && dotnet test DependencyInjection.Lifetime.Analyzers.sln --no-build --configuration Release --verbosity normal && node tools/generate-growth-assets.mjs sync-readme --check",
        "exitCode": 0,
        "observation": "Full repo validators passed."
      }
    ],
    "interactiveChecks": []
  },
  "tests": {
    "added": [
      {
        "file": "tests/DependencyInjection.Lifetime.Analyzers.Tests/Rules/DI017_CircularDependencyAnalyzerTests.cs",
        "cases": [
          {
            "name": "DirectConstructorCycle_ReportsDI017",
            "verifies": "Immediate constructor cycles produce the new public diagnostic."
          },
          {
            "name": "OpaqueFactoryCycle_NoDiagnostic",
            "verifies": "Cycle detection stays conservative when the path depends on opaque factories."
          }
        ]
      }
    ]
  },
  "discoveredIssues": [
    {
      "severity": "medium",
      "description": "Sample verifier expectations will need an explicit alias/secondary-diagnostic allowlist once DI017 is added to overlapping sample areas.",
      "suggestedFix": "Track secondary-diagnostic allowances in the sample verification manifest rather than hard-coding them in tests."
    }
  ]
}
```

## When to Return to Orchestrator

- The feature requires changing public diagnostic IDs/scope beyond what `mission.md` or the validation contract defines
- A public diagnostic change would require sample/docs policy decisions not already captured
- Shared infrastructure changes create a trade-off between correctness and false-positive risk that is not clearly resolved by the contract
- The required performance gate or validation surface is impossible to implement within current repo/tooling boundaries
