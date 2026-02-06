<p align="center">
  <img src="logo.png" alt="DependencyInjection.Lifetime.Analyzers - C# dependency injection lifetime analyser" width="512" height="512">
</p>

# DependencyInjection.Lifetime.Analyzers

**A Roslyn dependency injection analyser package for C# and ASP.NET Core lifetime bugs.**

Catch DI scope leaks, captive dependencies, and unresolvable services at compile time with zero runtime overhead.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CI](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)
[![Coverage](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/raw/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)

## Why This DI Lifetime Analyser

`DependencyInjection.Lifetime.Analyzers` helps teams using `Microsoft.Extensions.DependencyInjection` avoid common production issues:

- `ObjectDisposedException` from invalid scope usage.
- Memory leaks from undisposed scopes or root providers.
- Captive dependency bugs caused by incorrect service lifetimes.
- Hidden service locator usage that weakens testability.
- Runtime activation failures from missing or incompatible registrations.

This analyser package is designed for **ASP.NET Core**, **worker services**, **console apps**, and **CI pipelines** that need dependable dependency injection rules.

## Quickstart

Install from NuGet:

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers
```

Set useful severities in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error
dotnet_diagnostic.DI013.severity = error
dotnet_diagnostic.DI007.severity = suggestion
```

## Table of Contents

- [Why This DI Lifetime Analyser](#why-this-di-lifetime-analyser)
- [Quickstart](#quickstart)
- [Rule Index](#rule-index)
- [DI001: Service Scope Not Disposed](#di001-service-scope-not-disposed)
- [DI002: Scoped Service Escapes Scope](#di002-scoped-service-escapes-scope)
- [DI003: Captive Dependency](#di003-captive-dependency)
- [DI004: Service Used After Scope Disposed](#di004-service-used-after-scope-disposed)
- [DI005: Use `CreateAsyncScope` in Async Methods](#di005-use-createasyncscope-in-async-methods)
- [DI006: Static `IServiceProvider` Cache](#di006-static-iserviceprovider-cache)
- [DI007: Service Locator Anti-Pattern](#di007-service-locator-anti-pattern)
- [DI008: Disposable Transient Service](#di008-disposable-transient-service)
- [DI009: Open Generic Captive Dependency](#di009-open-generic-captive-dependency)
- [DI010: Constructor Over-Injection](#di010-constructor-over-injection)
- [DI011: `IServiceProvider` Injection](#di011-iserviceprovider-injection)
- [DI012: Conditional Registration Misuse](#di012-conditional-registration-misuse)
- [DI013: Implementation Type Mismatch](#di013-implementation-type-mismatch)
- [DI014: Root Service Provider Not Disposed](#di014-root-service-provider-not-disposed)
- [DI015: Unresolvable Dependency](#di015-unresolvable-dependency)
- [DI016: BuildServiceProvider Misuse](#di016-buildserviceprovider-misuse)
- [Configuration](#configuration)
- [Frequently Asked Questions](#frequently-asked-questions)

## Rule Index

| ID | Title | Default Severity | Code Fix |
|----|-------|------------------|----------|
| [DI001](#di001-service-scope-not-disposed) | Service scope not disposed | Warning | Yes |
| [DI002](#di002-scoped-service-escapes-scope) | Scoped service escapes scope | Warning | Yes |
| [DI003](#di003-captive-dependency) | Captive dependency | Warning | Yes |
| [DI004](#di004-service-used-after-scope-disposed) | Service used after scope disposed | Warning | No |
| [DI005](#di005-use-createasyncscope-in-async-methods) | Use `CreateAsyncScope` in async methods | Warning | Yes |
| [DI006](#di006-static-iserviceprovider-cache) | Static `IServiceProvider` cache | Warning | Yes |
| [DI007](#di007-service-locator-anti-pattern) | Service locator anti-pattern | Warning | No |
| [DI008](#di008-disposable-transient-service) | Disposable transient service | Warning | Yes |
| [DI009](#di009-open-generic-captive-dependency) | Open generic captive dependency | Warning | Yes |
| [DI010](#di010-constructor-over-injection) | Constructor over-injection | Info | No |
| [DI011](#di011-iserviceprovider-injection) | `IServiceProvider` injection | Warning | No |
| [DI012](#di012-conditional-registration-misuse) | Conditional/duplicate registration misuse | Info | No |
| [DI013](#di013-implementation-type-mismatch) | Implementation type mismatch | Error | No |
| [DI014](#di014-root-service-provider-not-disposed) | Root provider not disposed | Warning | Yes |
| [DI015](#di015-unresolvable-dependency) | Unresolvable dependency | Warning | No |
| [DI016](#di016-buildserviceprovider-misuse) | BuildServiceProvider misuse during registration | Warning | No |

---

## DI001: Service Scope Not Disposed

**What it catches:** `IServiceScope` instances created with `CreateScope()` or `CreateAsyncScope()` that are never disposed.

**Why it matters:** undisposed scopes can retain scoped and transient disposable services longer than expected, causing memory and handle leaks.

> **Explain Like I'm Ten:** If you borrow a paintbrush and never wash it, it dries out and ruins the next project.

**Problem:**

```csharp
public void Process()
{
    var scope = _scopeFactory.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<IMyService>();
    svc.Run();
}
```

**Better pattern:**

```csharp
public void Process()
{
    using var scope = _scopeFactory.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<IMyService>();
    svc.Run();
}
```

**Code Fix:** Yes. Adds `using` / `await using` where possible.

---

## DI002: Scoped Service Escapes Scope

**What it catches:** a service resolved from a scope that is returned or stored somewhere longer-lived.

**Why it matters:** once the scope is disposed, that service may point to disposed state.

> **Explain Like I'm Ten:** It is like taking an ice cube out of the freezer for later; by the time you need it, it has melted.

**Problem:**

```csharp
public IMyService GetService()
{
    using var scope = _scopeFactory.CreateScope();
    return scope.ServiceProvider.GetRequiredService<IMyService>();
}
```

**Better pattern:**

```csharp
public void UseServiceNow()
{
    using var scope = _scopeFactory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute();
}
```

**Code Fix:** Yes (suppression and acknowledgement options where direct refactor is not safe).

---

## DI003: Captive Dependency

**What it catches:** longer-lived services (especially singleton) capturing shorter-lived dependencies (scoped/transient).

**Why it matters:** lifetime mismatch can produce stale state, leaks, and thread-safety defects.

> **Explain Like I'm Ten:** If one pupil keeps the shared class scissors all term, nobody else can use them when needed.

**Problem:**

```csharp
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton<ISingletonService, SingletonService>();

public sealed class SingletonService : ISingletonService
{
    public SingletonService(IScopedService scoped) { }
}
```

**Better pattern:**

```csharp
services.AddScoped<ISingletonService, SingletonService>();

// or keep singleton and create scopes inside operations
public sealed class SingletonService : ISingletonService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SingletonService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Run()
    {
        using var scope = _scopeFactory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<IScopedService>();
        scoped.DoWork();
    }
}
```

**Code Fix:** Yes. Can adjust lifetime registration where supported.

---

## DI004: Service Used After Scope Disposed

**What it catches:** using a service after the scope that produced it has already ended.

**Why it matters:** leads to runtime disposal errors and brittle service behaviour.

> **Explain Like I'm Ten:** It is like trying to turn on a torch after you removed the batteries.

**Problem:**

```csharp
IMyService service;
using (var scope = _scopeFactory.CreateScope())
{
    service = scope.ServiceProvider.GetRequiredService<IMyService>();
}
service.DoWork();
```

**Better pattern:**

```csharp
using (var scope = _scopeFactory.CreateScope())
{
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.DoWork();
}
```

**Code Fix:** No. Usually needs manual refactor.

---

## DI005: Use `CreateAsyncScope` in Async Methods

**What it catches:** `CreateScope()` used in async flows where async disposal is needed.

**Why it matters:** async disposables (`IAsyncDisposable`) may not be cleaned up correctly with sync disposal patterns.

> **Explain Like I'm Ten:** If a machine needs a proper shutdown button, pulling the plug is not enough.

**Problem:**

```csharp
public async Task RunAsync()
{
    using var scope = _scopeFactory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}
```

**Better pattern:**

```csharp
public async Task RunAsync()
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}
```

**Code Fix:** Yes. Rewrites scope creation/disposal pattern.

---

## DI006: Static `IServiceProvider` Cache

**What it catches:** `IServiceProvider` / `IServiceScopeFactory` / keyed provider stored in static fields or properties.

**Why it matters:** global provider state encourages service locator use and muddles lifetime boundaries.

> **Explain Like I'm Ten:** Leaving the school master key in the corridor means anybody can open any door at any time.

**Problem:**

```csharp
public static class Locator
{
    public static IServiceProvider Provider { get; set; } = null!;
}
```

**Better pattern:**

```csharp
public sealed class Locator
{
    private readonly IServiceProvider _provider;

    public Locator(IServiceProvider provider)
    {
        _provider = provider;
    }
}
```

**Code Fix:** Yes. Removes `static` modifier in common cases.

---

## DI007: Service Locator Anti-Pattern

**What it catches:** resolving dependencies via `IServiceProvider` inside app logic.

**Why it matters:** hides real dependencies, makes tests harder, and weakens architecture boundaries.

> **Explain Like I'm Ten:** If every meal starts with "search the kitchen and see what turns up", dinner becomes chaos.

**Problem:**

```csharp
public sealed class MyService
{
    private readonly IServiceProvider _provider;

    public MyService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void Run()
    {
        var dep = _provider.GetRequiredService<IDependency>();
        dep.Execute();
    }
}
```

**Better pattern:**

```csharp
public sealed class MyService
{
    private readonly IDependency _dependency;

    public MyService(IDependency dependency)
    {
        _dependency = dependency;
    }

    public void Run() => _dependency.Execute();
}
```

**Code Fix:** No. This is usually architectural refactoring.

---

## DI008: Disposable Transient Service

**What it catches:** transient services implementing `IDisposable`/`IAsyncDisposable` in risky patterns.

**Why it matters:** disposal ownership can become unclear and resources may be leaked.

> **Explain Like I'm Ten:** Borrowing a bike every minute without returning the old one fills the whole bike shed.

**Problem:**

```csharp
services.AddTransient<IMyService, DisposableService>();

public sealed class DisposableService : IMyService, IDisposable
{
    public void Dispose() { }
}
```

**Better pattern:**

```csharp
services.AddScoped<IMyService, DisposableService>();
// or ensure explicit disposal ownership if transient is intentional
```

**Code Fix:** Yes. Suggests safer lifetime alternatives.

---

## DI009: Open Generic Captive Dependency

**What it catches:** open generic singleton registrations that depend on shorter-lived services.

**Why it matters:** every closed generic instance inherits the lifetime mismatch.

> **Explain Like I'm Ten:** If the recipe is wrong at the top of the cookbook, every dish made from it comes out wrong.

**Problem:**

```csharp
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));

public sealed class Repository<T> : IRepository<T>
{
    public Repository(IScopedService scoped) { }
}
```

**Better pattern:**

```csharp
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
```

**Code Fix:** Yes. Can adjust lifetime for open generic registrations.

---

## DI010: Constructor Over-Injection

**What it catches:** constructors with too many meaningful dependencies.

**Why it matters:** often signals a class with too many responsibilities.

> **Explain Like I'm Ten:** If one backpack needs ten straps to carry, it is probably trying to hold too much at once.

**Problem:**

```csharp
public sealed class ReportingService
{
    public ReportingService(
        IDep1 dep1,
        IDep2 dep2,
        IDep3 dep3,
        IDep4 dep4,
        IDep5 dep5)
    {
    }
}
```

**Better pattern:** split into focused collaborators and inject smaller abstractions.

**Code Fix:** No. Design decision required.

---

## DI011: `IServiceProvider` Injection

**What it catches:** constructor injection of `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider` in normal services.

**Why it matters:** this commonly enables hidden runtime resolution and service locator behaviour.

> **Explain Like I'm Ten:** Asking for a giant "surprise box" each time instead of a known tool means no one knows what you actually need.

**Problem:**

```csharp
public sealed class MyService
{
    public MyService(IServiceProvider provider) { }
}
```

**Better pattern:** inject concrete dependencies directly.

**Code Fix:** No.

**Known exceptions in this rule:** factory-style types and middleware `Invoke`/`InvokeAsync` paths.

---

## DI012: Conditional Registration Misuse

**What it catches:**

- `TryAdd*` calls after an `Add*` already registered that service.
- Duplicate `Add*` registrations where later entries override earlier ones.

**Why it matters:** registration intent becomes unclear and behaviour differs from what readers expect.

> **Explain Like I'm Ten:** Writing your name on the same seat twice does not get you two seats; one note just replaces the other.

**Problem:**

```csharp
services.AddSingleton<IMyService, ServiceA>();
services.TryAddSingleton<IMyService, ServiceB>(); // ignored

services.AddSingleton<IMyService, ServiceA>();
services.AddSingleton<IMyService, ServiceB>(); // overrides A
```

**Better pattern:** decide and signal intent clearly: `TryAdd*` first, or explicit override with comments/tests.

**Code Fix:** No.

---

## DI013: Implementation Type Mismatch

**What it catches:** invalid `typeof` service/implementation pairs that compile but fail at runtime.

**Why it matters:** service activation throws at runtime (`ArgumentException`/`InvalidOperationException` depending on path).

> **Explain Like I'm Ten:** A round plug will not fit a square socket just because both are on your desk.

**Problem:**

```csharp
public interface IRepository { }
public sealed class WrongType { }

services.AddSingleton(typeof(IRepository), typeof(WrongType));
```

**Better pattern:**

```csharp
public sealed class SqlRepository : IRepository { }
services.AddSingleton(typeof(IRepository), typeof(SqlRepository));
```

**Code Fix:** No.

---

## DI014: Root Service Provider Not Disposed

**What it catches:** root providers from `BuildServiceProvider()` that are never disposed.

**Why it matters:** singleton disposables at root scope may never be cleaned up.

> **Explain Like I'm Ten:** Locking the front door but leaving all the taps running still wastes the whole house.

**Problem:**

```csharp
var services = new ServiceCollection();
var provider = services.BuildServiceProvider();
var service = provider.GetRequiredService<IMyService>();
```

**Better pattern:**

```csharp
using var provider = services.BuildServiceProvider();
var service = provider.GetRequiredService<IMyService>();
```

**Code Fix:** Yes. Adds disposal pattern where safe.

---

## DI015: Unresolvable Dependency

**What it catches:** registered services with direct or transitive constructor/factory dependencies that are not registered (including keyed and open-generic paths).

**Why it matters:** runtime activation fails when DI tries to create the service.

> **Explain Like I'm Ten:** Planning to build a kite without string means the build fails when you start.

**Problem:**

```csharp
public interface IMissingDependency { }
public interface IMyService { }

public sealed class MyService : IMyService
{
    public MyService(IMissingDependency missing) { }
}

services.AddSingleton<IMyService, MyService>();
```

**Better pattern:**

```csharp
public sealed class MissingDependency : IMissingDependency { }

services.AddScoped<IMissingDependency, MissingDependency>();
services.AddSingleton<IMyService, MyService>();
```

**Code Fix:** No.

### DI015 strict mode

By default, DI015 assumes common host-provided framework services (logging/options/configuration) are available.
Disable that assumption for stricter analysis:

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

DI015 is intentionally conservative to keep false positives low:

- Dependency cycles are treated as resolvable.
- Factory registrations without inspectable dependency paths are treated as resolvable.

---

## DI016: BuildServiceProvider Misuse

**What it catches:** `BuildServiceProvider()` calls while composing registrations (for example in `ConfigureServices`, `IServiceCollection` extension registration methods, or registration lambdas).

**Why it matters:** building a second provider during registration can duplicate singleton instances and produce lifetime inconsistencies.

> **Explain Like I'm Ten:** If you set up a second classroom register halfway through, children can end up counted twice and rules become muddled.

**Problem:**

```csharp
public static IServiceCollection AddFeature(this IServiceCollection services)
{
    var provider = services.BuildServiceProvider();
    var options = provider.GetRequiredService<IMyOptions>();
    return services;
}
```

**Better pattern:**

```csharp
public static IServiceCollection AddFeature(this IServiceCollection services, IMyOptions options)
{
    // Use provided dependencies/options without creating a second container
    return services;
}
```

**Code Fix:** No.

DI016 is intentionally conservative to reduce false positives:

- It only reports symbol-confirmed DI `BuildServiceProvider()` calls in registration contexts.
- It does not report provider-factory methods that intentionally return `IServiceProvider`.

---

## Samples

- `samples/SampleApp`: diagnostic examples for `DI001` to `DI016`.
- `samples/DI015InAction`: runnable unresolved-dependency demonstration.

## Configuration

Suppress one diagnostic in code:

```csharp
#pragma warning disable DI007
var service = _provider.GetRequiredService<IMyService>();
#pragma warning restore DI007
```

Or in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI007.severity = none
```

## Requirements

- `.NET Standard 2.0` consumer compatibility.
- `Microsoft.Extensions.DependencyInjection`.

## Known Limitations

- Compile-time analysis only; runtime registrations cannot be analysed.
- Cross-assembly registration graphs are not fully tracked.
- Lifetime inference follows single-service resolution paths and may not model every `IEnumerable<T>` multi-registration activation path.

## Frequently Asked Questions

### What is a dependency injection lifetime analyser for C#?

It is a Roslyn analyser package that checks your DI registrations and DI usage during compilation, so lifetime and scope mistakes are found before production.

### Can this analyser prevent ASP.NET Core runtime DI failures?

It helps prevent a large class of runtime failures, including captive dependencies, scope disposal mistakes, and unregistered direct/transitive dependencies in constructor and factory activation paths.

### Does this work in Rider, Visual Studio, and CI builds?

Yes. It runs in IDE diagnostics and standard `dotnet build` / CI workflows because it is delivered as a standard .NET analyser package.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT Licence - see [LICENSE](LICENSE).
