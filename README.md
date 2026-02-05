<p align="center">
  <img src="logo.png" alt="DependencyInjection.Lifetime.Analyzers" width="512" height="512">
</p>

# DependencyInjection.Lifetime.Analyzers

Compile-time analyzers for `Microsoft.Extensions.DependencyInjection` lifetime and scope correctness.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CI](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)
[![Coverage](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/raw/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)

## Why Use This

- Catch DI lifetime bugs before runtime failures.
- No runtime overhead.
- Includes code fixes for high-frequency issues.
- Works in Visual Studio, Rider, VS Code, and CI.

## 2-Minute Quickstart

1. Install the package:

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers
```

2. Build once (`dotnet build`) to validate analyzer integration.

3. Set severities in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error
dotnet_diagnostic.DI007.severity = suggestion
```

## Rules At A Glance

| ID | Title | Severity | Code Fix |
|----|-------|----------|----------|
| DI001 | Service scope not disposed | Warning | Yes |
| DI002 | Scoped service escapes scope | Warning | Yes |
| DI003 | Captive dependency | Warning | Yes |
| DI004 | Service used after scope disposed | Warning | No |
| DI005 | Use CreateAsyncScope in async methods | Warning | Yes |
| DI006 | Static ServiceProvider cache | Warning | Yes |
| DI007 | Service locator anti-pattern | Warning | No |
| DI008 | Disposable transient service | Warning | Yes |
| DI009 | Open generic captive dependency | Warning | Yes |
| DI010 | Constructor over-injection | Info | No |
| DI011 | ServiceProvider injection | Warning | No |
| DI012 | Conditional registration misuse | Info | No |
| DI013 | Implementation type mismatch | Error | No |
| DI014 | Root service provider not disposed | Warning | Yes |
| DI015 | Unresolvable dependency | Warning | No |

Detailed examples and fixes: [Rule Reference](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/blob/master/docs/RULES.md)

## Samples

- `samples/SampleApp`: one diagnostics folder per rule (`DI001` through `DI015`).
- `samples/DI015InAction`: focused runnable sample for unresolved dependency diagnostics.

## Configuration

Suppress a specific rule in code:

```csharp
#pragma warning disable DI007
var service = _provider.GetRequiredService<IMyService>();
#pragma warning restore DI007
```

Or via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI007.severity = none
```

DI015 strict mode (disable framework-service assumptions):

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

## Requirements

- `.NET Standard 2.0` consumer compatibility
- `Microsoft.Extensions.DependencyInjection`

## Known Limitations

- Compile-time analysis only (runtime registrations are not inspectable).
- Cross-assembly registration graphs are not fully tracked.
- Lifetime inference follows standard single-service resolution (`GetService<T>`/`GetRequiredService<T>`) and may not model every multi-registration `IEnumerable<T>` activation path.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT License - see [LICENSE](LICENSE).
