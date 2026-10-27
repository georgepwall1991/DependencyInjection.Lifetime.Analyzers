<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/main/icon.png" width="96" height="96" alt="DependencyInjection.Lifetime.Analyzers icon">
</p>

# DependencyInjection.Lifetime.Analyzers

**Catch dependency injection lifetime bugs at compile time — before they become `ObjectDisposedException`, stale singletons, or production-only activation failures.**

A Roslyn analyzer for `Microsoft.Extensions.DependencyInjection` in ASP.NET Core, worker services, the generic host, and .NET libraries. It adds **no runtime dependency** and stays quiet when a bug cannot be proven from the compilation.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![Docs](https://img.shields.io/badge/docs-georgepwall1991.github.io-6b3fa0.svg)](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/)
[![CI](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

[Rule catalog](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/rules/) · [Problem guides](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/problems/) · [Scanner](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/scanner/) · [GitHub](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers)

## Install

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers
```

```xml
<PackageReference Include="DependencyInjection.Lifetime.Analyzers" Version="*">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

No runtime package is added to your app. The analyzer runs in Visual Studio, Rider, and `dotnet build` / CI.

## What it catches

- **Captive dependencies** — a singleton captures a scoped or transient service (`DI003`, `DI009` for open generics).
- **Scope leaks and disposal** — undisposed `IServiceScope`, root providers, use-after-dispose, async scope misuse (`DI001`, `DI004`, `DI005`, `DI014`).
- **Scope escape and root resolution** — scoped services leaving their scope or resolved from the root provider (`DI002`, `DI019`).
- **Activation failures** — missing registrations, implementation mismatches, non-instantiable types, circular graphs (`DI013`, `DI015`, `DI017`, `DI018`).
- **`BuildServiceProvider()` misuse** during composition (`DI016`).
- **Service locator drift** — static provider caches and direct `IServiceProvider` use (`DI006`, `DI007`, `DI011`).
- **Host and ASP.NET Core hazards** — middleware scoped capture, hosted-service scope-per-iteration, `HttpClient` lifetime, fire-and-forget scope/`HttpContext` capture (`DI020`, `DI023`, `DI024`, `DI029`, `DI034`).
- **Concurrency and leak families** — shared non-thread-safe services, event/Rx/callback subscription leaks, unbounded singleton caches (`DI021`–`DI022`, `DI025`–`DI028`, `DI030`, `DI035`).

**38 shipped rules.** Many include code fixes. See the [full rule catalog](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/rules/) for severity, bad and safe examples, detection boundaries, and suppression.

## 30-second example

```csharp
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton<ISingletonService, SingletonService>();

public sealed class SingletonService : ISingletonService
{
    // DI003: the scoped service is captured for the process lifetime.
    public SingletonService(IScopedService scoped) { }
}
```

The safe shape is to make the consumer scoped, or inject `IServiceScopeFactory` and create a scope per operation.

## Try it on a real repository first

`di-scan` builds any .NET solution with these analyzers injected — without editing its project files — and reports `ruleId path:line:column`, the surrounding source, SARIF, Markdown, and HTML.

```bash
dnx DependencyInjection.Lifetime.Analyzers.Scan -- MyApp.slnx
```

```bash
dotnet tool install -g DependencyInjection.Lifetime.Analyzers.Scan
di-scan MyApp.slnx --fail-on warning --sarif di.sarif
```

Use `--baseline` to fail only on new findings. See the [scanner guide](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/scanner/) for output formats, CI examples, and baselines.

## Starter profiles

The package ships four `.editorconfig` profiles under `profiles/`:

| Profile | Use it when |
| --- | --- |
| `default.editorconfig` | You want each rule at its shipped default severity. |
| `brownfield-adoption.editorconfig` | You are adding the analyzer to a large existing codebase and want composition failures first. |
| `strict-ci.editorconfig` | Every high-confidence rule should fail CI; informational rules stay warnings. |
| `library-author.editorconfig` | You ship a library and want activation and composition failures to be errors. |

## Why it stays quiet

- Framework types are resolved by symbol, never by type name alone.
- Custom APIs that reuse DI names are not treated as `Microsoft.Extensions.DependencyInjection` evidence.
- Test code is suppressed using test project names, directories, type/namespace names, base types, and test attributes.
- When the analyzer cannot prove a bug from registrations, constructors, and scope usage, it does not report.

## Proof, not just a rule list

- **3,128 tests**
- **14 pinned public repositories** and **471 findings** in the scan regression corpus
- **95.10%** mutation score on the shared test-code suppression helper, with an 80% gate
- Precision fixes driven by real repositories, including MassTransit, Elsa, IdentityServer, Polly, and dotnet-podcasts

The corpus and triage notes are in [docs/scan-regression.md](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/blob/main/docs/scan-regression.md).

## Compatibility

- **Target:** projects using `Microsoft.Extensions.DependencyInjection` — ASP.NET Core, generic host, workers, console apps, and libraries.
- **Analyzer host:** Roslyn in Visual Studio, Rider, and `dotnet build` / CI.
- **Package TFM:** `netstandard2.0` analyzer assembly.
- **Language:** C#.
- **Not claimed:** third-party containers unless they use the same registration APIs in a way the analyzer can see.

## Links

- [Documentation](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/)
- [Rule catalog](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/rules/)
- [Problem guides](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/problems/)
- [Scanner](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/scanner/)
- [Adoption guide](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/adoption/)
- [GitHub repository](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers)
- [Changelog](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/blob/main/CHANGELOG.md)
