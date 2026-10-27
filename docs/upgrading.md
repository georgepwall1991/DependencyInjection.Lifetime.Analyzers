# Upgrading

The analyzer package follows SemVer. Diagnostic IDs, default severities, and code-fix behavior are
the stable contract; releases can still remove findings when a rule is narrowed to avoid a proven
false positive. This page records the behavior changes that can move a build from green to clean,
or from noisy to quiet.

## 3.9.x

### 3.9.0 — DI001 and DI019 scope wrappers

- **DI001** treats a scope wrapped in a directly returned object as caller-owned, and recognizes the
  canonical `if (scope is IAsyncDisposable alias) await alias.DisposeAsync(); else scope.Dispose();`
  shape plus scopes stored in a field and disposed by the owning type.
- **DI019** no longer treats a custom wrapper's `ServiceProvider` property as the root provider. Only
  the singleton's own or inherited provider properties, known host properties, and real scope
  providers qualify.

If a previously reported finding disappeared, check whether the scope is now proven caller-owned or
the provider is a custom wrapper. If the new result is wrong, open a false-negative report.

### 3.10.0 — scanner UX and maintenance

- `di-scan --fix` applies safe code fixes with `dotnet format`.
- `di-scan --update-baseline <file>` writes a text baseline that `--baseline` reads back.
- Windows/macOS scanner CI, false-positive/negative issue forms, Dependabot, CODEOWNERS, and a
  second mutation shard.
- No analyzer behavior changes.

## 3.9.1–3.9.3 — packaging and discoverability

No analyzer behavior changes. These releases shipped the concise NuGet README, the scanner docs
page, NuGet metadata, and GitHub topics.

## 3.8.0 — scanner, profiles, and test-code suppression

- **Test code is suppressed** using the HttpClient analyzer conventions: test project names,
  directories, type/namespace names, base types, and test attributes. Findings in test projects
  disappear by design. If a production project is named or laid out like a test project, override
  the suppressor with a narrow `#pragma` or an `.editorconfig` severity.
- **DI001** recognizes conditional async disposal and field-owned scopes.
- **DI006** skips custom, stateless `IServiceProvider` null-object providers.
- **DI015** skips type parameters and closed generics containing them, and treats any registered
  `DbContextOptions<TContext>` as satisfying a non-generic `DbContextOptions` dependency.
- The package now ships starter profiles under `profiles/` and one page per rule under
  `docs/rules/`.

## Keeping the previous behavior

Pin the last version that matched your policy:

```xml
<PackageReference Include="DependencyInjection.Lifetime.Analyzers" Version="3.7.8">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Or keep the newer analyzer and lower one rule:

```ini
[*.cs]
dotnet_diagnostic.DI015.severity = none
```

For DI015's framework-service assumption, use the documented option:

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

## Regenerating a scanner baseline

When a reviewed behavior change intentionally removes findings, regenerate only the affected
baseline and review the diff:

```bash
di-scan ./MyApp.slnx --update-baseline di.baseline
git diff di.baseline
```

For the pinned repository corpus:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines
git diff tests/scan-corpus/baselines
```

Never hand-edit a baseline to make a failing scan pass.

## Cross-project composition

DI015 analyzes one compilation at a time. A library cannot see a registration added by a downstream
web project, so a partial composition root can report a missing dependency that the application
actually registers. Run the scanner against the composition-root project, or lower DI015 for the
library, and record the target as a partial composition root.

## Reporting a change

Use the dedicated [false-positive](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/issues/new?template=false-positive.yml)
or [false-negative](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/issues/new?template=false-negative.yml)
form. Include the rule ID, package or scanner version, the smallest repro, and the expected lifetime
or activation result.
