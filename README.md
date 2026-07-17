<p align="center">
  <img src="logo.png" alt="DependencyInjection.Lifetime.Analyzers - C# dependency injection lifetime analyser" width="512" height="512">
</p>

# DependencyInjection.Lifetime.Analyzers

**A Roslyn dependency injection analyzer/analyser package for C# and ASP.NET Core lifetime and activation bugs.**

Catch DI scope leaks, captive dependencies, `BuildServiceProvider()` misuse, circular dependencies, and unresolvable or non-instantiable services at compile time with zero runtime overhead.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CI](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)
[![Coverage](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/raw/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)

[Searchable docs site](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/) · [Rule index](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/rules/) · [Problem guides](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/problems/) · [NuGet package](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)

`DependencyInjection.Lifetime.Analyzers` is for teams using `Microsoft.Extensions.DependencyInjection` who want compile-time protection against DI lifetime mistakes that normally show up as runtime bugs, flaky tests, or production-only startup failures.

- Works in Rider, Visual Studio, and `dotnet build` / CI.
- Covers ASP.NET Core, worker services, console apps, and library code that wires services through the default DI container.
- Ships 26 focused diagnostics, with code fixes where safe and unambiguous.

## Why This DI Lifetime Analyser

`DependencyInjection.Lifetime.Analyzers` helps teams using `Microsoft.Extensions.DependencyInjection` avoid common production issues:

- `ObjectDisposedException` from invalid scope usage.
- Memory leaks from undisposed scopes or root providers.
- Captive dependency bugs caused by incorrect service lifetimes.
- Hidden service locator usage that weakens testability.
- Runtime activation failures from missing or incompatible registrations.

This analyser package is designed for **ASP.NET Core**, **worker services**, **console apps**, and **CI pipelines** that need dependable dependency injection rules.

## Why Teams Install It

- Find captive dependencies before they become stale-state or thread-safety bugs.
- Catch scope leaks before they become `ObjectDisposedException` incidents or memory leaks.
- Detect missing registrations and implementation mismatches before startup or background-job activation fails in production.
- Catch circular dependency chains and non-instantiable registrations before they fail at runtime.
- Push DI architecture rules into CI instead of relying on code review memory.

## Quickstart

<!-- generated-install-snippets:start -->
Install from NuGet:

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers --version 2.18.22
```

Or add a package reference directly:

```xml
<PackageReference Include="DependencyInjection.Lifetime.Analyzers" Version="2.18.22">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

For Central Package Management (`Directory.Packages.props`):

```xml
<PackageVersion Include="DependencyInjection.Lifetime.Analyzers" Version="2.18.22" />
```

Then reference it from the project file:

```xml
<PackageReference Include="DependencyInjection.Lifetime.Analyzers" PrivateAssets="all" />
```
<!-- generated-install-snippets:end -->

Set useful severities in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error
dotnet_diagnostic.DI013.severity = error
dotnet_diagnostic.DI007.severity = suggestion
dotnet_diagnostic.DI011.severity = suggestion
```

By default, runtime-failure and leak-oriented rules stay at `Warning` or `Error`, while broader design-smell rules such as `DI007`, `DI010`, `DI011`, and `DI012` default to `Info` to reduce noisy diagnostics.

For a rollout checklist and a starter severity policy, see [docs/ADOPTION.md](docs/ADOPTION.md).

## Who This Is For

- Teams using `Microsoft.Extensions.DependencyInjection` in ASP.NET Core or generic-host applications.
- Libraries and internal platforms that want DI usage guarded in CI, not just at runtime.
- Codebases using factories, keyed services, `ActivatorUtilities`, or manual scopes.
- Maintainers trying to reduce `IServiceProvider`-driven service locator drift over time.

## What It Catches

| Problem Area | Example Rules |
|--------------|---------------|
| Scope disposal and leaks | `DI001`, `DI004`, `DI005`, `DI014` |
| Lifetime mismatches | `DI003`, `DI009` |
| Service location and architectural drift | `DI006`, `DI007`, `DI011` |
| Registration correctness and activation validity | `DI012`, `DI013`, `DI015`, `DI016`, `DI018` |
| Dependency graph correctness | `DI017` |
| Root-provider lifetime validation | `DI019` |
| Middleware lifetime validation | `DI020` |
| Concurrency-unsafe captures in message handlers, timers, and parallel loops | `DI021`, `DI022` |
| Hosted-service scope-per-iteration validation | `DI024` |
| Cross-lifetime event-subscription leak detection | `DI025`, `DI026` |
| Constructor and composition smell detection | `DI010` |

## Table of Contents

- [Why This DI Lifetime Analyser](#why-this-di-lifetime-analyser)
- [Why Teams Install It](#why-teams-install-it)
- [Quickstart](#quickstart)
- [Who This Is For](#who-this-is-for)
- [What It Catches](#what-it-catches)
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
- [DI017: Circular Dependency](#di017-circular-dependency)
- [DI018: Non-Instantiable Implementation Type](#di018-non-instantiable-implementation-type)
- [DI019: Scoped Service Resolved From Root Provider](#di019-scoped-service-resolved-from-root-provider)
- [DI020: Middleware Captures Scoped Service In Constructor](#di020-middleware-captures-scoped-service-in-constructor)
- [DI021: Non-Thread-Safe Service Shared Across Concurrent Handler Invocations](#di021-non-thread-safe-service-shared-across-concurrent-handler-invocations)
- [DI022: Service Instance Reused Across Handler Invocations](#di022-service-instance-reused-across-handler-invocations)
- [DI024: Hosted Service Creates Scope Outside Execution Loop](#di024-hosted-service-creates-scope-outside-execution-loop)
- [DI025: Event Subscription On Longer-Lived Publisher Without Unsubscribe](#di025-event-subscription-on-longer-lived-publisher-without-unsubscribe)
- [DI026: Event Subscription On Scoped Publisher Without Unsubscribe](#di026-event-subscription-on-scoped-publisher-without-unsubscribe)
- [DI027: Rx Subscription On Longer-Lived Observable Without Dispose](#di027-rx-subscription-on-longer-lived-observable-without-dispose)
- [Configuration](#configuration)
- [Adoption Guide](#adoption-guide)
- [Frequently Asked Questions](#frequently-asked-questions)

## Rule Index

| ID | Title | Default Severity | Code Fix |
|----|-------|------------------|----------|
| [DI001](#di001-service-scope-not-disposed) | Service scope not disposed | Warning | Yes |
| [DI002](#di002-scoped-service-escapes-scope) | Scoped service escapes scope | Warning | Yes |
| [DI003](#di003-captive-dependency) | Captive dependency | Warning | Yes |
| [DI004](#di004-service-used-after-scope-disposed) | Service used after scope disposed | Warning | Yes |
| [DI005](#di005-use-createasyncscope-in-async-methods) | Use `CreateAsyncScope` in async methods | Warning | Yes |
| [DI006](#di006-static-iserviceprovider-cache) | Static `IServiceProvider` cache | Warning | Yes |
| [DI007](#di007-service-locator-anti-pattern) | Service locator anti-pattern | Info | No |
| [DI008](#di008-disposable-transient-service) | Disposable transient service | Warning | Yes |
| [DI009](#di009-open-generic-captive-dependency) | Open generic captive dependency | Warning | Yes |
| [DI010](#di010-constructor-over-injection) | Constructor over-injection | Info | No |
| [DI011](#di011-iserviceprovider-injection) | `IServiceProvider` injection | Info | No |
| [DI012](#di012-conditional-registration-misuse) | Conditional/duplicate registration misuse | Info | Yes |
| [DI013](#di013-implementation-type-mismatch) | Implementation type mismatch | Error | Yes |
| [DI014](#di014-root-service-provider-not-disposed) | Root provider not disposed | Warning | Yes |
| [DI015](#di015-unresolvable-dependency) | Unresolvable dependency | Warning | Yes |
| [DI016](#di016-buildserviceprovider-misuse) | BuildServiceProvider misuse during registration | Warning | No |
| [DI017](#di017-circular-dependency) | Circular dependency | Warning | No |
| [DI018](#di018-non-instantiable-implementation-type) | Non-instantiable implementation type | Warning | No |
| [DI019](#di019-scoped-service-resolved-from-root-provider) | Scoped service resolved from root provider | Warning | Yes |
| [DI020](#di020-middleware-captures-scoped-service-in-constructor) | Middleware captures scoped service in constructor | Warning | No |
| [DI021](#di021-non-thread-safe-service-shared-across-concurrent-handler-invocations) | Non-thread-safe service shared across concurrent handler invocations | Warning | Yes |
| [DI022](#di022-service-instance-reused-across-handler-invocations) | Service instance reused across handler invocations | Info | Yes |
| [DI024](#di024-hosted-service-creates-scope-outside-execution-loop) | Hosted service creates scope outside execution loop | Warning | No |
| [DI025](#di025-event-subscription-on-longer-lived-publisher-without-unsubscribe) | Event subscription on longer-lived publisher without unsubscribe | Warning | Yes |
| [DI026](#di026-event-subscription-on-scoped-publisher-without-unsubscribe) | Event subscription on scoped publisher without unsubscribe | Info | Yes |
| [DI027](#di027-rx-subscription-on-longer-lived-observable-without-dispose) | Rx subscription on longer-lived observable without dispose | Warning | No |

---

## DI001: Service Scope Not Disposed

**What it catches:** `IServiceScope` instances created with `CreateScope()` or `CreateAsyncScope()` that are never disposed, including scopes whose only disposal call is hidden behind a conditional branch, switch section, loop, catch block, or after a branch exit that can bypass shared cleanup. DI001 recognizes predeclared nullable scope locals assigned conditionally when a later conditional-access, non-null-guarded, same-branch pre-exit, or `finally` disposal reliably closes ownership, and it treats directly returned scopes as caller-owned even through simple casts or conditional return arms. Reassignment leaks and loop-created scopes that need per-iteration disposal still report.

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

**What it catches:** a service resolved from a scope that is returned or stored somewhere longer-lived, including services resolved through provider aliases, delegates that capture scoped services and then escape, scopes disposed later via `using (scope)`, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods. It also detects wrapped returned resolutions and later-returned locals such as casts, `as` casts, null-forgiving, ternary/coalesce expressions, and non-generic `GetService(typeof(T))`, while keeping pre-resolution locals and proven non-escaping scope-local holder objects, including simple direct local holder aliases, quiet. Holders that later escape through a return, conditional-access slot return, long-lived assignment including null-conditional assignment to a field/property-held receiver, nested receiver path under a fresh wrapper, escaping delegate, returned/stored local container, already-escaped local collection, returned collection alias, or `??=` receiver that may still point at a long-lived holder still report; slot reads before the scoped write stay quiet.

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

**Code Fix:** Yes (suppression option for intentionally accepted cases where direct refactoring is not practical).

---

## DI003: Captive Dependency

**What it catches:** singleton services capturing scoped or transient dependencies, including constructor injection, `IEnumerable<T>` collection captures, known scoped framework services such as `IOptionsSnapshot<T>`, EF Core contexts and `DbContextOptions<TContext>` registrations from `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, and `AddPooledDbContextFactory(...)` including service/implementation overload self-registrations, and high-confidence factory paths such as inline delegates, stable local delegate factories, method-group factories, `GetServices<T>()`, keyed resolutions, and `ActivatorUtilities.CreateInstance(...)` calls where DI still resolves a scoped or transient constructor parameter.

`ServiceDescriptor` registrations prepended with `services.Insert(0, descriptor)` are analyzed too, including reordered named arguments, concrete framework `ServiceCollection` receivers, and repeated prepends whose runtime list precedence differs from source order. Nonzero or dynamic insert indexes and source-defined concrete `Insert` bodies stay conservative because their absolute position or mutation behavior is not provable.

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

**DbContext-backed processors:**

```csharp
services.AddDbContext<AppDbContext>();
services.AddScoped<IProcessor, Processor>();
services.AddHostedService<ProcessorHostedService>();

public sealed class ProcessorHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public ProcessorHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IProcessor>();
        await processor.RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

Repository and unit-of-work abstractions are reported when their registered lifetime is scoped or transient. DI003 does not infer DbContext-backed behavior from names like `IRepository<T>` or `IUnitOfWork` alone.

**Code Fix:** Yes. Rewrites explicit registration lifetimes when the registration syntax is local and unambiguous (for example `AddSingleton`, `TryAddSingleton`, keyed `AddKeyedSingleton`, inline factory registrations, and supported `ServiceDescriptor` forms).

---

## DI004: Service Used After Scope Disposed

**What it catches:** using a service after the scope that produced it has already ended, including services resolved through provider aliases, scoped collections from `GetServices<T>()` enumerated after disposal, explicit `Dispose()` / `DisposeAsync()` (including `scope?.Dispose()` for scope locals), wrapped use receivers such as `service!.DoWork()` and `((IService)service).DoWork()`, scopes disposed later via `using (scope)`, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods.

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

**Code Fix:** Yes. Moves simple immediate invocation-style uses back into the owning scope only when the diagnostic local was assigned in that scope, or adds a narrow pragma suppression for context-dependent cases.

---

## DI005: Use `CreateAsyncScope` in Async Methods

**What it catches:** `CreateScope()` used in async flows where async disposal is needed and `CreateAsyncScope()` is available, including async methods, lambdas, local functions, anonymous methods, and top-level programs that use `await`. Detection covers regular member access (`_scopeFactory.CreateScope()`), parameterless `IServiceScope CreateScope()` methods on concrete `IServiceScopeFactory` implementations, and conditional-access receivers (`_scopeFactory?.CreateScope()`, `_provider?.CreateScope()`) alike.

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

**Code Fix:** Yes. Rewrites safe `using` scope creation/disposal patterns to `await using` plus `CreateAsyncScope()`, including explicit `IServiceScope` declarations that must become `var` for `AsyncServiceScope`.

---

## DI006: Static `IServiceProvider` Cache

**What it catches:** `IServiceProvider` / `IServiceScopeFactory` / keyed provider stored in static fields or properties, including common wrappers, mutable/immutable/frozen dictionary value caches, recursive dictionary values, and simple holder types that only wrap a provider.

**Why it matters:** global provider state encourages service locator use and muddles lifetime boundaries.

> **Explain Like I'm Ten:** Leaving the school master key in the corridor means anybody can open any door at any time.

**Problem:**

```csharp
public static class Locator
{
    public static IServiceProvider Provider { get; set; } = null!;
    private static readonly Lazy<IServiceProvider> LazyProvider = new(() => Provider);
    private static readonly Dictionary<string, Lazy<IServiceProvider>> TenantProviders = new();
    private static readonly ImmutableDictionary<string, IServiceProvider> SnapshotProviders = ImmutableDictionary<string, IServiceProvider>.Empty;
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

**Code Fix:** Yes. Removes `static` modifier in common private-member cases where existing references stay valid; it is suppressed for nested-type references, type-qualified references, and instance field/property initializers that would become invalid instance-member access.

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

DI007 follows generic resolutions, direct `typeof(...)` arguments, and local `Type` aliases initialized from `typeof(...)` when they are not reassigned before the resolution call. It stays quiet in recognized composition/factory boundaries: DI registration factories, value-returning `Create*`/`Build*` factory methods, ASP.NET Core middleware `Invoke`/`InvokeAsync` methods whose first parameter is `HttpContext`, `BackgroundService.ExecuteAsync`, exact hosted-service lifecycle implementations, options configure/validate implementations, and provider-aware options/factory delegates.

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

DI008 follows generic, `typeof(...)`, keyed, named-argument, `ServiceDescriptor.Transient(...)`, `ServiceDescriptor.Describe(..., ServiceLifetime.Transient)`, `new ServiceDescriptor(..., ServiceLifetime.Transient)`, conditional `services?.Add(ServiceDescriptor.Transient(...))`, `TryAddTransient`, and `TryAddEnumerable` registration shapes. Factory registrations stay quiet because disposal ownership is explicit in user code.

**Code Fix:** Yes. Suggests safer lifetime alternatives and rewrites local descriptor lifetime arguments where the registration is unambiguous.

---

## DI009: Open Generic Captive Dependency

**What it catches:** open generic singleton registrations that depend on shorter-lived services, including `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, keyed open-generic singleton registrations, and `IEnumerable<T>` constructor captures where the element service is shorter-lived.

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

DI009 follows the single likely activation constructor the container can actually use. Optional/default-value parameters are treated as activatable during that selection, and ambiguous equally-greedy constructor sets stay silent instead of guessing.

Dependency lifetimes are looked up against user registrations first and then fall back to the shared known-framework classifier, so open-generic singletons that capture `IOptionsSnapshot<T>` are reported as scoped captures even when the application does not register Options manually. `IOptions<T>` and `IOptionsMonitor<T>` keep their singleton lifetime and stay quiet.

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

For normal type registrations, DI010 evaluates the public constructor(s) the container could realistically activate instead of every declared constructor. It also covers straightforward factory registrations that directly return `new MyService(...)`, final-return factory blocks that set up locals before `return new MyService(...)`, and `ActivatorUtilities.CreateInstance<MyService>(sp)`, while staying conservative on branching or dynamic factories.

By default, DI010 reports when a constructor has more than `4` meaningful dependencies. It ignores primitives/value types, optional parameters, provider-plumbing types already covered by `DI011`, and common framework abstractions such as `ILogger<T>`, `IOptions<T>`, and `IConfiguration`.

Configure the threshold in `.editorconfig`:

```ini
[*.cs]
dotnet_code_quality.DI010.max_dependencies = 5
```

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

**Code Fix:** No. Replacing provider plumbing with explicit dependencies is a design decision.

**Known exceptions in this rule:** factory-style types with value-returning factory members, singleton services that use `IServiceScopeFactory` to create scopes deliberately, ASP.NET Core middleware `Invoke`/`InvokeAsync` methods whose first parameter is `HttpContext`, hosted services, endpoint filter factories, and provider parameters on non-public constructors the container cannot activate.

---

## DI012: Conditional Registration Misuse

**What it catches:**

- `TryAdd*` calls after an `Add*` already registered that service.
- Duplicate `Add*` registrations where later entries override earlier ones.

DI012 also follows the same `IServiceCollection` flow across local aliases and source-defined helper/local-function wrappers, while treating opaque helper boundaries conservatively instead of guessing at registration order. It stays quiet for intentional branch-dependent fallbacks such as guarded `Add*` plus unconditional `TryAdd*`, and for mutually exclusive `if`/`else if`/`else` alternative registrations.
When a `Replace(...)` still leaves a duplicate descriptor behind, DI012 reports the active registration that survives the single-descriptor replacement, ignoring inactive `TryAdd*` calls when choosing the message location.

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

**Code Fix:** Yes for ignored `TryAdd*` and `TryAddKeyed*` calls that are block-contained standalone statements; the fixer removes the redundant ignored registration. Duplicate override cases and embedded single-line statement bodies remain manual.

---

## DI013: Implementation Type Mismatch

**What it catches:** invalid service/implementation pairs that compile but fail at runtime, including generic, `typeof(...)`, keyed, named-argument, and `ServiceDescriptor` registrations.

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

**Code Fix:** Yes. Offers broad assists where the syntax and symbols are local enough to rewrite safely: remove the invalid block-contained standalone registration, replace the implementation type with a compatible candidate, or retarget the service type to an interface/base type implemented by the current implementation, including invalid implementation-instance registrations. Embedded single-line statement bodies stay manual unless a symbol-backed type rewrite is available.

---

## DI014: Root Service Provider Not Disposed

**What it catches:** root providers from `BuildServiceProvider()` that are never disposed, including local providers whose only manual disposal is conditional, catch-only, after reassignment to another provider, or after repeated creation inside a loop. Straight-line explicit disposal, standard `Dispose()` to `Dispose(true)` cleanup, and caller-owned return flows are accepted even when the `BuildServiceProvider()` result is parenthesized, same-instance cast, null-forgiven, selected by a ternary arm, or supplied by a null-coalescing operand; user-defined conversions remain reportable because they may produce a different instance.

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

**Code Fix:** Yes. Adds disposal pattern for simple local declarations with no existing manual disposal code. Conditional or otherwise partial manual-disposal flows stay diagnostic-only so the ownership rewrite remains deliberate.

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

**Code Fix:** Yes. Adds a missing self-binding registration when DI015 can prove a single direct concrete class dependency is safe to register. Supports local constructor diagnostics, `TryAdd*` registration sites, local `IServiceCollection` aliases, direct `GetRequiredService<TConcrete>()` factory diagnostics, and keyed self-bindings when the key can be emitted as a C# literal.

### DI015 strict mode

By default, DI015 assumes common host-provided framework services are available, including logging/options/configuration, `ILoggerFactory`, `IHostApplicationLifetime`, and the Generic Host's singleton `IHostLifetime`. Strict mode still requires explicit registrations, keyed framework-service requests are never satisfied by this unkeyed ambient assumption, and explicit registrations override ambient lifetime classification so scoped framework-service replacements remain visible to lifetime rules. Explicit framework extension calls such as `AddHttpClient()`, `AddMemoryCache()`, and `AddHttpContextAccessor()` are modeled as registrations for `IHttpClientFactory`, `IMemoryCache`, and `IHttpContextAccessor`; those services still report as missing when the matching extension is absent. `TimeProvider` also reports as missing unless registered explicitly. Typed HTTP client registrations treat one constructor `HttpClient` parameter as factory-provided while still checking repeated `HttpClient` parameters and other typed-client constructor dependencies. EF Core contexts registered through `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, or `AddPooledDbContextFactory(...)` are also modeled as registrations, including service/implementation overload self-registrations and the `DbContextOptions<TContext>` and `IDbContextFactory<TContext>` dependencies those patterns require.
Disable that assumption for stricter analysis:

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

DI015 is intentionally conservative to keep false positives low:

- Source-visible `IServiceCollection` wrappers are expanded before DI015 reports missing registrations.
- Stable local delegate factories are inspected, including inherited keyed factory parameters, later definite simple reassignments, exhaustive local-function branch rewrites, and method-group delegate aliases to local functions that rewrite the factory, while unrelated assignment left-hand-side uses and opaque delegate-local writes such as direct delegate calls, delegate `.Invoke()` calls, and `ref`/`out` writes stay conservative.
- `[ServiceKey]` parameters, `IEnumerable<T>`, `IServiceProviderIsService`, and `IServiceProviderIsKeyedService` are treated as container-provided.
- Parameterless `[FromKeyedServices]` inherits the containing keyed registration key when that key is known.
- `KeyedService.AnyKey` keyed registrations satisfy exact keyed dependency requests.
- Definite same-flow `RemoveAll(...)` and `Replace(...)` mutations suppress diagnostics for registrations they remove.
- Dependency cycles are treated as resolvable.
- Factory registrations without inspectable dependency paths are treated as resolvable.
- `GetService(...)` and dynamic keyed resolutions are treated as optional/unknown.
- If an earlier opaque or external wrapper could have registered services on the same `IServiceCollection` flow, DI015 stays silent instead of speculating.
- If any effective candidate registration is backed by an opaque factory, DI015 stays silent instead of speculating.
- Two-Type registrations with a non-extractable implementation argument are treated as registered-but-unknown, suppressing downstream missing-registration guesses without inventing an implementation shape.

---

## DI016: BuildServiceProvider Misuse

**What it catches:** `BuildServiceProvider()` calls while composing registrations (for example in `ConfigureServices`, `IServiceCollection` extension registration methods, registration lambdas, or builder-style `.Services` helper flows), whether written as reduced extension syntax (`services.BuildServiceProvider()`) or as a direct static call (`ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(builder.Services)`).

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
- It does not report provider-factory methods that intentionally return `IServiceProvider`, concrete provider implementations, or awaited provider results.
- It recognizes assignable `IServiceCollection` abstractions and same-boundary helper/alias flows from `.Services`, but it does not warn on standalone top-level `new ServiceCollection()` composition roots.
- It recognizes metadata-defined `IServiceCollection` fluent chains, so `builder.Services.AddSingleton<...>().BuildServiceProvider()` is treated as the same registration source as `builder.Services.BuildServiceProvider()`.
- Direct static extension calls recover the receiver from the Roslyn-bound `IServiceCollection` parameter, so named and reordered arguments retain the same registration-context proof; provider-factory return guardrails still apply.
- Builder `.Services` flows wrapped in the null-forgiving operator (`builder.Services!`) or a same-type cast (`(IServiceCollection)builder.Services`) at the call site, in helper return expressions, or in local-variable initializers are still recognized as registration contexts, while provider-factory methods that wrap the same expression stay silent because they return `IServiceProvider`.
- Identity-preserving null guards such as `(builder.Services ?? throw new InvalidOperationException()).BuildServiceProvider()` retain the builder `.Services` proof; a coalesce with an arbitrary fallback collection stays silent because the actual source is not provable.
- Conditional-access invocations and aliases such as `builder.Services?.BuildServiceProvider()`, `builder?.Services.BuildServiceProvider()`, and `var services = builder?.Services; services.BuildServiceProvider();` are recognized through the enclosing `ConditionalAccessExpression` and the `MemberBindingExpression`-shaped `.Services` access, so null-safe builder flows participate in detection the same way as direct member access. Provider-factory methods wrapping the same shape stay quiet.

---

## DI017: Circular Dependency

**What it catches:** constructor-injection cycles such as `A -> B -> A`, including longer transitive loops. It follows effective registration precedence, including exact closed registrations before open-generic fallbacks, and mirrors the default container's constructor-set rule: the greediest resolvable constructor is analyzed only when its resolved service identifiers (type plus key) contain every other resolvable constructor's service identifiers. Equivalent reordered constructors therefore expose the same real cycle, while non-superset sets stay silent because activation is ambiguous.

**Why it matters:** the default DI container cannot resolve circular constructor graphs and will fail at runtime when the service is activated.

> **Explain Like I'm Ten:** If two people each wait for the other to hand over the key first, the door never opens.

**Problem:**

```csharp
services.AddScoped<IOrderService, OrderService>();
services.AddScoped<IPaymentService, PaymentService>();

public sealed class OrderService : IOrderService
{
    public OrderService(IPaymentService payment) { }
}

public sealed class PaymentService : IPaymentService
{
    public PaymentService(IOrderService order) { }
}
```

**Better pattern:** break the cycle by moving shared logic into a third collaborator or by changing the dependency direction so each service has an acyclic constructor graph.

**Code Fix:** No. Breaking dependency cycles is a design change.

---

## DI018: Non-Instantiable Implementation Type

**What it catches:** registrations whose implementation type cannot be constructed by the DI container, such as abstract classes, interfaces, static classes, delegate types registered without a factory, default structs and enums, or concrete classes with no public constructors.

**Why it matters:** these registrations compile, but fail at runtime when the container tries to activate the service.

> **Explain Like I'm Ten:** Writing a ghost on the class register does not mean someone can actually show up for class.

**Problem:**

```csharp
public interface IMyService { }
public sealed class BadPrivateCtorService : IMyService
{
    private BadPrivateCtorService() { }
}

services.AddSingleton<IMyService, BadPrivateCtorService>();
```

DI018 also reports abstract classes, interfaces, static classes, delegate types (such as `services.AddSingleton<MyHandler>()` where `MyHandler` is a `delegate`), default structs, and enums used as implementation types without a factory expression. The default container activates implementation types through public constructors returned by reflection: Roslyn's synthetic value-type constructor is not emitted as constructor metadata, so a default struct or enum fails at first resolution. A struct with an explicitly declared public constructor remains valid. Factory arguments are recognized from the bound delegate parameter even when the expression is an invocation, conditional, coalesce expression, or delegate object creation, so valid factory registrations do not self-bind the service type. Delegates carry only implicit `(object, IntPtr)` and `(object, UIntPtr)` constructors that the default DI container cannot populate, so the registration fails at activation.

**Better pattern:**

```csharp
public sealed class GoodConcreteService : IMyService { }
public readonly struct MyValueService { }

services.AddSingleton<IMyService, GoodConcreteService>();

// For delegate types, register with a factory expression:
services.AddSingleton<MyHandler>(sp => (msg) => Console.WriteLine(msg));

// Supply value types explicitly instead of asking the container to activate them:
services.AddSingleton(typeof(MyValueService), _ => new MyValueService());
```

**Code Fix:** No.

---

## DI019: Scoped Service Resolved From Root Provider

**What it catches:** scoped services, known scoped framework services such as `IOptionsSnapshot<T>`, EF Core contexts from `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, and `AddPooledDbContextFactory(...)` including service/implementation overload self-registrations, or services whose activation graph reaches a scoped service, resolved from a root `IServiceProvider` such as ASP.NET Core `app.Services`, ASP.NET test-host `factory.Services` / `server.Services`, Generic Host `host.Services`, nullable root-provider surfaces such as `app.Services!`, or a provider returned by `BuildServiceProvider()`. Root-provider aliases also stay classified through `?? throw` guards and conditional expressions whose two result arms are proven root through path-stable declarations or straight-line writes. Provider declarations and assignments are collected in source order, path stability propagates through copied aliases, later unclassified, `??=`, deconstruction, and `ref`/`out` writes invalidate older provider facts. Write facts become visible only after right-hand-side, initializer, or argument evaluation, and nested mutation events are processed before their enclosing write, so resolutions and alias copies observe the provider state at that runtime point. Merely binding or retargeting a ref local preserves the referents' facts; source-positioned mappings ensure later writes follow every possible storage active at that point across conditional or unconditional retargeting and ref-conditional local, by-reference argument, or lvalue targets, while reads use only the mapping active at their position and classify the alias only when every possible storage agrees. Writes through aliases with multiple possible referents invalidate every candidate storage rather than claiming each one definitely received the new value. Forward or backward `goto` edges cannot make path-dependent facts stable. Field/property facts never qualify because source position cannot prove cross-method execution; deferred lambda, LINQ-query, and local-function hazards remain conservative for captured outer storage, while locals and parameters owned by the deferred boundary retain ordinary path stability for declarations and straight-line writes. Control flow outside that owning boundary does not alter the path executed inside it. Other control-flow-dependent, mixed root/scoped, and unknown arms stay conservative. Both ordinary extension syntax and direct static calls through the exact framework `ServiceProviderServiceExtensions` and `ServiceProviderKeyedServiceExtensions` types are analyzed, including reordered named arguments; same-named user extensions stay silent.

**Why it matters:** the default container's scope validation is designed to prevent scoped services from being resolved directly or indirectly from the root provider. Resolving them from root can fail at runtime or accidentally stretch scoped state to application lifetime.

> **Explain Like I'm Ten:** A classroom pass only works for one lesson. Taking it home for the whole year breaks the rules.

**Problem:**

```csharp
var app = builder.Build();
var db = app.Services.GetRequiredService<MyDbContext>();
```

**Better pattern:**

```csharp
var app = builder.Build();
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
```

DI019 also reports singleton and hosted-service methods that resolve scoped services from an injected root provider.

**Shows the full resolution path.** When a scoped service is reached *indirectly*, DI019 names every hop on the way down, so you never have to trace the graph by hand to find out why an innocent-looking resolution is unsafe:

```text
DI019: Service 'OrderProcessor' resolves scoped dependency from the root provider:
       OrderProcessor -> IInvoiceBuilder -> IRepository -> AppDbContext
```

That is strictly more actionable than the container's own `ValidateOnBuild` exception, which reports only the two endpoints and leaves the chain in between for you to reconstruct.

**Code Fix:** Yes. Offers to wrap ordinary extension-form resolutions in a `using` declaration or block with a new scope. Direct static-call syntax reports without a code fix because rewriting the declaring type as a provider receiver would not compile.

---

## DI020: Middleware Captures Scoped Service In Constructor

**What it catches:** Scoped services captured by the constructor of a conventional middleware class — both directly (a scoped parameter) and transitively (a parameter whose activation graph reaches a scoped service). Middleware registrations are recognized in reduced extension form (`app.UseMiddleware<T>()`) and in direct framework static form (`UseMiddlewareExtensions.UseMiddleware<T>(app)` / `UseMiddlewareExtensions.UseMiddleware(app, typeof(T))`), with explicit activation arguments matched to constructor parameters.

**Why it matters:** Conventional middleware (used via `app.UseMiddleware<T>()`) is instantiated once per application lifetime. Injecting a scoped service into the constructor will cause that specific scoped instance to be captured for the entire application lifetime, which often leads to "captive dependency" bugs or runtime errors (e.g., if the service is a DbContext).

**Problem:**

```csharp
public class MyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMyScopedService _scoped;

    public MyMiddleware(RequestDelegate next, IMyScopedService scoped)
    {
        _next = next;
        _scoped = scoped; // Scoped service captured in singleton middleware!
    }

    public Task InvokeAsync(HttpContext context) => _next(context);
}
```

**Better pattern:**

```csharp
public class MyMiddleware
{
    private readonly RequestDelegate _next;

    public MyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // Resolve scoped services from InvokeAsync parameters
    public Task InvokeAsync(HttpContext context, IMyScopedService scoped)
    {
        return _next(context);
    }
}
```

**Code Fix:** No. Moving dependencies to the `InvokeAsync` method may require significant architectural changes.

---

## DI021: Non-Thread-Safe Service Shared Across Concurrent Handler Invocations

**What it catches:** A documented non-thread-safe service (EF Core `DbContext` and derived contexts, `DbConnection`/`DbCommand`/`DbTransaction`/`DbDataReader` and their interfaces, `IDbContextTransaction`, `HttpContext`) that is created or resolved once and then captured — through a field, a closure over an outer local, or an enclosing method parameter — into a handler that a framework invokes **concurrently**:

- `ServiceBusProcessor.ProcessMessageAsync` / `ProcessErrorAsync` (when `MaxConcurrentCalls` is provably above 1)
- `ServiceBusSessionProcessor.ProcessMessageAsync` / `ProcessErrorAsync` (sessions are pumped concurrently by default)
- `EventProcessorClient.ProcessEventAsync` / `ProcessErrorAsync` (partitions are processed concurrently)
- RabbitMQ `EventingBasicConsumer.Received` / `AsyncEventingBasicConsumer.Received` / `ReceivedAsync` (instance-correlated: the consumer's own factory/connection/channel chain proves `ConsumerDispatchConcurrency` — proven 1 or a fresh default factory stays silent, proven above 1 warns, untraceable chains stay config-gated DI022; fallback constants must bind to the real RabbitMQ property)
- `System.Threading.Timer` callbacks with a finite period (callbacks can overlap)
- `System.Timers.Timer.Elapsed` (elapsed events can overlap unless `AutoReset = false` or a `SynchronizingObject` is set)
- `Parallel.For` / `ForEach` / `ForEachAsync` / `Invoke` bodies
- PLINQ `ForAll` bodies (partitions run concurrently unless `WithDegreeOfParallelism(1)` is proven on the query chain)
- TPL Dataflow `ActionBlock` / `TransformBlock` / `TransformManyBlock` delegates (when `MaxDegreeOfParallelism` is provably above 1; blocks default to sequential)
- `EventProcessor<TPartition>` batch and error overrides (partitions are processed concurrently)

It also catches the deferred variant: resolving from a **long-lived scope captured from outside the handler** (`_scope.ServiceProvider.GetRequiredService<AppDbContext>()` inside the handler still hands the same instance to every concurrent invocation).

**Why it matters:** This is the deferred form of the captive dependency. The lifetimes can look correct — a scope was even created — but one instance is shared across overlapping invocations and fails at runtime with errors like *"A second operation was started on this context instance before a previous operation completed."* It works in dev (one message at a time) and explodes under production load.

**Problem:**

```csharp
public class OrderProcessor : BackgroundService
{
    private readonly AppDbContext _db;                 // resolved once
    private readonly ServiceBusSessionProcessor _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleAsync; // invoked concurrently
        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task HandleAsync(ProcessSessionMessageEventArgs args)
    {
        _db.Add(args);                 // DI021: one DbContext, N concurrent handlers
        await _db.SaveChangesAsync();  // "A second operation was started on this context"
    }
}
```

**Better pattern:**

```csharp
private async Task HandleAsync(ProcessSessionMessageEventArgs args)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Add(args);
    await db.SaveChangesAsync();
}
```

DI021 stays quiet for handlers that already do the right thing: a scope created **inside** the handler, `IDbContextFactory<TContext>` usage, instances created inline, and handlers that explicitly serialize themselves (`lock` on a stable monitor shared from outside the handler, `SemaphoreSlim` wait/release in `try`/`finally`, `Interlocked`/`Monitor.TryEnter` reentrancy guards, timer re-arm). Locking the handler's own parameter, an object created inside the handler, or a shared monitor reassigned by the handler does not serialize separate invocations and still reports. Frameworks that already create a scope per message (MassTransit, NServiceBus, Quartz, Hangfire, SignalR, Azure Functions) are deliberately not sinks.

**Code Fix:** Yes. Rewrites the handler to resolve the service from a new scope per invocation, plumbs `IServiceScopeFactory` through the constructor when needed, and removes the now-dead captured field.

---

## DI022: Service Instance Reused Across Handler Invocations

**What it catches:** The same capture shape as DI021, but on a sink whose concurrency is controlled by a configuration knob that cannot be proven at compile time — canonically `ServiceBusProcessor` where `MaxConcurrentCalls` comes from configuration or is left at its default.

**Why it matters:** If the knob is ever raised above 1 this becomes the DI021 concurrency crash. Even with sequential dispatch, one instance accumulates state across all messages: an EF Core change tracker grows without bound, and a failed `SaveChanges` poisons every subsequent message. DI022 reports at **Info** severity because the concurrency claim is conditional; teams that want it louder can raise it with one line:

```ini
dotnet_diagnostic.DI022.severity = warning
```

When `MaxConcurrentCalls` is a compile-time constant greater than 1, the diagnostic upgrades to DI021 (Warning). When it is provably 1, both rules stay silent.

**Code Fix:** Yes. Same scope-per-invocation rewrite as DI021.

---

## DI024: Hosted Service Creates Scope Outside Execution Loop

**What it catches:** A `BackgroundService.ExecuteAsync` override (or `IHostedService`/`IHostedLifecycleService` start method) that creates an `IServiceScope` once **before** its long-running execution loop — including direct or compound cancellation checks, `while (true)`, `for (;;)`, `PeriodicTimer` loops, and channel-consumer loops — and uses it inside the loop, either directly or through a service resolved from it. One-hop, directly invoked private helpers in the same type declaration receive the same helper-local analysis; deferred and transitive helper calls stay conservative. Generic and direct-`typeof(T)` non-generic `GetService`/`GetRequiredService` resolutions participate, including keyed `GetKeyedService`/`GetRequiredKeyedService` calls with compile-time keys; runtime `Type` values and dynamic keys stay conservative. Compound conditions stay conservative: nested `!` operators are reduced by polarity, every `&&` operand must be long-running, while one long-running `||` operand is sufficient; negated cancellation combinations use De Morgan semantics. It also catches a service whose registration is provably scoped resolved once before the loop and reused across iterations.

**Why it matters:** The well-known hosted-service idiom is *scope per iteration*. A scope hoisted above the loop keeps the same scoped instances alive for the entire process lifetime: an EF Core `DbContext` serves stale data and its change tracker grows without bound, a unit of work accumulates every iteration's state, and a single failure poisons all subsequent iterations.

```csharp
public class PollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PollingService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); // DI024: one scope for the process lifetime
        while (!stoppingToken.IsCancellationRequested)
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
            await processor.ProcessPendingAsync(stoppingToken);
        }
    }
}
```

**Correct pattern:** create the scope inside the loop body so each iteration gets fresh scoped services:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
        await processor.ProcessPendingAsync(stoppingToken);
    }
}
```

