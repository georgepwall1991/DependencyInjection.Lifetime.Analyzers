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
- Ships 19 focused diagnostics, with code fixes where safe and unambiguous.

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
dotnet add package DependencyInjection.Lifetime.Analyzers --version 2.8.4
```

Or add a package reference directly:

```xml
<PackageReference Include="DependencyInjection.Lifetime.Analyzers" Version="2.8.4">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

For Central Package Management (`Directory.Packages.props`):

```xml
<PackageVersion Include="DependencyInjection.Lifetime.Analyzers" Version="2.8.4" />
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
| [DI019](#di019-scoped-service-resolved-from-root-provider) | Scoped service resolved from root provider | Warning | No |

---

## DI001: Service Scope Not Disposed

**What it catches:** `IServiceScope` instances created with `CreateScope()` or `CreateAsyncScope()` that are never disposed, including scopes whose only disposal call is hidden behind a conditional branch, switch section, loop, or catch block.

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

**What it catches:** a service resolved from a scope that is returned or stored somewhere longer-lived, including services resolved through provider aliases, delegates that capture scoped services and then escape, scopes disposed later via `using (scope)`, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods.

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

**What it catches:** singleton services capturing scoped or transient dependencies, including constructor injection, `IEnumerable<T>` collection captures, and high-confidence factory paths such as inline delegates, method-group factories, `GetServices<T>()`, keyed resolutions, and `ActivatorUtilities.CreateInstance(...)` without explicit constructor arguments.

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

**Code Fix:** Yes. Rewrites explicit registration lifetimes when the registration syntax is local and unambiguous (for example `AddSingleton`, keyed `AddKeyedSingleton`, and supported `ServiceDescriptor` forms).

---

## DI004: Service Used After Scope Disposed

**What it catches:** using a service after the scope that produced it has already ended, including services resolved through provider aliases, scoped collections from `GetServices<T>()` enumerated after disposal, explicit `Dispose()` / `DisposeAsync()`, scopes disposed later via `using (scope)`, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods.

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

For normal type registrations, DI010 evaluates the constructor(s) the container could realistically activate instead of every accessible constructor. It also covers straightforward factory registrations that directly return `new MyService(...)` or `ActivatorUtilities.CreateInstance<MyService>(sp)`, while staying conservative on more dynamic factories.

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

**Code Fix:** Yes. Adds a missing self-binding registration only when DI015 can prove a single direct constructor dependency is a concrete, non-keyed type and the registration call is local and unambiguous.

**Fixable case:**

```csharp
public sealed class MissingDependency { }

public sealed class MyService : IMyService
{
    public MyService(MissingDependency missing) { }
}

services.AddScoped<IMyService, MyService>();
```

DI015 can offer:

```csharp
services.AddScoped<MissingDependency>();
services.AddScoped<IMyService, MyService>();
```

**Not auto-fixable:** abstractions/interfaces, keyed dependencies, multiple missing dependencies, transitive-only missing leaves, `ServiceDescriptor` registrations, and factory-rooted diagnostics.

**Known exceptions in this rule:** factory-style types, middleware `Invoke`/`InvokeAsync` paths, hosted services, and endpoint filter factories.

---

## DI012: Conditional Registration Misuse

**What it catches:**

- `TryAdd*` calls after an `Add*` already registered that service.
- Duplicate `Add*` registrations where later entries override earlier ones.

DI012 also follows the same `IServiceCollection` flow across local aliases and source-defined helper/local-function wrappers, while treating opaque helper boundaries conservatively instead of guessing at registration order.

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

**Code Fix:** Yes for ignored `TryAdd*` calls that are standalone statements; the fixer removes the redundant ignored registration. Duplicate override cases remain manual.

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

**Code Fix:** Yes. Offers broad assists where the syntax and symbols are local enough to rewrite safely: remove the invalid standalone registration, replace the implementation type with a compatible candidate, or retarget the service type to an interface/base type implemented by the current implementation.

---

## DI014: Root Service Provider Not Disposed

**What it catches:** root providers from `BuildServiceProvider()` that are never disposed, including local providers whose only manual disposal is conditional, catch-only, or after reassignment to another provider.

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

**Code Fix:** Yes. Adds a missing self-binding registration when DI015 can prove a single direct concrete dependency is safe to register. Supports local constructor diagnostics, direct `GetRequiredService<TConcrete>()` factory diagnostics, and keyed self-bindings when the key can be emitted as a C# literal.

### DI015 strict mode

By default, DI015 assumes common host-provided framework services (logging/options/configuration) are available.
Disable that assumption for stricter analysis:

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

DI015 is intentionally conservative to keep false positives low:

- Source-visible `IServiceCollection` wrappers are expanded before DI015 reports missing registrations.
- `[ServiceKey]` parameters and `IEnumerable<T>` are treated as container-provided.
- Parameterless `[FromKeyedServices]` inherits the containing keyed registration key when that key is known.
- `KeyedService.AnyKey` keyed registrations satisfy exact keyed dependency requests.
- Definite same-flow `RemoveAll(...)` and `Replace(...)` mutations suppress diagnostics for registrations they remove.
- Dependency cycles are treated as resolvable.
- Factory registrations without inspectable dependency paths are treated as resolvable.
- `GetService(...)` and dynamic keyed resolutions are treated as optional/unknown.
- If an earlier opaque or external wrapper could have registered services on the same `IServiceCollection` flow, DI015 stays silent instead of speculating.
- If any effective candidate registration is backed by an opaque factory, DI015 stays silent instead of speculating.

---

## DI016: BuildServiceProvider Misuse

**What it catches:** `BuildServiceProvider()` calls while composing registrations (for example in `ConfigureServices`, `IServiceCollection` extension registration methods, registration lambdas, or builder-style `.Services` helper flows).

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
- It recognizes assignable `IServiceCollection` abstractions and same-boundary helper/alias flows from `.Services`, but it does not warn on standalone top-level `new ServiceCollection()` composition roots.

---

## DI017: Circular Dependency

**What it catches:** constructor-injection cycles such as `A -> B -> A`, including longer transitive loops. It stays silent when constructor selection is ambiguous rather than guessing which path the container will choose.

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

**What it catches:** registrations whose implementation type cannot be constructed by the DI container, such as abstract classes, interfaces, static classes, or concrete classes with no public constructors.

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

DI018 also reports abstract classes, interfaces, and static classes used as implementation types.

**Better pattern:**

```csharp
public sealed class GoodConcreteService : IMyService { }

services.AddSingleton<IMyService, GoodConcreteService>();
```

**Code Fix:** No.

---

## DI019: Scoped Service Resolved From Root Provider

**What it catches:** scoped services, or services whose activation graph reaches a scoped service, resolved from a root `IServiceProvider` such as `app.Services`, `host.Services`, or a provider returned by `BuildServiceProvider()`.

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

**Code Fix:** No. Creating the right scope can change control flow and disposal semantics, so the fix should be chosen deliberately.

---

## Samples

- `samples/SampleApp`: diagnostic examples for `DI001` to `DI019`.
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
