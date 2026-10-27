# Scanning a repository with `di-scan`

`di-scan` builds any .NET solution or project with the
`DependencyInjection.Lifetime.Analyzers` analyzers injected. It does not edit the target's project
files. It writes a SARIF report, prints the rules that fired with the surrounding source, and can
also write a Markdown summary or a self-contained HTML triage page.

Use it to try the analyzers on a real codebase before adding the package, compare two releases, or
review a suspected false positive without opening the solution.

## Install and run

With the .NET 10 SDK, run the tool without installing it:

```bash
dnx DependencyInjection.Lifetime.Analyzers.Scan -- MyApp.slnx
```

With the .NET 8 or 9 SDK, install it as a global tool:

```bash
dotnet tool install -g DependencyInjection.Lifetime.Analyzers.Scan
di-scan MyApp.slnx
```

The scanner is also built from this repository:

```bash
dotnet build src/DependencyInjection.Lifetime.Analyzers.Scan/DependencyInjection.Lifetime.Analyzers.Scan.csproj -c Release
dotnet src/DependencyInjection.Lifetime.Analyzers.Scan/bin/Release/net8.0/DependencyInjection.Lifetime.Analyzers.Scan.dll MyApp.slnx
```

## What the output contains

- The rule ID, severity, title, and count for every rule that fired.
- `path:line:column` for each finding, the diagnostic message, and the surrounding source.
- A link to the rule page in `docs/rules/`.
- A SARIF 2.1.0 report for GitHub code scanning or any SARIF viewer.
- Optional Markdown and HTML reports for triage.

The scanner ignores diagnostics the compiler considers suppressed, so `#pragma` and
`[SuppressMessage]` behave exactly as they do in a normal build. Diagnostics in test code are
suppressed by the analyzer's `TestCodeSuppressor`, using test directories, test project names, test
type and namespace names, test base types, and test attributes.

## Common workflows

### Review every finding in one rule

```bash
di-scan ./MyApp.slnx --rules DI003 --findings all --context 4 --html di003.html
```

### Fail CI only on new findings

Generate a baseline once, commit it, and let CI fail only when a new finding appears:

```bash
di-scan ./MyApp.slnx --fail-on warning --sarif di.sarif
di-scan ./MyApp.slnx --fail-on warning --baseline di.sarif --sarif di-new.sarif
```

A SARIF baseline matches by fingerprint, so a finding that moves down a file stays known. A text
baseline matches by rule and exact location; it is the format the pinned corpus uses.

### Exclude generated code or vendored sources

```bash
di-scan ./MyApp.slnx --exclude '**/Migrations/**' --exclude 'third-party/**'
```

A glob without `/` matches a file or folder name anywhere, such as `Migrations` or
`*.Designer.cs`.

### Use it in GitHub Actions

```yaml
- name: Scan DI lifetimes
  run: |
    dnx DependencyInjection.Lifetime.Analyzers.Scan -- ./MyApp.slnx \
      --fail-on warning --sarif di.sarif --summary di.md
```

In GitHub Actions the scanner also appends the Markdown report to the job summary and annotates the
most severe findings on the pull request's changed lines. Pass `--no-github` to skip that.

## Profiles

The package ships starter `.editorconfig` profiles under `profiles/`:

| Profile | Use it when |
| --- | --- |
| `default.editorconfig` | You want each rule at its shipped default severity. |
| `brownfield-adoption.editorconfig` | You are adding the analyzer to a large existing codebase and want composition failures first. |
| `strict-ci.editorconfig` | Every high-confidence rule should fail CI; informational rules stay warnings. |
| `library-author.editorconfig` | You ship a library and want activation and composition failures to be errors. |

The profiles are generated from the analyzer descriptors by
`node tools/generate-rule-assets.mjs`; do not hand-edit them.

## Related documentation

- [Scanner README](../src/DependencyInjection.Lifetime.Analyzers.Scan/README.md)
- [Rule reference](rules/README.md)
- [Scan regression corpus](scan-regression.md), run with `pwsh ./scripts/Invoke-ScanRegression.ps1`
