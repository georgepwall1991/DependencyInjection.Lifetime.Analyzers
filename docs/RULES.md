# Rule Reference

This document mirrors the full rule guidance in the repository README so the same guidance is available in both places.

For the latest full rule content, see:
- [README.md](../README.md)

---

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

---

## DI001: Service Scope Not Disposed

**What it catches:** `IServiceScope` instances created with `CreateScope()` or `CreateAsyncScope()` that are never disposed, including scopes whose only disposal call is hidden behind a conditional branch, or behind a switch section, loop, or catch block that does not also contain the creation, or after a branch exit that can bypass shared cleanup. Create-and-dispose within the same loop iteration, switch section, or catch clause — the per-message worker shape — stays quiet, but a `continue`/`break` that skips the dispose, or a `yield return`/`yield break` that can strand the scope in a never-resumed iterator, still reports. DI001 recognizes predeclared nullable scope locals assigned conditionally when a later conditional-access, non-null-guarded, same-branch pre-exit, or `finally` disposal reliably closes ownership, and it treats directly returned scopes as caller-owned even through simple casts or conditional return arms. Reassignment leaks and loop-created scopes that need per-iteration disposal still report.

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

**Code Fix:** Yes. Adds `using` / `await using` where possible; the `await using` conversion also rewrites explicitly typed declarations to `var`, because `AsyncServiceScope` boxed to `IServiceScope` cannot be awaited-using.

---

## DI002: Scoped Service Escapes Scope

**What it catches:** a service resolved from a scope that is returned or stored somewhere longer-lived, including services resolved through provider aliases, delegates that capture scoped services and then escape, scopes declared before a later `using (scope)` disposal block, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods. Collection escapes through field/property-held containers (`_cache.Add(service)`, `_byTenant[key] = service`), event subscriptions that bind the scoped service to an owner that outlives the scope (`_publisher.Changed += service.Handle`, captured-delegate handlers), and composite-construction returns (`return (service, count);`, `return new { Service = service };`) are detected too. Wrapped returned resolutions and later-returned locals such as casts, `as` casts, null-forgiving, ternary/coalesce expressions, and non-generic `GetService(typeof(T))` are covered; local containers, scope-local publishers, proven non-escaping scope-local holders including simple direct local holder aliases, pre-resolution locals, and composites consumed inside the scope stay quiet. Holders that later escape through a return, conditional-access slot return, long-lived assignment including null-conditional assignment to a field/property-held receiver, nested receiver path under a fresh wrapper, escaping delegate, returned/stored local container, already-escaped local collection, returned collection alias, or `??=` receiver that may still point at a long-lived holder still report; slot reads before the scoped write stay quiet.

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