DI024 stays quiet when the code already does the right thing: a scope created inside the loop (including an inner batch loop reusing the outer iteration's scope), a startup scope consumed entirely before the loop (migrations), a dispose-and-recreate scope reassigned inside the loop, a hoisted scope whose every resolution is provably singleton (hoisting is then behaviorally identical), bounded loops such as cancellation-plus-counter conjunctions, and hoisted services whose lifetime cannot be proven scoped from the visible registrations.

**Code Fix:** No. Moving the scope into the loop is a statement-level rewrite with disposal implications; apply the correct pattern above manually.

---

## DI025: Event Subscription On Longer-Lived Publisher Without Unsubscribe

**What it catches:** A transient- or scoped-registered service that subscribes (`+=`) an instance-capturing handler — an instance method group, a `this`-capturing lambda, or a stored instance-bound delegate field — to an event on a **longer-lived publisher** and never unsubscribes. Longer-lived publishers are injected dependencies whose registration is provably singleton — closed registrations preferred, open-generic singleton registrations (`AddSingleton(typeof(IEventBus<>), typeof(EventBus<>))`) matched for constructed injections — via a constructor parameter or a field/property assigned only from a constructor parameter, and `static` events. Identity and reference casts preserve that proof, so `((IBaseBus)_bus).Changed += H` reports for direct injected receivers and already-proven stable chains. Chained receivers (`_host.Bus.Changed += H`) report too when the publisher is a **stable projection** of an injected root: the lifetime proof anchors on the chain root's registration, and every intermediate segment must be a readonly field, a get-only auto-property, or a getter returning one — interface segments proven through the root's registered implementation types. C# forbids assigning another type's field-like event, so the cross-type delegate leak lives on a **delegate-typed field or property** of the publisher instead: `_bus.Handlers += OnMessage` and the equivalent self-assignment `_bus.Handlers = (EventHandler)Delegate.Combine(_bus.Handlers, OnMessage)` report identically to an event `+=`, with a mirrored `Delegate.Remove` self-assignment recognized as the matching unsubscription. An unsubscription written with a *different* lambda instance (`-= (s, e) => Handle()` after `+= (s, e) => Handle()`) is recognized as the classic no-op unsubscribe bug: the subscription still reports, and the diagnostic points at the ineffective `-=`.

**Why it matters:** This is the most common managed memory leak in .NET. The publisher's delegate list holds a strong reference to every handler target, so a singleton publisher roots **every subscriber instance the container ever creates** — each resolution leaks a handler plus the full object graph behind it, and every event raise fans out to thousands of stale handlers executing against released state. Catching it precisely requires knowing registration lifetimes, which is exactly what this analyzer knows and lifetime-blind analyzers cannot.

```csharp
services.AddSingleton<IMessageBus, MessageBus>();
services.AddTransient<OrderHandler>();

public class OrderHandler
{
    private readonly IMessageBus _bus;

    public OrderHandler(IMessageBus bus)
    {
        _bus = bus;
        _bus.MessageReceived += OnMessage; // DI025: every OrderHandler instance stays rooted by the singleton
    }

    private void OnMessage(object sender, EventArgs e) { }
}
```

**Correct pattern:** store the subscription and remove it when the subscriber is released:

```csharp
public class OrderHandler : IDisposable
{
    private readonly IMessageBus _bus;

    public OrderHandler(IMessageBus bus)
    {
        _bus = bus;
        _bus.MessageReceived += OnMessage;
    }

    public void Dispose() => _bus.MessageReceived -= OnMessage;

    private void OnMessage(object sender, EventArgs e) { }
}
```

DI025 stays quiet when the pairing is safe or unprovable: singleton subscribers (a bounded population of one cannot grow the delegate list — hosted services subscribing to singleton buses stay silent), transient publishers (scoped publishers report the [DI026](#di026-event-subscription-on-scoped-publisher-without-unsubscribe) Info tier instead), any matching `-=` anywhere in the type (Dispose, `StopAsync`, a `Detach()` teardown, or the unsubscribe-then-resubscribe idiom) with the same method group — override chains normalized — or the same stored delegate field/local, static handlers and `this`-free lambdas (they do not root the subscriber), publishers assigned from `new` or from ordinary method parameters, user-defined or value-changing receiver conversions, chained receivers whose projection is not provably stable (a settable or computed segment may hand out a different instance per access, and metadata-only or virtual segments cannot be inspected), unregistered subscriber or publisher types, keyed-only publisher registrations, `EventSource`-derived publishers, and factory registrations whose implementation type is unknown. Casted and uncast receiver syntax canonicalize to the same publisher identity when matching `+=` with `-=`.

**Code Fix:** Yes, in three tiers, all gated on a method-group handler whose receiver (a field/property, a field/property-rooted chain, or a static event) still resolves inside `Dispose`. (1) **Insert into an existing Dispose** — when the type already declares a block-bodied `Dispose()`, `Dispose(bool)`, or `DisposeAsync()` and implements the matching disposal interface (`IDisposable`/`IAsyncDisposable` — a method merely named Dispose is never called by the container), the fix inserts the mirrored `-=` at the top of that method. (2) **Create the Dispose path when the contract is inherited** — when disposability comes from a base type following the standard virtual `Dispose(bool)` pattern, the fix adds a `protected override void Dispose(bool disposing)` that unsubscribes and calls `base.Dispose(disposing)`; inherited shapes with no such hook (a non-virtual or explicitly-implemented base `Dispose`) are refused so the fix can never add a method the container won't call. (3) **Implement `IDisposable` for scoped subscribers** — a subscriber registered **scoped** that implements neither disposal interface gets `IDisposable` plus a `public void Dispose()` that unsubscribes, since its owning scope disposes it deterministically. Introducing `IDisposable` on a **transient** subscriber stays refused — that is the DI008 disposable-transient-capture shape, so the fix never trades a DI025 for a DI008 — and hoisting a lambda into a field stays refused because it changes capture semantics.

---

## DI026: Event Subscription On Scoped Publisher Without Unsubscribe

**What it catches:** The scope-bounded tier of DI025: a **transient**-registered service subscribes an instance-capturing handler to an event on a **scoped** registered publisher — same receiver, identity/reference-cast, handler, and unsubscription proofs as DI025 — and never unsubscribes. The publisher's registration lifetime is resolved with the same rules (most conservative registration wins, closed registrations preferred over open-generic fallbacks, keyed-only registrations excluded), so a publisher registered both scoped and singleton reports DI026, because only the scope-bounded claim is provable.

**Why it matters:** A transient injected with a scoped publisher is resolved from that same scope, so every transient instance the scope creates stays rooted in the publisher's delegate list **until the scope is disposed**, and the event keeps invoking handlers on instances the container has already released. In a short per-request scope the accumulation usually dies quickly; in long-lived scopes — SignalR connections, Blazor circuits, hosted-service loop scopes — it is a real leak. DI026 reports at Info because the impact depends on scope longevity; raise it per team policy:

```ini
[*.cs]
dotnet_diagnostic.DI026.severity = warning
```

```csharp
services.AddScoped<IMessageBus, MessageBus>();
services.AddTransient<OrderHandler>();

public class OrderHandler
{
    public OrderHandler(IMessageBus bus)
    {
        bus.MessageReceived += OnMessage; // DI026: rooted by the scoped bus until the scope is disposed
    }

    private void OnMessage(object sender, EventArgs e) { }
}
```

**Correct pattern:** identical to DI025 — store the subscription and remove it with `-=` when the subscriber is released (for example in `Dispose`).

DI026 shares every DI025 guardrail: scoped subscribers on scoped publishers stay silent (equal lifetimes are torn down together), any matching `-=` anywhere in the type suppresses, and all silence-on-unknown legs (unstable chained projections, `new`-assigned members, keyed-only publishers, `EventSource` publishers, factory registrations) apply unchanged — stable chained receivers report the tier exactly like direct receivers.

**Code Fix:** Yes — the same tier-1 (insert into existing `Dispose`) and tier-2 (override an inherited virtual `Dispose(bool)`) repairs as DI025, with the same gates. The tier-3 implement-`IDisposable` assist is never offered here, because DI026 only fires for **transient** subscribers and making a transient `IDisposable` is exactly the DI008 shape the fixer refuses.

---

## DI027: Rx Subscription On Longer-Lived Observable Without Dispose

**What it catches:** The Rx twin of DI025. `IObservable<T>.Subscribe(...)` returns an `IDisposable` token that unsubscribes the observer when disposed — there is no `-=` to prove missing, so the leak proof inverts to a **discarded token**. A **transient** or **scoped** registered service subscribes an instance-capturing handler (method group, `this`-capturing lambda, or stored delegate) to an observable exposed by a longer-lived publisher — an injected **singleton** dependency, or a **scoped** publisher shared by a transient subscriber — and throws the returned token away. The observable is reached through the same classified receivers as DI025 (an injected member proven ctor-assigned, a constructor parameter, or a stable chained projection such as `_source.Ticks`), and the publisher's registration lifetime is resolved with the same rules (most conservative registration wins, closed registrations preferred over open-generic fallbacks, keyed-only registrations excluded).

**Why it matters:** A discarded subscription is a live one. The observable holds the observer, the observer captures the subscriber, and nothing ever releases it, so the longer-lived publisher roots every subscriber instance the container creates — leaking memory on each resolution and invoking stale observers against released state. DI027 is a single **Warning** tier: whether the publisher is singleton or a scope-shared scoped, a discarded token that outlives the subscriber is a definite leak.

```csharp
services.AddSingleton<ITicker, Ticker>();   // Ticker : IObservable<int>
services.AddTransient<TickHandler>();

public class TickHandler
{
    public TickHandler(ITicker ticker)
    {
        ticker.Subscribe(OnTick); // DI027: the IDisposable is discarded; every TickHandler stays rooted
    }

    private void OnTick(int value) { }
}
```

**Correct pattern:** store the token and dispose it when the subscriber is released (for example in `Dispose`, or via a `CompositeDisposable`).

```csharp
public class TickHandler : IDisposable
{
    private readonly IDisposable _subscription;

    public TickHandler(ITicker ticker) => _subscription = ticker.Subscribe(OnTick);

    public void Dispose() => _subscription.Dispose();

    private void OnTick(int value) { }
}
```

DI027 recognizes both idiomatic receiver syntax (`source.Subscribe(handler)`) and direct static extension syntax (`ObservableExtensions.Subscribe(source, handler)`). Static calls must bind to a real extension method; Roslyn's bound parameter mapping identifies the observable even when named arguments are reordered, and the source argument is never mistaken for a handler.

The BCL observer overload is also covered when the subscriber passes itself directly (`source.Subscribe(this)`): the argument must bind to `IObserver<T>` and reduce semantically to the containing instance. Separate observer objects remain silent because they do not prove that the registered subscriber is retained.

**Guardrails (silent, by design):** DI027 only fires on the highest-confidence discard shapes — an ignored expression statement (`obs.Subscribe(H);`), a discard assignment (`_ = obs.Subscribe(H)`), or a local initialized with the token that is never referenced again (and is not a `using` declaration). Everything else stays silent and is a documented false negative: tokens stored in a **field** (dispose-path analysis is deferred), `using`/`using var` subscriptions, tokens that are later `.Dispose()`d, returned, or passed as arguments, and `CompositeDisposable`/`DisposeWith`/`AddTo`/`SerialDisposable` patterns. As with DI025, singleton subscribers, transient publishers, scoped-on-scoped pairs, static or `this`-free lambdas, separate observer objects, unregistered subscriber/publisher types, keyed-only publishers, unstable chained projections, non-extension static helpers named `Subscribe`, and non-observer `Subscribe(this)` overloads all stay silent.

**Code Fix:** No — planned. The safe repair (introduce `IDisposable`, store the token, dispose it) depends on the subscriber's registered lifetime exactly like the DI025 tier-3 assist, and is deferred to a follow-up.

---

## Samples

- `samples/SampleApp`: diagnostic examples for `DI001` to `DI027`.
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

## Adoption Guide

- Start with [docs/ADOPTION.md](docs/ADOPTION.md) if you are evaluating the package for a team or shared platform.
- Use [docs/RULES.md](docs/RULES.md) if you want a rule-by-rule reference you can link from issues, pull requests, or internal docs.

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
