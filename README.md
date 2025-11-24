# DependencyInjection.Lifetime.Analyzers

Roslyn analyzers for detecting dependency injection scope leaks and lifetime mismatches in applications using `Microsoft.Extensions.DependencyInjection`.

[![NuGet](https://img.shields.io/nuget/v/DependencyInjection.Lifetime.Analyzers.svg)](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package DependencyInjection.Lifetime.Analyzers
```

Or via Package Manager Console:

```powershell
Install-Package DependencyInjection.Lifetime.Analyzers
```

The analyzers will automatically run during compilation and in your IDE.

## Diagnostics

| ID | Title | Severity | Code Fix |
|----|-------|----------|----------|
| [DI001](#di001) | Service scope must be disposed | Warning | Yes |
| [DI002](#di002) | Scoped service escapes scope | Warning | No |
| [DI003](#di003) | Captive dependency detected | Warning | Yes |
| [DI004](#di004) | Service used after scope disposed | Warning | No |
| [DI005](#di005) | Use CreateAsyncScope in async methods | Warning | Yes |
| [DI006](#di006) | Avoid caching IServiceProvider in static members | Warning | Yes |
| [DI007](#di007) | Avoid service locator anti-pattern | Warning | No |
| [DI008](#di008) | Transient service implements IDisposable | Warning | Yes |
| [DI009](#di009) | Open generic captive dependency | Warning | Yes |

---

## Diagnostic Details

### DI001

**Service scope must be disposed**

`IServiceScope` implements `IDisposable` and must be disposed to release resources.

```csharp
// Bad - scope is not disposed
public void DoWork()
{
    var scope = _factory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute();
}

// Good - scope is disposed with using
public void DoWork()
{
    using var scope = _factory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    service.Execute();
}
```

**Code Fix**: Add `using` or `await using` statement.

---

### DI002

**Scoped service escapes scope**

Services resolved from a scope should not escape the scope's lifetime by being returned or stored in longer-lived locations.

```csharp
// Bad - scoped service escapes via return
public IMyService GetService()
{
    using var scope = _factory.CreateScope();
    return scope.ServiceProvider.GetRequiredService<IMyService>(); // Escapes!
}
```

---

### DI003

**Captive dependency detected**

A singleton service capturing a scoped or transient dependency keeps that dependency alive for the application's lifetime, defeating its intended lifecycle.

```csharp
// Bad - singleton captures scoped dependency
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton<ISingletonService, SingletonService>(); // SingletonService depends on IScopedService

public class SingletonService : ISingletonService
{
    private readonly IScopedService _scoped; // Captured! Lives forever now
    public SingletonService(IScopedService scoped) => _scoped = scoped;
}
```

**Code Fix**: Change the consumer to `Scoped` or `Transient` lifetime.

---

### DI004

**Service used after scope disposed**

Using a service after its scope has been disposed can cause `ObjectDisposedException`.

```csharp
// Bad - service used after scope disposed
IMyService service;
using (var scope = _factory.CreateScope())
{
    service = scope.ServiceProvider.GetRequiredService<IMyService>();
}
service.Execute(); // Scope is disposed!
```

---

### DI005

**Use CreateAsyncScope in async methods**

In async methods, `CreateAsyncScope` should be used instead of `CreateScope` to ensure proper async disposal.

```csharp
// Bad - CreateScope in async method
public async Task DoWorkAsync()
{
    using var scope = _factory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}

// Good - CreateAsyncScope in async method
public async Task DoWorkAsync()
{
    await using var scope = _factory.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.ExecuteAsync();
}
```

**Code Fix**: Replace `CreateScope()` with `CreateAsyncScope()`.

---

### DI006

**Avoid caching IServiceProvider in static members**

Storing `IServiceProvider` or `IServiceScopeFactory` in static fields can lead to issues with scope management and memory leaks.

```csharp
// Bad - static service provider cache
public class ServiceLocator
{
    private static IServiceProvider _provider; // Anti-pattern!

    public static void Configure(IServiceProvider provider)
    {
        _provider = provider;
    }
}
```

**Code Fix**: Remove `static` modifier.

---

### DI007

**Avoid service locator anti-pattern**

Resolving services via `IServiceProvider.GetService()` hides dependencies and makes code harder to test. Prefer constructor injection.

```csharp
// Bad - service locator pattern
public class MyService
{
    private readonly IServiceProvider _provider;

    public MyService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void DoWork()
    {
        var dependency = _provider.GetRequiredService<IDependency>(); // Hidden dependency
        dependency.Execute();
    }
}

// Good - constructor injection
public class MyService
{
    private readonly IDependency _dependency;

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

**Note**: Service locator is acceptable in factories, middleware `Invoke` methods, and when using `IServiceScopeFactory` correctly.

---

### DI008

**Transient service implements IDisposable**

Transient services implementing `IDisposable` or `IAsyncDisposable` are not tracked by the DI container and will not be disposed, causing memory leaks.

```csharp
// Bad - disposable transient is never disposed by container
services.AddTransient<IMyService, DisposableService>();

public class DisposableService : IMyService, IDisposable
{
    private readonly Stream _stream = new MemoryStream();
    public void Dispose() => _stream.Dispose(); // Never called by container!
}
```

**Code Fix**: Change to `AddScoped` or `AddSingleton`.

---

### DI009

**Open generic captive dependency**

An open generic singleton service should not depend on scoped or transient services.

```csharp
// Bad - open generic singleton captures scoped dependency
services.AddScoped<IScopedService, ScopedService>();
services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));

public class Repository<T> : IRepository<T>
{
    public Repository(IScopedService scoped) { } // Captured!
}
```

**Code Fix**: Change to `AddScoped` or `AddTransient`.

---

## Configuration

### Suppressing Diagnostics

Suppress individual diagnostics using `#pragma` directives:

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

Change diagnostic severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DI003.severity = error  # Treat captive dependencies as errors
dotnet_diagnostic.DI007.severity = suggestion  # Downgrade service locator to suggestion
```

---

## Requirements

- .NET Standard 2.0 compatible (works with .NET Core 2.0+, .NET 5+, .NET Framework 4.6.1+)
- Microsoft.Extensions.DependencyInjection.Abstractions

---

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