**What it catches:** singleton services capturing scoped or transient dependencies, including constructor injection, `IEnumerable<T>` collection captures, known scoped framework services such as `IOptionsSnapshot<T>`, typed HTTP clients registered with `AddHttpClient<TClient>()` / `AddHttpClient<TClient,TImplementation>()`, EF Core contexts and `DbContextOptions<TContext>` registrations from `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, and `AddPooledDbContextFactory(...)` including service/implementation overload self-registrations, and high-confidence factory paths such as inline delegates, stable local delegate factories, method-group factories, `GetServices<T>()`, keyed resolutions, and `ActivatorUtilities.CreateInstance(...)` calls where DI still resolves a scoped or transient constructor parameter. A factory that creates and provably disposes its own scope (`using var scope = sp.CreateScope();`) stays quiet for resolutions through that scope when only derived values flow into the product — one-time scoped setup is not a captive — while an escaping resolved instance or an undisposed factory scope still reports.

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

**Code Fix:** Yes. Rewrites explicit registration lifetimes when the registration syntax is local and unambiguous (for example `AddSingleton`, `TryAddSingleton`, keyed `AddKeyedSingleton`, inline factory registrations, and supported `ServiceDescriptor` forms). The rewrite only ever targets MEDI registration methods — user helpers whose names happen to contain a lifetime token are never renamed.

---

## DI004: Service Used After Scope Disposed

**What it catches:** using a service after the scope that produced it has already ended, including scoped collections from `GetServices<T>()` enumerated after disposal, explicit `Dispose()` / `DisposeAsync()` (including `scope?.Dispose()` for scope locals), wrapped use receivers such as `service!.DoWork()` and `((IService)service).DoWork()`, services resolved from a predeclared scope variable later disposed via `using (scope)`, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods. Uses in branches mutually exclusive with the disposal — whether the dispose is explicit or a `using` statement/declaration — stay quiet, and `out` arguments are writes rather than uses (the rewritten local is fresh afterwards), while `ref` arguments still report.

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

**Code Fix:** Yes. Moves simple immediate invocation-style uses back into the owning scope only when the diagnostic local was assigned in that scope, or adds a narrow pragma suppression for context-dependent cases. The pragma suppression always lands on a line-starting statement, so embedded unbraced statements compile.

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

**What it catches:** `IServiceProvider` / `IServiceScopeFactory` / keyed provider stored in static fields or properties, including common wrappers (`Lazy<T>`, `Task<T>`, `ValueTask<T>`, `Func<T>`, `AsyncLocal<T>`, `ThreadLocal<T>`), mutable/immutable/frozen dictionary value caches, recursive dictionary values such as `Dictionary<string, Lazy<IServiceProvider>>`, and simple holder types that only wrap a provider.

**Why it matters:** global provider state encourages service locator use and muddles lifetime boundaries.

> **Explain Like I'm Ten:** Leaving the school master key in the corridor means anybody can open any door at any time.

**Problem:**

```csharp
public static class Locator
{
    public static IServiceProvider Provider { get; set; } = null!;
    private static readonly Lazy<IServiceProvider> LazyProvider = new(() => Provider);
    private static readonly Dictionary<string, Lazy<IServiceProvider>> LazyTenantProviders = new();
    private static readonly Dictionary<string, IServiceProvider> TenantProviders = new();
    private static readonly ImmutableDictionary<string, IServiceProvider> SnapshotProviders = ImmutableDictionary<string, IServiceProvider>.Empty;
    private static ProviderHolder Holder = null!;
}
```

```csharp
public sealed class ProviderHolder
{
    private readonly IServiceProvider _provider;

