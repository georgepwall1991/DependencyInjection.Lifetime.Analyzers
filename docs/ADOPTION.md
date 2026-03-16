# Adoption Guide

This guide is for teams evaluating `DependencyInjection.Lifetime.Analyzers` for ASP.NET Core, worker-service, console, and library codebases that use `Microsoft.Extensions.DependencyInjection`.

## What It Helps Prevent

- Captive dependencies caused by singleton-to-scoped or singleton-to-transient lifetime mismatches.
- Scope lifetime bugs such as undisposed scopes, leaked scoped services, and use-after-dispose.
- Runtime activation failures caused by missing registrations or incompatible implementation types.
- DI composition problems such as `BuildServiceProvider()` misuse, duplicate/conditional registrations, and service locator patterns.

## Recommended Rollout

1. Install the package in one production application or shared solution layer.
2. Keep the default severities for the first pass so the team can see the real signal profile.
3. Promote the highest-confidence rules to `error` in `.editorconfig`.
4. Use CI to keep the agreed severity baseline enforced.
5. Open focused issues for false positives or false negatives with a minimal reproduction.

Starter `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error
dotnet_diagnostic.DI013.severity = error
dotnet_diagnostic.DI015.severity = warning
dotnet_diagnostic.DI016.severity = warning
dotnet_diagnostic.DI007.severity = suggestion
dotnet_diagnostic.DI011.severity = suggestion
```

## Good First Targets

- ASP.NET Core applications that create manual scopes in background services, jobs, or startup paths.
- Solutions that rely on `IServiceProvider` factories, keyed services, or `ActivatorUtilities`.
- Codebases with intermittent `ObjectDisposedException` or "Unable to resolve service for type ..." production failures.
- Teams that want analyzer-enforced DI standards in Rider, Visual Studio, and CI.

## Evaluation Checklist

- Run `dotnet build` after package installation and review emitted diagnostics.
- Confirm whether the reported diagnostics match your intended DI architecture.
- Apply available code fixes for disposal and lifetime rewrite cases.
- Adjust `.editorconfig` severities to match how aggressively you want to enforce each rule family.

## Useful Links

- [README](../README.md)
- [Full rule reference](./RULES.md)
- [NuGet package](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)

## Reporting Gaps

When opening an issue, include:

- the affected rule ID if known
- a minimal repro snippet
- package version
- SDK version
- whether the problem is a false positive, false negative, or missing code fix
