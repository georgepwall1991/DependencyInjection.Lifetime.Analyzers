# Environment

Environment variables, external dependencies, and setup notes.

**What belongs here:** required toolchains, local constraints, setup notes, external dependencies.
**What does NOT belong here:** service ports/commands (use `.factory/services.yaml`).

---

- Repository type: .NET Roslyn analyzer package with Node-based asset generation.
- Required toolchains:
  - `.NET SDK` compatible with `global.json` (`10.0.102` with `rollForward: latestFeature`; `10.0.201` validated locally)
  - `Node` for `tools/generate-growth-assets.mjs`
- No external credentials, databases, queues, or web services are required.
- Local environment constraint:
  - `.NET 8` runtime is not installed locally during planning. `net8.0` sample projects build successfully, but worker validation should prefer `dotnet build` over `dotnet run` for sample verification.
- Sample verification should use temporary directories and temporary SARIF outputs rather than writing transient files into the repo.