    public ProviderHolder(IServiceProvider provider)
    {
        _provider = provider;
    }
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

**Options:** `dotnet_code_quality.DI006.detect_holder_pattern = false` disables the simple holder-type detector if a codebase intentionally uses provider-wrapper types.

---

## DI007: Service Locator Anti-Pattern

**What it catches:** resolving dependencies via `IServiceProvider` inside app logic, including non-generic resolution calls that pass a local `Type` alias initialized from `typeof(...)`.

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

DI007 stays quiet in recognized composition/factory boundaries: DI registration factories, value-returning `Create*`/`Build*` factory methods, ASP.NET Core middleware `Invoke`/`InvokeAsync` methods whose first parameter is `HttpContext`, `BackgroundService.ExecuteAsync`, exact hosted-service lifecycle implementations, options configure/validate implementations, and provider-aware options/factory delegates.

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

DI008 follows generic, `typeof(...)`, keyed, named-argument, typed HTTP client (`AddHttpClient<TClient>()` / `AddHttpClient<TClient,TImplementation>()`), `ServiceDescriptor.Transient(...)`, conditional `services?.Add(ServiceDescriptor.Transient(...))`, `ServiceDescriptor.KeyedTransient(...)`, `ServiceDescriptor.Describe(..., ServiceLifetime.Transient)`, `ServiceDescriptor.DescribeKeyed(..., ServiceLifetime.Transient)`, `new ServiceDescriptor(..., ServiceLifetime.Transient)`, `TryAddTransient`, plain `TryAdd(ServiceDescriptor...)`, `Replace(ServiceDescriptor...)`, and `TryAddEnumerable` registration shapes, including descriptor arrays, lists, and C# collection expressions. Descriptor argument binding uses Roslyn parameters, so keyed descriptor calls whose `serviceKey` is itself a `typeof(...)` expression still report the disposable implementation rather than misreading the key as the implementation. Factory registrations stay quiet because disposal ownership is explicit in user code.

**Code Fix:** Yes. Suggests safer lifetime alternatives and rewrites local descriptor lifetime arguments where the registration is unambiguous.

**Options:** `dotnet_code_quality.DI008.allowed_disposable_types = MyType, My.Namespace.OtherType` suppresses known intentional disposable transients by simple or full type name.

---

## DI009: Open Generic Captive Dependency

**What it catches:** open generic singleton registrations that depend on shorter-lived services, including common registration-shape variants such as `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, keyed open-generic singleton registrations, and `IEnumerable<T>` constructor captures where the element service is shorter-lived.

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

For normal type registrations, DI010 evaluates the public constructor(s) the container could realistically activate instead of every declared constructor — including C# 12 primary constructors. Tied equally-greedy activation constructors report only once at the registration using the highest meaningful dependency count. It also covers straightforward factory registrations that directly return `new MyService(...)`, final-return factory blocks that set up locals before `return new MyService(...)`, and `ActivatorUtilities.CreateInstance<MyService>(sp)`, while staying conservative on branching or dynamic factories. Method-group factories work across files: `services.AddScoped<IMyService>(Factories.Create)` is analyzed through the factory body even when `Factories` is declared in another file.

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

DI012 also follows the same `IServiceCollection` flow across local aliases and source-defined helper/local-function wrappers, while treating opaque helper boundaries conservatively instead of guessing at registration order. Common framework registration helpers such as `AddLogging()`, `AddOptions()`, `Configure<T>()`, `AddMemoryCache()`, `AddHttpClient()`, and `AddHttpContextAccessor()` are transparent rather than opaque barriers, so later user registrations remain visible. It stays quiet for intentional branch-dependent fallbacks such as guarded `Add*` plus unconditional `TryAdd*`, applies `TryAddEnumerable`'s service-and-implementation pair semantics, reports later `TryAdd*` calls when every reachable branch has already registered the service even through wrapped branch exits, and keeps mutually exclusive `if`/`else if`/`else` alternative registrations quiet.
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

For instance-backed registrations (`AddSingleton(typeof(IService), instance)` and the `ServiceDescriptor` equivalents), DI013 only reports when the instance's runtime type is provably known: the argument is an object creation (even through parentheses or upcasts), or its static type is sealed or a value type. A local declared as a base type or interface stays silent — its static type says nothing about the runtime type, and DI013 is the package's only Error-severity rule, so it never reports on code that could be correct.

**Code Fix:** Yes. Offers broad assists where the syntax and symbols are local enough to rewrite safely: remove the invalid block-contained standalone registration, replace the implementation type with a compatible candidate, or retarget the service type to an interface/base type implemented by the current implementation, including invalid implementation-instance registrations. Embedded single-line statement bodies stay manual unless a symbol-backed type rewrite is available. Candidate suggestions never include generic type definitions or structs — both produce registrations that fail to compile or crash at resolution.

---

## DI014: Root Service Provider Not Disposed

**What it catches:** root providers from `BuildServiceProvider()` that are never disposed, including local providers whose only manual disposal is conditional, catch-only, after reassignment to another provider, or after repeated creation inside a loop. Straight-line explicit disposal, standard `Dispose()` to `Dispose(true)` cleanup, and caller-owned return flows are accepted even when the `BuildServiceProvider()` result is parenthesized, same-instance cast, or null-forgiven — including a provider stored in a local and returned later (ownership transfer), and create-and-dispose within the same loop iteration, switch section, or catch clause (a `continue`/`break` that skips the dispose still reports).

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

**Code Fix:** Yes. Adds disposal pattern for simple local declarations with no existing manual disposal code; declared types that do not implement the required disposal interface (e.g. `IServiceProvider`) are rewritten to `var` so the emitted `using` compiles. Conditional or otherwise partial manual-disposal flows stay diagnostic-only so the ownership rewrite remains deliberate.

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

By default, DI015 assumes common host-provided framework services are available, including logging/options/configuration, `ILoggerFactory`, and `IHostApplicationLifetime`. Explicit framework extension calls such as `AddHttpClient()`, `AddMemoryCache()`, and `AddHttpContextAccessor()` are modeled as registrations for `IHttpClientFactory`, `IMemoryCache`, and `IHttpContextAccessor`; those services still report as missing when the matching extension is absent. `TimeProvider` also reports as missing unless registered explicitly. Typed HTTP client registrations treat one constructor `HttpClient` parameter as factory-provided while still checking repeated `HttpClient` parameters and other typed-client constructor dependencies. EF Core contexts registered through `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, or `AddPooledDbContextFactory(...)` are also modeled as registrations, including service/implementation overload self-registrations and the `DbContextOptions<TContext>` and `IDbContextFactory<TContext>` dependencies those patterns require.
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
- It does not report provider-factory methods that intentionally return `IServiceProvider`, concrete provider implementations, or awaited provider results.
- It recognizes assignable `IServiceCollection` abstractions and same-boundary helper/alias flows from `.Services`, but it does not warn on standalone top-level `new ServiceCollection()` composition roots.
- It recognizes metadata-defined `IServiceCollection` fluent chains, so `builder.Services.AddSingleton<...>().BuildServiceProvider()` is treated as the same registration source as `builder.Services.BuildServiceProvider()`.
- Builder `.Services` flows wrapped in the null-forgiving operator (`builder.Services!`) or a same-type cast (`(IServiceCollection)builder.Services`) at the call site, in helper return expressions, or in local-variable initializers are still recognized as registration contexts, while provider-factory methods that wrap the same expression stay silent because they return `IServiceProvider`.
- Conditional-access invocations and aliases such as `builder.Services?.BuildServiceProvider()`, `builder?.Services.BuildServiceProvider()`, and `var services = builder?.Services; services.BuildServiceProvider();` are recognized through the enclosing `ConditionalAccessExpression` and the `MemberBindingExpression`-shaped `.Services` access, so null-safe builder flows participate in detection the same way as direct member access. Provider-factory methods wrapping the same shape stay quiet.

---

## DI017: Circular Dependency

**What it catches:** high-confidence activation cycles such as `A -> B -> A`, including longer transitive loops through constructors, explicit `GetRequiredService` / `GetRequiredKeyedService` factory calls, `ActivatorUtilities` factory construction, keyed-service inheritance, open-generic registrations, exact closed registrations that override open-generic fallbacks, and registered `IEnumerable<T>` elements. It analyzes only reachable service-registration flows and stays silent when constructor selection, factory behavior, keyed lookup, or registration reachability is ambiguous.

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

DI017 intentionally remains conservative:

- It honors source-ordered effective registrations, including duplicate overrides, `TryAdd`, `RemoveAll`, and `Replace` removal semantics.
- It does not report cycles from uninvoked registration helpers, unrelated `IServiceCollection` instances, opaque factory bodies, unregistered optional/default constructor parameters, implementation instances, or ambiguous equally greedy constructors.
- `IEnumerable<T>` parameters are treated as cycle edges only when matching element registrations exist; empty collections stay silent.

**Code Fix:** No. Breaking dependency cycles is a design change.

---

## DI018: Non-Instantiable Implementation Type

**What it catches:** registrations whose implementation type cannot be constructed by the DI container, such as abstract classes, interfaces, static classes, delegate types registered without a factory, or concrete classes with no public constructors.

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

DI018 also reports abstract classes, interfaces, static classes, and delegate types (such as `services.AddSingleton<MyHandler>()` where `MyHandler` is a `delegate`) used as implementation types without a factory expression, including through `ServiceDescriptor` factories, target-typed descriptor construction, stable descriptor locals, and `TryAddEnumerable(ServiceDescriptor...)`. Factory arguments are recognized from the bound delegate parameter even when the expression is an invocation, conditional, coalesce expression, or delegate object creation, so valid factory registrations do not self-bind the service type. Delegates carry only implicit `(object, IntPtr)` and `(object, UIntPtr)` constructors that the default DI container cannot populate, so the registration fails at activation.

**Better pattern:**

```csharp
public sealed class GoodConcreteService : IMyService { }

services.AddSingleton<IMyService, GoodConcreteService>();

// For delegate types, register with a factory expression:
services.AddSingleton<MyHandler>(sp => (msg) => Console.WriteLine(msg));
```

**Code Fix:** No.

---

## DI019: Scoped Service Resolved From Root Provider

**What it catches:** scoped services, known scoped framework services such as `IOptionsSnapshot<T>`, EF Core contexts from `AddDbContext(...)`, `AddDbContextFactory(...)`, `AddDbContextPool(...)`, and `AddPooledDbContextFactory(...)` including service/implementation overload self-registrations, or services whose activation graph reaches a scoped service, resolved from a root `IServiceProvider` such as ASP.NET Core `app.Services`, ASP.NET test-host `factory.Services` / `server.Services`, Generic Host `host.Services`, nullable root-provider surfaces such as `app.Services!`, or a provider returned by `BuildServiceProvider()`.

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

**Code Fix:** Yes. Offers to wrap the resolution in a `using` declaration or block with a new scope.

---

## DI020: Middleware Captures Scoped Service In Constructor

**What it catches:** Scoped services captured by the constructor of a conventional middleware class — both directly (a scoped parameter) and transitively (a parameter whose activation graph reaches a scoped service).

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

**What it catches:** A documented non-thread-safe service (EF Core `DbContext` and derived contexts, `DbConnection`/`DbCommand`/`DbTransaction`/`DbDataReader` and their interfaces, `IDbContextTransaction`, `HttpContext`) created or resolved once and then captured — through a field, a closure over an outer local, or an enclosing method parameter — into a handler that a framework invokes concurrently: `ServiceBusProcessor`/`ServiceBusSessionProcessor` message and error handlers, `EventProcessorClient` event handlers, RabbitMQ `EventingBasicConsumer.Received`/`AsyncEventingBasicConsumer.Received`/`ReceivedAsync` consumer handlers (instance-correlated through the consumer's own factory/connection/channel chain: proven `ConsumerDispatchConcurrency` above 1 warns, proven 1 or a fresh default factory stays silent, untraceable chains stay config-gated), `System.Threading.Timer` callbacks with a finite period, `System.Timers.Timer.Elapsed`, `Parallel.For`/`ForEach`/`ForEachAsync`/`Invoke` bodies, PLINQ `ForAll` bodies (sequential only when `WithDegreeOfParallelism(1)` is proven on the query chain), TPL Dataflow `ActionBlock`/`TransformBlock`/`TransformManyBlock` delegates (sequential by default; reported when `MaxDegreeOfParallelism` is provably above 1, config-gated DI022 when the options are unprovable), and `EventProcessor<TPartition>` batch/error overrides (the override body is the handler; partitions run concurrently). Resolving from a long-lived scope captured from outside the handler is reported too — it hands the same instance to every concurrent invocation.

**Why it matters:** This is the deferred form of the captive dependency. The lifetimes can look correct, but one instance is shared across overlapping invocations and fails at runtime ("A second operation was started on this context instance before a previous operation completed"). It works in development with one message at a time and fails under production load.

> **Explain Like I'm Ten:** One pencil shared by the whole class works fine while pupils write one at a time. The moment everyone writes at once, the pencil snaps.

**Problem:**

```csharp
public class OrderProcessor : BackgroundService
{
    private readonly AppDbContext _db; // resolved once

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleAsync; // invoked concurrently
        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task HandleAsync(ProcessSessionMessageEventArgs args)
    {
        _db.Add(args);                // one DbContext, N concurrent handlers
        await _db.SaveChangesAsync();
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

DI021 stays quiet for scopes created inside the handler, `IDbContextFactory<TContext>` usage, instances created inline, proven-sequential configurations (`MaxConcurrentCalls = 1`, `MaxConcurrentSessions = 1`, `MaxDegreeOfParallelism = 1`, one-shot timers, `AutoReset = false`), and handlers that explicitly serialize themselves (`lock`, `SemaphoreSlim` wait/release in `try`/`finally`, `Interlocked`/`Monitor.TryEnter` reentrancy guards, timer re-arm). Frameworks that already create a scope per message (MassTransit, NServiceBus, Quartz, Hangfire, SignalR, Azure Functions) are deliberately not sinks.

**Code Fix:** Yes. Rewrites the handler to resolve the service from a new scope per invocation, plumbs `IServiceScopeFactory` through the constructor when needed, and removes the now-dead captured field. The plumbing stays deliberate where a rewrite could break the build or runtime: partial types (a constructor or field reference may live in another part), multiple or expression-bodied constructors, and constructors whose parameters or locals already use the `scopeFactory` name are left diagnostic-only.

---

## DI022: Service Instance Reused Across Handler Invocations

**What it catches:** Two tiers. First, the same capture shape as DI021 on a sink whose concurrency is controlled by a configuration knob that cannot be proven at compile time — canonically `ServiceBusProcessor` where `MaxConcurrentCalls` comes from configuration or is left at its default of 1, and RabbitMQ consumers (`EventingBasicConsumer`/`AsyncEventingBasicConsumer`) where `ConsumerDispatchConcurrency` lives on the `ConnectionFactory` several hops from the consumer; a constant above 1 in the containing type upgrades the RabbitMQ report to DI021, but nothing short of that can prove the dispatch pump sequential for a specific consumer. Second, the scoped-lifetime tier: a service outside the non-thread-safe catalog whose effective registration is scoped, captured into any concurrently-invoked handler — the capture itself is the lifetime violation, so the report stays Info regardless of the sink's knob. Singleton-registered and unregistered captures stay silent.

**Why it matters:** If the knob is ever raised above 1 this becomes the DI021 concurrency crash. Even with sequential dispatch, one instance accumulates state across all messages: an EF Core change tracker grows without bound, and a failed `SaveChanges` poisons every subsequent message. DI022 reports at Info severity because the concurrency claim is conditional; raise it per team policy with `dotnet_diagnostic.DI022.severity = warning`. When `MaxConcurrentCalls` is a compile-time constant above 1 the diagnostic upgrades to DI021; when it is provably 1, both rules stay silent. Knob proofs follow same-file non-virtual helper methods that return a fresh options creation (`var options = CreateOptions();`), so concurrency configured in a sibling factory method is proven too; virtual helpers, parameter-driven values, and shared-instance returns stay unproven.

Manually constructed instances are never reported by the scoped tier — the single-origin scan covers field initializers, assignments, and property initializers (`private EmailSender Email { get; } = new EmailSender();`).

**Code Fix:** Yes. Same scope-per-invocation rewrite as DI021.

## DI024: Hosted Service Creates Scope Outside Execution Loop

**What it catches:** Two tiers. First, a `BackgroundService.ExecuteAsync` override or `IHostedService`/`IHostedLifecycleService` start method that creates an `IServiceScope` once before its long-running execution loop (`while (!token.IsCancellationRequested)`, `while (true)`, `for (;;)`, `PeriodicTimer` `WaitForNextTickAsync` loops, and channel-consumer loops — `await foreach` over `ChannelReader<T>.ReadAllAsync(...)` or `while (await reader.WaitToReadAsync(...))`, including channel loops nested inside an outer cancellation loop when the scope is created per outer iteration but spans the unbounded inner drain; `ConfigureAwait(...)`/`WithCancellation(...)` wrappers on any of the awaited shapes are peeled before gating) and uses it inside the loop — directly, through a service resolved from it before the loop, or through a provider alias local (`var sp = scope.ServiceProvider;`) used inside the loop. Declare-then-assign locals (`IServiceScope? scope = null; try { scope = factory.CreateScope(); while (...) ... } finally { scope?.Dispose(); }` — the try/finally ownership pattern) qualify via their pre-loop assignment: the last direct pre-loop write wins, so a creation makes the candidate and a null/default clear (or an unrecognized value) kills it. Second, a service whose effective registration is provably scoped, resolved once before the loop from any provider and reused across iterations. Both tiers also cover fields: a scope (or resolved service) stored in a field qualifies when every assignment to the field is the expected shape and every assignment site is a field initializer, a constructor, or a hosted execution method (`BackgroundService.StartAsync` overrides included); partial types are analyzed across all declarations. Reported at the `CreateScope`/`CreateAsyncScope` or `GetRequiredService` call with the loop as an additional location.

**Why it matters:** The hosted-service idiom is scope per iteration. A hoisted scope keeps the same scoped instances alive for the process lifetime: an EF Core `DbContext` serves stale data and its change tracker grows without bound, and one failed iteration poisons all subsequent ones.

**Guardrails:** Scopes created inside the loop (including inner batch loops reusing the outer iteration's scope), startup scopes consumed entirely before the loop, dispose-and-recreate scopes reassigned inside the loop, hoisted scopes whose every resolution is provably singleton, bounded loops (including plain `foreach` batches and `await foreach` over non-channel sources — a repository-style `ReadAllAsync` is a bounded enumeration, so only `System.Threading.Channels.ChannelReader<T>` sources qualify), shutdown paths (`StopAsync` and the stopping/stopped lifecycle callbacks), hoisted services with unprovable lifetimes, fields assigned anywhere outside field initializers/constructors/execution methods (a helper method may reassign per iteration), locals whose closest pre-loop write is a null/default clear, and provider aliases repointed inside the loop all stay silent.

**Code Fix:** No. Moving the scope into the loop body is a statement-level rewrite with disposal implications; apply it manually.

## DI025: Event Subscription On Longer-Lived Publisher Without Unsubscribe

**What it catches:** A transient- or scoped-registered service that subscribes (`+=`) an instance-capturing handler — an instance method group, a `this`-capturing lambda, or a stored instance-bound delegate field — to an event on a longer-lived publisher and never unsubscribes. Longer-lived publishers are injected dependencies whose registration is provably singleton — closed registrations preferred, open-generic singleton registrations matched for constructed injections — via a constructor parameter or a field/property assigned only from a constructor parameter, and `static` events. A `-=` written with a different lambda instance is recognized as the classic no-op unsubscribe bug: the subscription still reports and the diagnostic points at the ineffective `-=`.

**Why it matters:** the publisher's delegate list holds a strong reference to every handler target, so a singleton publisher roots every subscriber instance the container ever creates — the most common managed memory leak in .NET, plus stale handlers executing against released state on every event raise.

> **Explain Like I'm Ten:** If every visitor ties a balloon to the school gate and nobody ever unties one, the gate ends up dragging a thousand balloons.

**Problem:**

```csharp
services.AddSingleton<IMessageBus, MessageBus>();
services.AddTransient<OrderHandler>();

public class OrderHandler
{
    private readonly IMessageBus _bus;

    public OrderHandler(IMessageBus bus)
    {
        _bus = bus;
        _bus.MessageReceived += OnMessage; // every OrderHandler instance stays rooted
    }

    private void OnMessage(object sender, EventArgs e) { }
}
```

**Better pattern:**

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

**Guardrails:** singleton subscribers stay silent (a population of one cannot grow the delegate list — hosted services subscribing to singleton buses are the canonical safe shape), as do scoped/transient publishers, any matching `-=` anywhere in the type (Dispose, `StopAsync`, teardown methods, the unsubscribe-then-resubscribe idiom) with the same method group — override chains normalized — or the same stored delegate field/local, static handlers and `this`-free lambdas, publishers assigned from `new` or ordinary method parameters, chained receivers (`_dep.Inner.Event`), unregistered subscriber or publisher types, keyed-only publisher registrations, `EventSource`-derived publishers, and factory registrations with unknown implementation types.

**Code Fix:** Yes. When the handler is a method group, the receiver is a field/property or static event, and the type already declares a block-bodied `Dispose()`, `Dispose(bool)`, or `DisposeAsync()` and implements the matching disposal interface (`IDisposable`/`IAsyncDisposable`), the fix inserts the mirrored `-=` at the top of that method. Introducing `IDisposable` on a type that lacks it is intentionally not offered — adding disposability to a transient changes container tracking behavior (see DI008).
