<p align="center">
  <img src="logo.png" alt="DependencyInjection.Lifetime.Analyzers" width="512" height="512">
</p>

# DependencyInjection.Lifetime.Analyzers

**Your Guardian Against DI Scope Leaks and Lifetime Bugs**

Stop memory leaks and `ObjectDisposedException` from reaching production. Compile-time analysis with zero runtime
overhead.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CI](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)
[![Coverage](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/raw/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/actions/workflows/ci.yml)

## Why Use This?

- **Catch bugs at compile time** – No more discovering captive dependencies in production
- **Zero runtime cost** - All analysis happens during compilation
- **Actionable fixes** - Most issues come with automated code fixes
- **Works everywhere** - Visual Studio, Rider, VS Code, and CI builds

## Installation

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers
```

The analyzers will automatically run during compilation and in your IDE.

## Samples

- `samples/SampleApp`: One diagnostics folder per rule (`DI001` through `DI015`) for quick IDE/build inspection.
- `samples/DI015InAction`: Focused runnable sample that shows DI015 failures and the corrected registration path.

## The Rules

| ID                                                    | Title                                 | Severity | Code Fix |
|-------------------------------------------------------|---------------------------------------|----------|----------|
| [DI001](#di001-service-scope-not-disposed)            | Service scope not disposed            | Warning  | Yes      |
| [DI002](#di002-scoped-service-escapes-scope)          | Scoped service escapes scope          | Warning  | Yes      |
| [DI003](#di003-captive-dependency)                    | Captive dependency                    | Warning  | Yes      |
| [DI004](#di004-service-used-after-scope-disposed)     | Service used after scope disposed     | Warning  | No       |
| [DI005](#di005-use-createasyncscope-in-async-methods) | Use CreateAsyncScope in async methods | Warning  | Yes      |
| [DI006](#di006-static-serviceprovider-cache)          | Static ServiceProvider cache          | Warning  | Yes      |
| [DI007](#di007-service-locator-anti-pattern)          | Service locator anti-pattern          | Warning  | No       |
| [DI008](#di008-disposable-transient-service)          | Disposable transient service          | Warning  | Yes      |
| [DI009](#di009-open-generic-captive-dependency)       | Open generic captive dependency       | Warning  | Yes      |
| [DI010](#di010-constructor-over-injection)            | Constructor over-injection            | Info     | No       |
| [DI011](#di011-serviceprovider-injection)             | ServiceProvider injection             | Warning  | No       |
| [DI012](#di012-conditional-registration-misuse)       | Conditional registration misuse       | Info     | No       |
| [DI013](#di013-implementation-type-mismatch)          | Implementation type mismatch          | Error    | No       |
| [DI014](#di014-root-service-provider-not-disposed)    | Root service provider not disposed    | Warning  | Yes      |
| [DI015](#di015-unresolvable-dependency)               | Unresolvable dependency               | Warning  | No       |

---

## DI001: Service Scope Not Disposed

`IServiceScope` implements `IDisposable` and must be disposed to release resources. Forgetting to dispose a scope causes
memory leaks.

> **Explain Like I'm Ten:** It's like borrowing a library book and never returning it. Eventually, the library runs out
> of books and no one can borrow anything.

**The Problem:**

```csharp
public void DoWork()
{
    var scope = _factory.CreateScope(); // Scope is never disposed!
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute();
}
```

**The Solution:**

```csharp
public void DoWork()
{
    using var scope = _factory.CreateScope(); // Disposed automatically
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute();
}
```

**Code Fix:** Yes - Adds `using` or `await using` statement

---

## DI002: Scoped Service Escapes Scope

Services resolved from a scope should not outlive that scope. Returning or storing a scoped service causes it to be used
after disposal.

> **Explain Like I'm Ten:** It's like taking a rental car outside the coverage area. The roadside assistance won't work
> where you're going, and you'll be stranded.

**The Problem:**

```csharp
public IMyService GetService()
{
    using var scope = _factory.CreateScope();
    return scope.ServiceProvider.GetRequiredService<IMyService>(); // Escapes the scope!
}
```

**The Solution:**

```csharp
// Option 1: Keep scope and service together
public (IServiceScope Scope, IMyService Service) GetService()
{
    var scope = _factory.CreateScope();
    return (scope, scope.ServiceProvider.GetRequiredService<IMyService>());
}

// Option 2: Redesign to use the service within the scope
public void UseService()
{
    using var scope = _factory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute(); // Use it here, don't return it
}
```

**Code Fix:** Yes - Suppress with `#pragma` or add TODO comment

---

## DI003: Captive Dependency

A singleton service capturing a scoped or transient dependency keeps that dependency alive forever, defeating its
intended lifecycle.

> **Explain Like I'm Ten:** It's like a hoarder keeping recyclables in their house forever. Those cans should be
> refreshed each week, but now they're stuck there for life.

**The Problem:**

```csharp
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton<ISingletonService, SingletonService>();

public class SingletonService : ISingletonService
{
    private readonly IScopedService _scoped; // Captured! Lives forever now

    public SingletonService(IScopedService scoped)
    {
        _scoped = scoped;
    }
}

// Also detected in factory registrations:
services.AddSingleton<ISingletonService>(sp => 
    new SingletonService(sp.GetRequiredService<IScopedService>())); // Detected!
```

**The Solution:**

```csharp
// Option 1: Change the singleton to scoped
services.AddScoped<ISingletonService, SingletonService>();

// Option 2: Inject IServiceScopeFactory instead
public class SingletonService : ISingletonService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SingletonService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void DoWork()
    {
        using var scope = _scopeFactory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<IScopedService>();
        // Use scoped service here
    }
}
```

**Code Fix:** Yes - Changes service lifetime to `Scoped` or `Transient`

---

## DI004: Service Used After Scope Disposed

Using a service after its scope has been disposed causes `ObjectDisposedException` at runtime.

> **Explain Like I'm Ten:** It's like trying to use your hotel room key after you've checked out. The key worked before,
> but now the room belongs to someone else.

**The Problem:**

```csharp
IMyService service;
using (var scope = _factory.CreateScope())
{
    service = scope.ServiceProvider.GetRequiredService<IMyService>();
}
service.Execute(); // Scope is disposed! This may throw ObjectDisposedException
```

**The Solution:**

```csharp
using (var scope = _factory.CreateScope())
{
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute(); // Use the service while the scope is still alive
}
```

**Code Fix:** No - Requires manual refactoring

---

## DI005: Use CreateAsyncScope in Async Methods

In async methods, `CreateAsyncScope` ensures services implementing `IAsyncDisposable` are disposed correctly.

> **Explain Like I'm Ten:** It's like using a regular trash can for special recyclables in an eco-building. Async
> methods need the async recycling bin to handle things properly.

**The Problem:**

```csharp
public async Task DoWorkAsync()
{
    using var scope = _factory.CreateScope(); // Wrong disposal pattern for async!
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}
```

**The Solution:**

```csharp
public async Task DoWorkAsync()
{
    await using var scope = _factory.CreateAsyncScope(); // Proper async disposal
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}
```

**Code Fix:** Yes - Replaces `CreateScope()` with `CreateAsyncScope()`

---

## DI006: Static ServiceProvider Cache

Storing `IServiceProvider` or `IServiceScopeFactory` in static fields creates global state that causes scope management
issues and memory leaks.

> **Explain Like I'm Ten:** It's like leaving the master key to your building in the lobby. Anyone can grab it and cause
> chaos.

**The Problem:**

```csharp
public class ServiceLocator
{
    private static IServiceProvider _provider; // Global state!

    public static void Configure(IServiceProvider provider)
    {
        _provider = provider;
    }

    public static T GetService<T>() => _provider.GetRequiredService<T>();
}
```

**The Solution:**

```csharp
public class ServiceLocator
{
    private readonly IServiceProvider _provider; // Instance field, not static

    public ServiceLocator(IServiceProvider provider)
    {
        _provider = provider;
    }
}
```

**Code Fix:** Yes - Removes `static` modifier

---

## DI007: Service Locator Anti-Pattern

Resolving services via `IServiceProvider.GetService()` hides dependencies and makes code harder to test. Prefer
constructor injection.

> **Explain Like I'm Ten:** It's like going to the store every time you need milk instead of keeping it in your fridge.
> Your friends won't know you need milk until they see you leave.

**The Problem:**

```csharp
public class MyService
{
    private readonly IServiceProvider _provider;

    public MyService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void DoWork()
    {
        var dependency = _provider.GetRequiredService<IDependency>(); // Hidden dependency!
        dependency.Execute();
    }
}
```

**The Solution:**

```csharp
public class MyService
{
    private readonly IDependency _dependency;

    public MyService(IDependency dependency) // Explicit dependency
    {
        _dependency = dependency;
    }

    public void DoWork()
    {
        _dependency.Execute();
    }
}
```

**Code Fix:** No - Requires manual refactoring

**Note:** Service locator is acceptable in factories, middleware `Invoke` methods, and when using `IServiceScopeFactory`
correctly.

---

## DI008: Disposable Transient Service

Transient services implementing `IDisposable` are not tracked by the DI container. The container won't dispose them,
causing memory leaks.

> **Explain Like I'm Ten:** It's like buying disposable plates but never throwing them away. You keep getting new ones,
> but the pile in the corner just keeps growing.

**The Problem:**

```csharp
services.AddTransient<IMyService, DisposableService>();

public class DisposableService : IMyService, IDisposable
{
    private readonly Stream _stream = new MemoryStream();

    public void Dispose() => _stream.Dispose(); // Never called by container!
}
```

**The Solution:**

```csharp
// Option 1: Use Scoped lifetime (container will dispose it)
services.AddScoped<IMyService, DisposableService>();

// Option 2: Use Singleton lifetime (disposed at app shutdown)
services.AddSingleton<IMyService, DisposableService>();

// Option 3: Manually dispose in consuming code
public class Consumer
{
    public void UseService(IMyService service)
    {
        try { /* use service */ }
        finally { (service as IDisposable)?.Dispose(); }
    }
}
```

**Code Fix:** Yes - Changes to `AddScoped` or `AddSingleton`

---

## DI009: Open Generic Captive Dependency

Same as DI003, but for open generic registrations. A singleton generic service should not depend on scoped or transient
services.

> **Explain Like I'm Ten:** Same hoarding problem as DI003, but it's a factory that produces hoarders. Every
`Repository<T>` you create will hoard the scoped service.

**The Problem:**

```csharp
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));

public class Repository<T> : IRepository<T>
{
    public Repository(IScopedService scoped) { } // Captured by every Repository<T>!
}
```

**The Solution:**

```csharp
// Change the open generic to scoped
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
```

**Code Fix:** Yes - Changes to `AddScoped` or `AddTransient`

---

## DI010: Constructor Over-Injection

A class with too many constructor dependencies may be violating the Single Responsibility Principle (SRP) and should be
refactored.

> **Explain Like I'm Ten:** It's like having a toy that needs 10 batteries. That toy is probably trying to do too many
> things at once. Better to have simpler toys that need fewer batteries.

**The Problem:**

```csharp
services.AddScoped<IMyService, MyService>();

public class MyService : IMyService
{
    public MyService(
        IDependency1 dep1,
        IDependency2 dep2,
        IDependency3 dep3,
        IDependency4 dep4,
        IDependency5 dep5) // 5+ dependencies - may violate SRP
    {
        // ...
    }
}
```

**The Solution:**

```csharp
// Split into smaller, focused services
public class MyService : IMyService
{
    public MyService(
        ISubServiceA subA,  // Groups dep1 and dep2
        ISubServiceB subB)  // Groups dep3, dep4, and dep5
    {
    }
}

public class SubServiceA : ISubServiceA
{
    public SubServiceA(IDependency1 dep1, IDependency2 dep2) { }
}

public class SubServiceB : ISubServiceB
{
    public SubServiceB(IDependency3 dep3, IDependency4 dep4, IDependency5 dep5) { }
}
```

**Code Fix:** No - Requires manual refactoring to split responsibilities

**Note:** Common dependencies like `ILogger<T>`, `IOptions<T>`, `IConfiguration`, and value types are excluded from the
count.

---

## DI011: ServiceProvider Injection

Injecting `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider` directly enables the service locator
anti-pattern and hides dependencies.

> **Explain Like I'm Ten:** It's like asking for a magic wand that can summon anything. Your friends don't know what you
> actually need until you start summoning things, and then they can't help you prepare.

**The Problem:**

```csharp
services.AddScoped<IMyService, MyService>();

public class MyService : IMyService
{
    private readonly IServiceProvider _provider;  // Hidden dependencies!

    public MyService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void DoWork()
    {
        var dep = _provider.GetRequiredService<IDependency>();
        dep.Execute();
    }
}
```

**The Solution:**

```csharp
public class MyService : IMyService
{
    private readonly IDependency _dependency;  // Explicit dependency

    public MyService(IDependency dependency)
    {
        _dependency = dependency;
    }

    public void DoWork()
    {
        _dependency.Execute();
    }
}
```

**Code Fix:** No - Requires manual refactoring

**Exceptions:** This rule excludes:
- Factory classes (name ends with "Factory")
- Middleware classes (has `Invoke` or `InvokeAsync` method)

---

## DI012: Conditional Registration Misuse

Detects issues with conditional registration methods (`TryAdd*`) and duplicate registrations.

> **Explain Like I'm Ten:** It's like signing up for the same newsletter twice with different email addresses. One of
> them will be ignored, and you might not get the emails you expected.

**The Problem:**

```csharp
// DI012: TryAdd after Add - the TryAdd is silently ignored
services.AddSingleton<IMyService, ServiceA>();
services.TryAddSingleton<IMyService, ServiceB>(); // Will be ignored!

// DI012b: Duplicate Add - later registration overrides earlier
services.AddSingleton<IMyService, ServiceA>();
services.AddSingleton<IMyService, ServiceB>(); // ServiceA registration is lost!
```

**The Solution:**

```csharp
// Use TryAdd first if you want "register if not exists" behavior
services.TryAddSingleton<IMyService, ServiceA>();

// Or be explicit about overriding
services.AddSingleton<IMyService, ServiceB>(); // Intentionally overrides
```

**Code Fix:** No - Requires understanding of intended behavior

---

## DI013: Implementation Type Mismatch

When registering services using `typeof`, the compiler cannot check generic constraints. If the implementation type
does not implement the service type, `AddSingleton` throws an exception at runtime.

> **Explain Like I'm Ten:** It's like trying to put a square peg in a round hole. The instruction manual (compiler) usually warns you, but this time you threw away the manual.

**The Problem:**

```csharp
public interface IRepository { }
public class WrongType { } // Does not implement IRepository

// Compiler allows this, but it throws ArgumentException at runtime!
services.AddSingleton(typeof(IRepository), typeof(WrongType));
```

**The Solution:**

```csharp
public class SqlRepository : IRepository { }

// Correct type provided
services.AddSingleton(typeof(IRepository), typeof(SqlRepository));
```

**Code Fix:** No - Requires correcting the types

---

## DI014: Root Service Provider Not Disposed

The root `IServiceProvider` created by `BuildServiceProvider()` implements `IDisposable`. If it is not disposed,
any singleton services implementing `IDisposable` will not be disposed, causing resource leaks.

> **Explain Like I'm Ten:** It's like locking the main door of the school but forgetting to turn off the lights. 
> The lights stay on forever and waste electricity.

**The Problem:**

```csharp
var services = new ServiceCollection();
// ... register services ...

// Provider created but never disposed
var provider = services.BuildServiceProvider();
var service = provider.GetRequiredService<IMyService>();
```

**The Solution:**

```csharp
// Dispose via 'using'
using var provider = services.BuildServiceProvider();
var service = provider.GetRequiredService<IMyService>();
```

**Code Fix:** Yes - Adds `using` statement

---

## DI015: Unresolvable Dependency

When a registered service depends on another service that was never registered, service resolution fails at runtime
with an `InvalidOperationException`.

> **Explain Like I'm Ten:** You packed a lunchbox with a sandwich recipe, but never bought bread. Lunch prep fails when you actually try to make it.

**The Problem:**

```csharp
public interface IMyService { }
public interface IMissingDependency { }

public class MyService : IMyService
{
    public MyService(IMissingDependency dependency) { } // Not registered
}

services.AddSingleton<IMyService, MyService>(); // Fails when resolved
```

**The Solution:**

```csharp
public class MissingDependency : IMissingDependency { }

services.AddScoped<IMissingDependency, MissingDependency>();
services.AddSingleton<IMyService, MyService>();
```

**Code Fix:** No - Requires registering the dependency or changing constructor/factory design

See a runnable end-to-end example in `samples/DI015InAction`.

**DI015 Strict Mode (Optional):**

By default, DI015 assumes common host-provided framework services (like logging/options/configuration) are available.
You can disable this assumption for stricter analysis:

```ini
[*.cs]
dotnet_code_quality.DI015.assume_framework_services_registered = false
```

---

## Configuration

### Suppressing Diagnostics

```csharp
#pragma warning disable DI007 // Service locator is intentional here
var service = _provider.GetRequiredService<IMyService>();
#pragma warning restore DI007
```

Or in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI007.severity = none
```

### Changing Severity

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error      # Treat captive dependencies as errors
dotnet_diagnostic.DI007.severity = suggestion # Downgrade service locator to suggestion
```

---

## Requirements

- .NET Standard 2.0+ (works with .NET Core 2.0+, .NET 5+, .NET Framework 4.6.1+)
- Microsoft.Extensions.DependencyInjection

## Known Limitations

- **Compile-time only** - Runtime registrations cannot be analyzed
- **Single compilation** - Cross-assembly registrations are not tracked

---

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

MIT License - see [LICENSE](LICENSE) for details.
