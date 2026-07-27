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
| [DI023](#di023-fire-and-forget-background-work-captures-a-scope) | Fire-and-forget background work captures a scope | Warning | No |
| [DI024](#di024-hosted-service-creates-scope-outside-execution-loop) | Hosted service creates scope outside execution loop | Warning | No |
| [DI025](#di025-event-subscription-on-longer-lived-publisher-without-unsubscribe) | Event subscription on longer-lived publisher without unsubscribe | Warning | Yes |
| [DI026](#di026-event-subscription-on-scoped-publisher-without-unsubscribe) | Event subscription on scoped publisher without unsubscribe | Info | Yes |
| [DI027](#di027-rx-subscription-on-longer-lived-observable-without-dispose) | Rx subscription on longer-lived observable without dispose | Warning | No |
| [DI028](#di028-discarded-callback-registration-on-a-longer-lived-source) | Discarded callback registration on a longer-lived source | Warning | No |
| [DI029](#di029-httpclient-lifetime-misuse) | HttpClient lifetime misuse | Warning | No |
| [DI030](#di030-unbounded-singleton-or-static-cache) | Unbounded singleton or static cache | Info | No |
| [DI031](#di031-shared-implementation-registered-under-several-service-types) | Shared implementation registered under several service types | Info | No |
| [DI032](#di032-service-implements-only-iasyncdisposable) | Service implements only IAsyncDisposable | Warning | No |
| [DI033](#di033-container-will-not-dispose-a-pre-built-instance) | Container will not dispose a pre-built instance | Info | No |
| [DI034](#di034-httpcontext-used-in-fire-and-forget-background-work) | HttpContext used in fire-and-forget background work | Warning | No |
| [DI035](#di035-non-thread-safe-service-shared-across-a-fan-out) | Non-thread-safe service shared across a fan-out | Warning | No |

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

**What it catches:** a service resolved from a scope that is returned or stored somewhere longer-lived, including services resolved through provider aliases, delegates that capture scoped services and then escape, scopes declared before a later `using (scope)` disposal block, and the same patterns inside constructors, accessors, local functions, lambdas, and anonymous methods. Collection escapes through field/property-held containers (`_cache.Add(service)`, `_byTenant[key] = service`, `_cache.GetOrAdd(key, service)` and its value-factory spelling `_cache.GetOrAdd(key, _ => resolution)`, `_cache.AddOrUpdate(...)`) and caller-owned collection parameters (`destination.Add(service)`), including caller-visible `ref`/`out` replacements, event subscriptions that bind the scoped service to an owner that outlives the scope (`_publisher.Changed += service.Handle`, captured-delegate handlers), and composite-construction returns (`return (service, count);`, `return new { Service = service };`) are detected too. Wrapped returned resolutions and later-returned locals such as casts, `as` casts, null-forgiving, ternary/coalesce expressions, and non-generic `GetService(typeof(T))` are covered; local containers, by-value parameters definitely replaced with fresh collections that remain local, scope-local publishers, proven non-escaping scope-local holders including simple direct local holder aliases, pre-resolution locals, and composites consumed inside the scope stay quiet. A fresh parameter replacement that is stored into longer-lived state, returned after mutation, or exposed through a direct local alias or `ref`/`out` still reports. Holders that later escape through a return, conditional-access slot return, long-lived assignment including null-conditional assignment to a field/property-held receiver, nested receiver path under a fresh wrapper, escaping delegate, returned/stored local container, already-escaped local collection, returned collection alias, or `??=` receiver that may still point at a long-lived holder still report; slot reads before the scoped write stay quiet.

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

The shared registration model also recognizes `IServiceCollection.Insert(0, ServiceDescriptor...)`, including reordered named arguments and the concrete framework `ServiceCollection` implementation. It evaluates ordinary additions after prepends and reverses repeated prepend operations to match the runtime descriptor list. Nonzero or dynamic insert indexes remain deliberately silent because unmodelled descriptors make absolute positions unsafe to infer; source-defined concrete `Insert` bodies stay silent because an implementation can remap the interface member to arbitrary behavior.

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

**What it catches:** root providers from `BuildServiceProvider()` that are never disposed, including local providers whose only manual disposal is conditional, catch-only, after reassignment to another provider, or after repeated creation inside a loop. Straight-line explicit disposal, standard `Dispose()` to `Dispose(true)` cleanup, and caller-owned return flows are accepted even when the `BuildServiceProvider()` result is parenthesized, same-instance cast, null-forgiven, selected by a ternary arm, or supplied by a null-coalescing operand — including a provider stored in a local and returned later (ownership transfer), and create-and-dispose within the same loop iteration, switch section, or catch clause (a `continue`/`break` that skips the dispose still reports). A `using` declaration or statement proves cleanup only when that same provider instance reaches its resource expression. User-defined conversions remain reportable because they may produce a different instance, including a disposable wrapper selected by a coalesce inside `using`.

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

**What it catches:** high-confidence activation cycles such as `A -> B -> A`, including longer transitive loops through constructors, explicit `GetRequiredService` / `GetRequiredKeyedService` factory calls, `ActivatorUtilities` factory construction, keyed-service inheritance, open-generic registrations, exact closed registrations that override open-generic fallbacks, and registered `IEnumerable<T>` elements. It analyzes only reachable service-registration flows and mirrors the default container's constructor-set rule: equivalent reordered greedy constructors expose the same cycle, while a greediest constructor whose resolved service identifiers (type plus key) are not a superset of every other resolvable constructor stays silent as ambiguous.

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
- It does not report cycles from uninvoked registration helpers, unrelated `IServiceCollection` instances, opaque factory bodies, unregistered optional/default constructor parameters, implementation instances, or resolvable constructor sets with no service-identifier superset.
- `IEnumerable<T>` parameters are treated as cycle edges only when matching element registrations exist; empty collections stay silent.

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

DI018 also reports abstract classes, interfaces, static classes, delegate types (such as `services.AddSingleton<MyHandler>()` where `MyHandler` is a `delegate`), default structs, and enums used as implementation types without a factory expression, including through `ServiceDescriptor` factories, target-typed descriptor construction, stable descriptor locals, and `TryAddEnumerable(ServiceDescriptor...)`. The default container activates implementation types through public constructors returned by reflection: Roslyn's synthetic value-type constructor is not emitted as constructor metadata, so a default struct or enum fails at first resolution. A struct with an explicitly declared public constructor remains valid. Factory arguments are recognized from the bound delegate parameter even when the expression is an invocation, conditional, coalesce expression, or delegate object creation, so valid factory registrations do not self-bind the service type. Delegates carry only implicit `(object, IntPtr)` and `(object, UIntPtr)` constructors that the default DI container cannot populate, so the registration fails at activation.

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

**What it catches:** A documented non-thread-safe service (EF Core `DbContext` and derived contexts, `DbConnection`/`DbCommand`/`DbTransaction`/`DbDataReader` and their interfaces, `IDbContextTransaction`, `HttpContext`) created or resolved once and then captured — through a field, a closure over an outer local, or an enclosing method parameter — into a handler that a framework invokes concurrently: `ServiceBusProcessor`/`ServiceBusSessionProcessor` message and error handlers, `EventProcessorClient` event handlers, RabbitMQ `EventingBasicConsumer.Received`/`AsyncEventingBasicConsumer.Received`/`ReceivedAsync` consumer handlers (instance-correlated through the consumer's own factory/connection/channel chain: proven `ConsumerDispatchConcurrency` above 1 warns, proven 1 or a fresh default factory stays silent, untraceable chains stay config-gated; fallback constants must bind to the real RabbitMQ property), `System.Threading.Timer` callbacks with a finite period, `System.Timers.Timer.Elapsed`, `Parallel.For`/`ForEach`/`ForEachAsync`/`Invoke` bodies, PLINQ `ForAll` bodies (sequential only when `WithDegreeOfParallelism(1)` is proven on the query chain), TPL Dataflow `ActionBlock`/`TransformBlock`/`TransformManyBlock` delegates (sequential by default; reported when `MaxDegreeOfParallelism` is provably above 1, config-gated DI022 when the options are unprovable), and `EventProcessor<TPartition>` batch/error overrides (the override body is the handler; partitions run concurrently). Resolving from a long-lived scope captured from outside the handler is reported too — it hands the same instance to every concurrent invocation. Both generic requests and exact framework non-generic `IServiceProvider.GetService(typeof(T))` / `GetRequiredService(typeof(T))` requests participate; direct-static calls bind the provider by declared parameter only for exact framework extension containers, and concrete provider implementations bind the `System.Type` contract parameter regardless of its source name. Built-in identity, reference, and boxing conversions remain transparent while tracing provider origins, preserving coverage for captured value-type and `IServiceProvider`-constrained providers; cyclic constraint graphs in temporarily invalid source are bounded by symbol identity. Runtime `Type` values, user-defined conversions on the requested type or provider receiver, and user-defined same-named helpers remain conservative and silent.

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

DI021 stays quiet for scopes created inside the handler, `IDbContextFactory<TContext>` usage, instances created inline, proven-sequential configurations (`MaxConcurrentCalls = 1`, `MaxConcurrentSessions = 1`, `MaxDegreeOfParallelism = 1`, one-shot timers, `AutoReset = false`), and handlers that explicitly serialize themselves (`lock` on a stable monitor shared from outside the handler, `SemaphoreSlim` wait/release in `try`/`finally`, `Interlocked`/`Monitor.TryEnter` reentrancy guards, timer re-arm). Locking the handler's own parameter, an object created inside the handler, or a shared monitor reassigned by the handler does not serialize separate invocations and still reports. Frameworks that already create a scope per message (MassTransit, NServiceBus, Quartz, Hangfire, SignalR, Azure Functions) are deliberately not sinks.

**Code Fix:** Yes. Rewrites the handler to resolve the service from a new scope per invocation, plumbs `IServiceScopeFactory` through the constructor when needed, and removes the now-dead captured field. The plumbing stays deliberate where a rewrite could break the build or runtime: partial types (a constructor or field reference may live in another part), multiple or expression-bodied constructors, and constructors whose parameters or locals already use the `scopeFactory` name are left diagnostic-only.

---

## DI022: Service Instance Reused Across Handler Invocations

**What it catches:** Two tiers. First, the same capture shape as DI021 on a sink whose concurrency is controlled by a configuration knob that cannot be proven at compile time — canonically `ServiceBusProcessor` where `MaxConcurrentCalls` comes from configuration or is left at its default of 1, and RabbitMQ consumers (`EventingBasicConsumer`/`AsyncEventingBasicConsumer`) where `ConsumerDispatchConcurrency` lives on the `ConnectionFactory` several hops from the consumer; a constant above 1 on the actual SDK property upgrades the report to DI021, while unrelated same-named user properties do not. Second, the scoped-lifetime tier: a service outside the non-thread-safe catalog whose effective registration is scoped, captured into any concurrently-invoked handler — the capture itself is the lifetime violation, so the report stays Info regardless of the sink's knob. Singleton-registered and unregistered captures stay silent.

**Why it matters:** If the knob is ever raised above 1 this becomes the DI021 concurrency crash. Even with sequential dispatch, one instance accumulates state across all messages: an EF Core change tracker grows without bound, and a failed `SaveChanges` poisons every subsequent message. DI022 reports at Info severity because the concurrency claim is conditional; raise it per team policy with `dotnet_diagnostic.DI022.severity = warning`. When `MaxConcurrentCalls` is a compile-time constant above 1 the diagnostic upgrades to DI021; when it is provably 1, both rules stay silent. Knob proofs follow same-file non-virtual helper methods that return a fresh options creation (`var options = CreateOptions();`), so concurrency configured in a sibling factory method is proven too; virtual helpers, parameter-driven values, and shared-instance returns stay unproven.

Manually constructed instances are never reported by the scoped tier — the single-origin scan covers field initializers, assignments, and property initializers (`private EmailSender Email { get; } = new EmailSender();`).

**Code Fix:** Yes. Same scope-per-invocation rewrite as DI021.

## DI023: Fire-and-Forget Background Work Captures a Scope

**What it catches:** a `using` scope, a local bound to its `ServiceProvider`, or any local resolved from it, captured by background work started with `Task.Run` or `TaskFactory.StartNew` whose task is thrown away — an expression statement, a `_ =` discard, or a finite/cancelable `Wait(...)` whose Boolean result is stored or returned while the task may continue.

**Why it matters:** `using` disposes the scope the instant the starting method returns, which for a discarded task is almost always before the work has finished. The background work then resolves from, or calls into, a disposed scope: `ObjectDisposedException` at best, and at worst a service that quietly operates on torn-down state such as a closed `DbContext` connection. The failure is timing-dependent, so it passes locally and fails under load.

**Problem:**

```csharp
public void Handle(int orderId)
{
    using var scope = _scopeFactory.CreateScope();
    var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();

    _ = Task.Run(async () => await archiver.ArchiveAsync(orderId));  // DI023
}   // <- scope disposed here, while ArchiveAsync is still running
```

**Better pattern:** give the background work a scope of its own, or await it before leaving.

```csharp
public void Handle(int orderId)
{
    _ = Task.Run(async () =>
    {
        using var scope = _scopeFactory.CreateScope();
        var archiver = scope.ServiceProvider.GetRequiredService<IOrderArchiver>();
        await archiver.ArchiveAsync(orderId);
    });
}
```

**Guardrails:** capture tracking follows any number of hops (`scope` → `provider` → `service`) and covers method groups and delegate locals, not just inline lambdas. Values that cannot hold the scope's graph stay quiet: primitives, enums, and strings derived from it (`scope.GetHashCode()`), a local proved to be reassigned to something not scope-derived before the capture (same-block dominance; a branch-only or conditionally evaluated overwrite still reports), assignment targets, and `nameof(service)`. The scope must be disposed by a `using` in the same method — an undisposed scope has no proven teardown point here and is DI001's finding instead. A task that is awaited, returned, stored in a local, or waited on to guaranteed completion (parameterless `.Wait()`, infinite timeout with a non-cancelable token, `.GetAwaiter().GetResult()`) keeps the frame alive and stays silent. A framework `Task.Wait` with a finite timeout or cancelable token does not, even when its Boolean result is stored or returned, since either exit can leave the task running; arguments are bound to their parameters, so named and reordered forms classify correctly. User-defined extension methods named `Wait` and background work that captures nothing scope-derived stay silent.

**Code Fix:** No — the repair is a design choice between awaiting the task and moving scope creation inside the background work, and the two produce different execution semantics.

---

## DI024: Hosted Service Creates Scope Outside Execution Loop

**What it catches:** Two tiers. First, a `BackgroundService.ExecuteAsync` override or `IHostedService`/`IHostedLifecycleService` start method that creates an `IServiceScope` once before its long-running execution loop (`while (!token.IsCancellationRequested)`, compound cancellation conditions, `while (true)`, `for (;;)`, `PeriodicTimer` `WaitForNextTickAsync` loops, and channel-consumer loops — `await foreach` over `ChannelReader<T>.ReadAllAsync(...)` or `while (await reader.WaitToReadAsync(...))`, including channel loops nested inside an outer cancellation loop when the scope is created per outer iteration but spans the unbounded inner drain; `ConfigureAwait(...)`/`WithCancellation(...)` wrappers on any of the awaited shapes are peeled before gating) and uses it inside the loop — directly, through a service resolved from it before the loop, or through a provider alias local (`var sp = scope.ServiceProvider;`) used inside the loop. The same helper-local analysis follows one-hop, directly invoked private helpers declared on the same type; field candidates stay confined to true hosted entry points. Generic resolutions and the framework's direct-`typeof(T)` non-generic `GetService`/`GetRequiredService` forms participate, including keyed `GetKeyedService`/`GetRequiredKeyedService` calls whose service key is compile-time known, plus casted and null-forgiving results; runtime `Type` values, dynamic keys, and user-defined same-named methods remain unproven. Compound conditions are evaluated conservatively: nested `!` operators are reduced by polarity, every `&&` operand must be long-running because any operand can bound the loop, while one long-running `||` operand is sufficient; negated cancellation combinations use De Morgan semantics. Declare-then-assign locals (`IServiceScope? scope = null; try { scope = factory.CreateScope(); while (...) ... } finally { scope?.Dispose(); }` — the try/finally ownership pattern) qualify via their pre-loop assignment: the last direct pre-loop write wins, so a creation makes the candidate and a null/default clear (or an unrecognized value) kills it. Second, a service whose effective registration is provably scoped, resolved once before the loop from any provider and reused across iterations. Both tiers also cover fields: a scope (or resolved service) stored in a field qualifies when every assignment to the field is the expected shape and every assignment site is a field initializer, a constructor, or a hosted execution method (`BackgroundService.StartAsync` overrides included); partial types are analyzed across all declarations. Reported at the `CreateScope`/`CreateAsyncScope` or service-resolution call with the loop as an additional location.

**Why it matters:** The hosted-service idiom is scope per iteration. A hoisted scope keeps the same scoped instances alive for the process lifetime: an EF Core `DbContext` serves stale data and its change tracker grows without bound, and one failed iteration poisons all subsequent ones.

**Guardrails:** Scopes created inside the loop (including inner batch loops reusing the outer iteration's scope), startup scopes consumed entirely before the loop, dispose-and-recreate scopes reassigned inside the loop, hoisted scopes whose every resolution is provably singleton (including keyed singletons matched by compile-time key), bounded loops (including cancellation-plus-counter conjunctions, plain `foreach` batches, and `await foreach` over non-channel sources — a repository-style `ReadAllAsync` is a bounded enumeration, so only `System.Threading.Channels.ChannelReader<T>` sources qualify), shutdown paths (`StopAsync` and the stopping/stopped lifecycle callbacks), hoisted services with unprovable lifetimes, fields assigned anywhere outside field initializers/constructors/execution methods (a helper method may reassign per iteration), locals whose closest pre-loop write is a null/default clear, dynamic keyed resolutions, uncalled or deferred helpers, transitive and cross-declaration helpers, helper parameter/field flow, and provider aliases repointed inside the loop all stay silent.

**Code Fix:** No. Moving the scope into the loop body is a statement-level rewrite with disposal implications; apply it manually.

## DI025: Event Subscription On Longer-Lived Publisher Without Unsubscribe

**What it catches:** A transient- or scoped-registered service that subscribes (`+=`) an instance-capturing handler — an instance method group, a `this`-capturing lambda, or a stored instance-bound delegate field — to an event on a longer-lived publisher and never unsubscribes. Longer-lived publishers are injected dependencies whose registration is provably singleton — closed registrations preferred, open-generic singleton registrations matched for constructed injections — via a constructor parameter or a field/property assigned only from a constructor parameter, and `static` events. Identity and reference casts preserve that proof, so `((IBaseBus)_bus).Changed += H` reports for direct injected receivers and already-proven stable chains. Chained receivers (`_host.Bus.Changed += H`) report when the publisher is a stable projection of an injected root: the lifetime proof anchors on the chain root's registration, and every intermediate segment must be a readonly field, a get-only auto-property, or a getter returning one, with interface segments proven through the root's registered implementation types. Because C# forbids assigning another type's field-like event, the cross-type delegate leak lives on a delegate-typed field or property of the publisher instead: `_bus.Handlers += OnMessage` and the equivalent self-assignment `_bus.Handlers = (EventHandler)Delegate.Combine(_bus.Handlers, OnMessage)` report identically to an event `+=`, with a mirrored `Delegate.Remove` self-assignment recognized as the matching unsubscription. A `-=` written with a different lambda instance is recognized as the classic no-op unsubscribe bug: the subscription still reports and the diagnostic points at the ineffective `-=`.

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

**Guardrails:** singleton subscribers stay silent (a population of one cannot grow the delegate list — hosted services subscribing to singleton buses are the canonical safe shape), as do transient publishers (scoped publishers report the [DI026](#di026-event-subscription-on-scoped-publisher-without-unsubscribe) Info tier instead), any matching `-=` anywhere in the type (Dispose, `StopAsync`, teardown methods, the unsubscribe-then-resubscribe idiom) with the same method group — override chains normalized — or the same stored delegate field/local, static handlers and `this`-free lambdas, publishers assigned from `new` or ordinary method parameters, user-defined or value-changing receiver conversions, chained receivers whose projection is not provably stable (settable or computed segments, metadata-only or virtual segments), unregistered subscriber or publisher types, keyed-only publisher registrations, `EventSource`-derived publishers, and factory registrations with unknown implementation types. Casted and uncast receiver syntax canonicalize to the same publisher identity when matching `+=` with `-=`. Removals are recorded structurally, so a branch-conditional `-=` (`if (_attached) { _bus.E -= H; }`) suppresses unconditionally — a deliberate, documented FN that favours FP-safety over proving the guard always runs.

**Code Fix:** Yes, in three tiers, all gated on a method-group handler whose receiver (a field/property, a field/property-rooted chain, or a static event) still resolves inside `Dispose`. (1) **Insert into an existing Dispose** — when the type already declares a block-bodied `Dispose()`, `Dispose(bool)`, or `DisposeAsync()` and implements the matching disposal interface (`IDisposable`/`IAsyncDisposable`), the fix inserts the mirrored `-=` at the top of that method. (2) **Create the Dispose path when the contract is inherited** — when disposability comes from a base type that follows the standard virtual `Dispose(bool)` pattern, the fix adds a `protected override void Dispose(bool disposing)` that unsubscribes and chains to `base.Dispose(disposing)`; overriding the pattern is what guarantees the unsubscribe actually runs (through the base's `Dispose()` → `Dispose(true)` dispatch). Inherited shapes with no such hook — a non-virtual or explicitly-implemented base `Dispose` — are refused, because an added method the container never calls would be a fake repair. (3) **Implement `IDisposable` outright for scoped subscribers** — a subscriber registered **scoped** that implements neither disposal interface gets `IDisposable` added to its base list plus a `public void Dispose()` that unsubscribes; its owning scope disposes it deterministically, so no leak is introduced. Introducing `IDisposable` on a **transient** subscriber is refused — that is exactly the DI008 disposable-transient-capture shape, so the fix must never trade a DI025 for a DI008 — and hoisting a lambda into a field stays refused because it changes capture semantics.

---

## DI026: Event Subscription On Scoped Publisher Without Unsubscribe

**What it catches:** The scope-bounded tier of DI025: a **transient**-registered service subscribes an instance-capturing handler to an event on a **scoped** registered publisher — the receiver, identity/reference-cast, handler, and unsubscription proofs are exactly DI025's — and never unsubscribes. Publisher lifetime resolution follows the same rules (most conservative registration wins, closed registrations preferred over open-generic fallbacks, keyed-only registrations excluded), so a publisher registered both scoped and singleton reports DI026: only the scope-bounded claim is provable.

**Why it matters:** A transient injected with a scoped publisher is resolved from that same scope, so every transient instance the scope creates stays rooted in the publisher's delegate list until the scope is disposed, and the event keeps invoking handlers on instances the container has already released. Per-request scopes make this mostly benign; long-lived scopes — SignalR connections, Blazor circuits, hosted-service loop scopes — make it a real accumulation. DI026 reports at Info because the impact depends on scope longevity; raise it per team policy:

```ini
[*.cs]
dotnet_diagnostic.DI026.severity = warning
```

> **Explain Like I'm Ten:** Balloons tied to the classroom door instead of the school gate — they all pop when the classroom closes for the day, but a classroom that stays open all year still ends up dragging a lot of balloons.

**Problem:**

```csharp
services.AddScoped<IMessageBus, MessageBus>();
services.AddTransient<OrderHandler>();

public class OrderHandler
{
    public OrderHandler(IMessageBus bus)
    {
        bus.MessageReceived += OnMessage; // rooted by the scoped bus until the scope is disposed
    }

    private void OnMessage(object sender, EventArgs e) { }
}
```

**Better pattern:** identical to DI025 — store the subscription and remove it with `-=` when the subscriber is released (for example in `Dispose`).

**Guardrails:** every DI025 guardrail applies unchanged. Additionally, scoped subscribers on scoped publishers stay silent — equal lifetimes resolve from the same scope and are torn down together.

**Code Fix:** Yes — the same tier-1 (insert into existing `Dispose`) and tier-2 (override an inherited virtual `Dispose(bool)`) repairs as DI025, with the same gates. The tier-3 implement-`IDisposable` assist is never offered here: DI026 only fires for **transient** subscribers, and making a transient `IDisposable` is precisely the DI008 shape the fixer refuses.

## DI027: Rx Subscription On Longer-Lived Observable Without Dispose

**What it catches:** The Rx twin of DI025. `IObservable<T>.Subscribe(...)` returns an `IDisposable` token that unsubscribes the observer when disposed, so there is no `-=` to prove missing — the leak proof inverts to a **discarded token**. A **transient** or **scoped** registered service subscribes an instance-capturing handler (method group, `this`-capturing lambda, or stored delegate) to an observable exposed by a longer-lived publisher — an injected **singleton** dependency, or a **scoped** publisher shared by a transient subscriber — and discards the returned token. The observable is reached through DI025's classified receivers (an injected member proven ctor-assigned, a constructor parameter, or a stable chained projection such as `_source.Ticks`), and publisher lifetime resolution follows the same rules (most conservative registration wins, closed registrations preferred over open-generic fallbacks, keyed-only registrations excluded). Matching is FQN-light: any method named `Subscribe` returning `System.IDisposable`, invoked on a `System.IObservable<T>` receiver, so `System.Reactive`, community Rx, and hand-rolled extensions all bind.

**Why it matters:** A discarded subscription is a live one. The observable holds the observer, the observer captures the subscriber, and nothing releases it, so the longer-lived publisher roots every subscriber instance the container creates — leaking memory on each resolution and firing stale observers against released state. Unlike the DI025/DI026 Info split, DI027 is a single **Warning** tier: a token that outlives its subscriber is a definite leak whether the publisher is singleton or a scope-shared scoped.

> **Explain Like I'm Ten:** Subscribing hands you a "cancel" ticket. If you drop the ticket in the bin instead of keeping it, you can never cancel — and the newsletter keeps piling up in your mailbox forever.

**Problem:**

```csharp
services.AddSingleton<ITicker, Ticker>();   // Ticker : IObservable<int>
services.AddTransient<TickHandler>();

public class TickHandler
{
    public TickHandler(ITicker ticker)
    {
        ticker.Subscribe(OnTick); // the IDisposable is discarded; every TickHandler stays rooted
    }

    private void OnTick(int value) { }
}
```

**Better pattern:** store the token and dispose it when the subscriber is released (for example in `Dispose`, or via a `CompositeDisposable`).

```csharp
public class TickHandler : IDisposable
{
    private readonly IDisposable _subscription;

    public TickHandler(ITicker ticker) => _subscription = ticker.Subscribe(OnTick);

    public void Dispose() => _subscription.Dispose();

    private void OnTick(int value) { }
}
```

DI027 recognizes both idiomatic receiver syntax (`source.Subscribe(handler)`) and direct static extension syntax (`ObservableExtensions.Subscribe(source, handler)`). Static calls must bind to a real extension method; bound parameter mapping identifies the observable even when named arguments are reordered, and the source argument is excluded from handler capture analysis.

The BCL observer overload is also covered when the subscriber passes itself directly (`source.Subscribe(this)`): the argument must bind to `IObserver<T>` and reduce semantically to the containing instance. Separate observer objects remain silent because they do not prove subscriber capture.

**Guardrails:** DI027 fires only on the highest-confidence discard shapes — an ignored expression statement, a discard assignment (`_ = obs.Subscribe(H)`), a local initialized with the token that is never referenced again (and is not a `using` declaration), or a simple assignment to a private field declared on the subscriber when that field has no other symbol-bound reference across any partial declaration. A later disposal, return, argument pass, reassignment, or other field access stays silent; inherited and public/internal/protected fields also stay silent because external handling cannot be ruled out. `using`/`using var`, `CompositeDisposable`/`DisposeWith`/`AddTo`/`SerialDisposable`, and more complex field flows remain conservative. DI025's silence-on-unknown legs all apply: singleton subscribers, transient publishers, scoped-on-scoped pairs, static or `this`-free lambdas, separate observer objects, unregistered subscriber/publisher types, keyed-only publishers, unstable chained projections, non-extension static helpers named `Subscribe`, and non-observer `Subscribe(this)` overloads.

**Code Fix:** No — planned. The safe repair (introduce `IDisposable`, store the token, dispose it) depends on the subscriber's registered lifetime exactly like the DI025 tier-3 assist, and is deferred to a follow-up.

---

## DI028: Discarded Callback Registration On A Longer-Lived Source

**What it catches:** The third member of the DI025/DI027 family. Where DI025 proves a missing `-=` and DI027 proves a discarded `Subscribe` token, DI028 covers every remaining way .NET hands out a callback registration: `IOptionsMonitor<T>.OnChange`, `CancellationToken.Register` / `UnsafeRegister`, `ChangeToken.OnChange`, `IChangeToken.RegisterChangeCallback`, and `CancellationTokenSource.CreateLinkedTokenSource`. A **transient** or **scoped** registered service registers a callback on a longer-lived source — an injected singleton options monitor, `IHostApplicationLifetime.ApplicationStopping`, a token from a singleton-held `CancellationTokenSource`, a configuration reload token — and discards the registration that would detach it.

**Why it matters:** A discarded registration is a live one. The callback must provably capture the subscriber, either through the handler (method group, `this`-capturing lambda, stored delegate) or through the `object? state` argument, so the source roots every subscriber instance the container creates along with everything it holds — for a typical service, a `DbContext` and its change tracker. `ApplicationStopping` lives for the whole process, so the leak grows once per resolution and is never reclaimed.

**Problem:**

```csharp
services.AddScoped<OrderProcessor>();

public class OrderProcessor
{
    public OrderProcessor(IHostApplicationLifetime lifetime, AppDbContext db)
    {
        // DI028: the registration is discarded; every OrderProcessor stays rooted
        lifetime.ApplicationStopping.Register(() => Flush(db));
    }

    private void Flush(AppDbContext db) { }
}
```

**Better pattern:** store the registration and dispose it when the subscriber is released.

```csharp
public class OrderProcessor : IDisposable
{
    private readonly CancellationTokenRegistration _registration;

    public OrderProcessor(IHostApplicationLifetime lifetime, AppDbContext db) =>
        _registration = lifetime.ApplicationStopping.Register(() => Flush(db));

    public void Dispose() => _registration.Dispose();

    private void Flush(AppDbContext db) { }
}
```

Both the instance form (`monitor.OnChange(h)`) and the static extension form (`OptionsMonitorExtensions.OnChange(monitor, h)`) are recognized, with the source bound through Roslyn's parameter mapping so reordered named arguments resolve correctly. The `state` overloads are covered too: `token.Register(static s => ((Worker)s!).Run(), this)` has a static callback yet still pins the subscriber.

**Guardrails:** the subscriber must be registered and shorter-lived than the source, so a **singleton** or hosted-service subscriber registering on `ApplicationStopping` — the idiomatic, correct pattern — never fires. Method-parameter tokens stay silent (an ASP.NET `RequestAborted` registration is request-scoped and correct), as do locally created `CancellationTokenSource` tokens, `IOptionsSnapshot` sources above scoped subscribers, and any scoped-on-scoped or equal-lifetime pair. A static source qualifies only when the receiver is the exact framework `Token` property on an exact private `static readonly CancellationTokenSource` field initialized inline by the exact parameterless framework constructor and every compilation-visible use is either a direct `Register`/`UnsafeRegister` receiver read or a provably infinite `CancelAfter`; timed, already-canceled, factory-created, reassigned, canceled, disposed, mutable, public, aliased, and stored-token sources remain silent, while `Timeout.Infinite`, `-1`, and `Timeout.InfiniteTimeSpan` preserve the process-lifetime proof. Discard proof mirrors DI027 for callback registrations: an ignored expression statement, a `_ =` discard, a never-referenced non-`using` local, or an otherwise-unused private field reports. Linked token sources also report when a declaration initializer or assignment to a predeclared local—including an assignment expression used as the `Token` receiver—is consumed through ordinary or conditional-access `Token` reads and is not reliably disposed. A conditional read remains visible when another operation such as `Register` is chained after `Token`; disposing that later operation does not dispose the linked source. Direct `.Token` or `?.Token` extraction reports too because it loses the only handle through which the linked source can be disposed. The ownership proof is shared with DI014: `using`, reachable straight-line or `finally` disposal, and unconditional return to the caller stay silent; conditional or bypassed cleanup and reassignment before disposal report. Parentheses, null-forgiving operators, and identity casts cannot hide a `Token` read or wrapped initializer, and real cleanup through the same wrappers remains recognized; extension methods merely named `Dispose` or `DisposeAsync` do not establish cleanup. References before the current assignment belong to the older local value and are ignored; capture by a nested local function or lambda stays conservative because execution order is not proven. Unknown transfer calls—including fluent calls before a later `.Token` read—stay conservative and silent. Chained sources are followed only through provably stable projections; the metadata-only framework projections `CancellationTokenSource.Token` and `IHostApplicationLifetime.ApplicationStopping`/`ApplicationStarted`/`ApplicationStopped` are accepted only as a contiguous suffix, so nothing can be laundered through them. Known false negatives: `IChangeToken` reached through a field or local and non-trivial `ChangeToken.OnChange` producer lambdas are silent.

**Code Fix:** No — planned. Introducing `IDisposable` on a transient subscriber recreates the DI008 disposable-transient shape, the linked-source arm needs a different repair from the registration arm, and `CancellationTokenRegistration` is a struct with defensive-copy pitfalls.

---

## DI031: Shared Implementation Registered Under Several Service Types

**What it catches:** one implementation type registered under two or more different service types with the same singleton or scoped lifetime, as plain type registrations on the same service-collection flow.

**Why it matters:** each registration is its own descriptor, and the container builds one instance per descriptor. `AddSingleton<IReader, Store>()` followed by `AddSingleton<IWriter, Store>()` reads like one shared `Store` but produces two: state written through one interface is invisible through the other, and anything the implementation owns — a timer, a connection, a cache — exists twice. The bug is silent, because both resolutions succeed and return a perfectly valid object.

**Problem:**

```csharp
services.AddSingleton<IFeatureReader, FeatureStore>();
services.AddSingleton<IFeatureWriter, FeatureStore>();  // DI031: a second FeatureStore
```

**Better pattern:** register the implementation once, then forward.

```csharp
services.AddSingleton<FeatureStore>();
services.AddSingleton<IFeatureReader>(sp => sp.GetRequiredService<FeatureStore>());
services.AddSingleton<IFeatureWriter>(sp => sp.GetRequiredService<FeatureStore>());
```

**Guardrails:** transient registrations are exempt — a fresh instance per resolution is the contract, so there is no shared instance to lose. Registrations with different lifetimes, keyed registrations, factory registrations, pre-built instances, and registrations on different service-collection flows are all left alone, as is the same service type registered twice (that is DI012's duplicate registration). Registrations guarded by an `if`, `switch`, loop, or `try` never both run, so no two-instance claim is made, and neither do registrations in different executable bodies. Grouping is by constructed type, so `GenericStore<int>` and `GenericStore<string>` are distinct, and a `RemoveAll` or `Replace` that runs after the registration it removes withdraws the claim (a removal earlier in the method does not). Known false negatives: an open-generic registration paired with a closed one, and a fluent chain that removes a service type and then re-registers it in the same expression. Reported at Info: separate instances are occasionally deliberate, and the forwarding fix is a design decision.

**Code Fix:** No — the repair chooses which service type keeps the concrete registration and rewrites the rest as factories, which changes registration order and is better made deliberately.

---

## DI032: Service Implements Only IAsyncDisposable

**What it catches:** a service the container creates — a plain type registration at any lifetime — whose implementation implements `IAsyncDisposable` but not `IDisposable`.

**Why it matters:** the container tracks everything it creates so it can dispose it, but a synchronous `Dispose()` on the provider or a scope has no synchronous disposal method to call. Rather than skipping the service, `ServiceProvider` throws `InvalidOperationException`: *"'X' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container."* The failure surfaces at shutdown or at the end of a scope, which is exactly where it is hardest to notice in testing.

**Problem:**

```csharp
public sealed class UploadQueue : IUploadQueue, IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;
}

services.AddSingleton<IUploadQueue, UploadQueue>();  // DI032
using var provider = services.BuildServiceProvider();  // throws on Dispose()
```

**Better pattern:** implement both, so every disposal path has something to call.

```csharp
public sealed class UploadQueue : IUploadQueue, IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}
```

The alternative is to guarantee every disposal is asynchronous — `await provider.DisposeAsync()`, `CreateAsyncScope`, `await using` — which the generic host does for you but a hand-built provider does not.

**Guardrails:** the rule covers singleton and scoped registrations — a transient disposable is DI008's finding, and a second diagnostic on the same registration would be noise. Pre-built instances are exempt because the container never disposes them at all (that is DI033). Factory registrations do count — the container creates and tracks a factory's result — when the lambda body is a single object creation; an opaque factory proves nothing and stays quiet. A descriptor removed or replaced after it was added never reaches the provider. The diagnostic is conditional on the service being resolved at least once, since that is what puts it in the container's disposal list.

**Code Fix:** No — adding a synchronous `Dispose` means deciding what synchronous teardown of an inherently asynchronous resource should do, which the analyzer cannot answer.

---

## DI033: Container Will Not Dispose a Pre-Built Instance

**What it catches:** a disposable instance handed to the container as an existing object — `AddSingleton<TService>(new Thing())` or a descriptor carrying an implementation instance.

**Why it matters:** the container disposes only the instances it creates. An instance built by the caller is registered as-is, and disposing the provider does not touch it, so its file handles, sockets, or timers live until the process ends. The registration reads exactly like the type-registration form that *is* disposed, which is what makes it easy to miss.

**Problem:**

```csharp
services.AddSingleton<IMetricsSink>(new MetricsSink());  // DI033: never disposed by the container
```

**Better pattern:** hand the type over instead, and the container owns creation and disposal together.

```csharp
services.AddSingleton<IMetricsSink, MetricsSink>();
```

If the instance must be pre-built — it is shared with code outside the container, or its construction needs values the container does not have — dispose it deliberately at shutdown and treat the registration as a loan.

**Guardrails:** non-disposable instances raise no ownership question, and factory registrations are exempt because the container *does* create and therefore dispose what a factory returns, as is a descriptor removed or replaced after it was added. Reported at Info: caller-owned disposal is a legitimate choice, just one worth making explicitly.

**Code Fix:** No — rewriting to a type registration changes construction, and disposing at shutdown is a lifecycle decision.

---

## DI034: HttpContext Used in Fire-and-Forget Background Work

**What it catches:** an `HttpContext` value — a parameter, local, or field of that type — or a read of `IHttpContextAccessor.HttpContext`, inside background work started with `Task.Run` or `TaskFactory.StartNew` whose task is thrown away or observed only through a finite/cancelable `Wait(...)` Boolean result.

**Why it matters:** ASP.NET Core pools `HttpContext` and resets it as soon as the response has been written, and the accessor's backing `AsyncLocal` is cleared or reassigned to the next request. Work that outlives the request therefore reads a context whose request, response, features, and `RequestServices` have already been torn down. The usual symptom is a `NullReferenceException` or `ObjectDisposedException` under load, and the worst one is reading another user's request data from a recycled context.

**Problem:**

```csharp
public void Handle(HttpContext context)
{
    _ = Task.Run(() => _audit.Write(context.TraceIdentifier));  // DI034
}
```

**Better pattern:** take what the work needs out of the context first — plain values survive the request.

```csharp
public void Handle(HttpContext context)
{
    var traceId = context.TraceIdentifier;
    _ = Task.Run(() => _audit.Write(traceId));
}
```

**Guardrails:** a task that is awaited, returned, stored in a local, or waited on to guaranteed completion keeps the request alive until the work completes and stays silent. A framework `Task.Wait` with a finite timeout or cancelable token can return while work continues, so storing or returning its Boolean result still reports; user-defined extension methods named `Wait` stay conservative and silent. Background work that touches no context also stays silent. Reading the accessor *inside* the work is reported too, since by then the `AsyncLocal` has already moved on.

**Code Fix:** No — which values to hoist out of the context is a decision about what the background work actually needs.

---

## DI035: Non-Thread-Safe Service Shared Across a Fan-Out

**What it catches:** a documented non-thread-safe service — an EF Core `DbContext` or a derived context, `IDbContextTransaction`, or an ADO.NET connection, command, transaction, or reader — declared outside a `Task.WhenAll` projection and used inside every one of its tasks. This includes a service created once per outer `SelectMany` group and then shared by the inner tasks flattened into the same `WhenAll`.

**Why it matters:** `Task.WhenAll` starts every task before awaiting any of them, so the projection's lambda runs concurrently on one shared instance. `DbContext` detects it and throws *"A second operation was started on this context before a previous operation completed"*; the ADO.NET types are less forgiving and can corrupt connection state instead. The code reads like a clean parallel speed-up, which is exactly why it survives review.

**Problem:**

```csharp
await Task.WhenAll(orderIds.Select(id => _db.LoadAsync(id)));  // DI035
```

**Better pattern:** give each task its own scope, and therefore its own context.

```csharp
await Task.WhenAll(orderIds.Select(async id =>
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.LoadAsync(id);
}));
```

Processing the work sequentially with a `foreach` and `await` is equally correct when the parallelism is not worth a scope per item.

**Guardrails:** only the selector of a `Select`/`SelectMany`, or a lambda handed to `WhenAll` directly, counts as a concurrent body — a `Where` predicate runs one element at a time during enumeration, and an unrelated lambda nested inside a selector is part of that selector's single task. The exception is an inner task-returning selector directly returned by an exact `System.Linq.Enumerable.SelectMany` collection selector: those tasks are flattened into the outer `WhenAll`, so a value declared in the outer selector is shared by the inner fan-out. Exact LINQ binding, exact `Task` return, and return ownership are required; returns inside nested lambdas or local functions do not qualify. Properties are excluded, since a computed property can return a fresh instance per access. Only values declared *outside* the concurrent body count — a context created or resolved inside the actual task lambda belongs to that one task. Thread-safe services are untouched, `nameof` is not a use, and a sequential `foreach` never fans out. `Parallel.For`/`ForEach`/`ForEachAsync` bodies and framework message handlers are DI021's territory; this rule covers the `Task.WhenAll` leg it documented as out of scope.

**Code Fix:** No — the repair is a choice between a scope per task and sequential processing, with different throughput consequences.

---

## DI029: HttpClient Lifetime Misuse

**What it catches:** Two opposite lifetime errors on the same connection pool. **Socket exhaustion** — a registered service constructs `new HttpClient(...)` on a per-invocation path (a method, accessor, lambda, any loop body, or the constructor of a transient service). **Stale DNS** — an `HttpClient` is handed to the container as a singleton (`AddSingleton<HttpClient>`, `AddSingleton(new HttpClient())`, a singleton `ServiceDescriptor`, or a keyed singleton) or held in a `static` field or property.

**Why it matters:** Each per-call client opens its own connection pool, and disposing it does not free the socket — the connection sits in `TIME_WAIT` for minutes, so under load the ephemeral port range runs out and the application fails with `SocketException`. Wrapping the construction in `using` makes it worse, not better, because disposal is exactly what strands the socket. The usual fix — make it a singleton — trades the problem for its mirror image: one handler holds its connections for the life of the process and never re-resolves DNS, so after a failover or deployment the client keeps routing to an endpoint that has moved. `IHttpClientFactory` is the only shape that gets both connection reuse and DNS freshness.

**Problem:**

```csharp
services.AddScoped<ApiClient>();

public class ApiClient
{
    public async Task<Order> GetAsync(int id)
    {
        using var http = new HttpClient();  // DI029: one socket per call
        return await http.GetFromJsonAsync<Order>($"/orders/{id}");
    }
}

services.AddSingleton<HttpClient>();  // DI029: handler never rotates
services.AddScoped<HttpClient>();     // DI029: one handler pool per scope
```

**Better pattern:** let the container own the handler pool.

```csharp
services.AddHttpClient<ApiClient>(c => c.BaseAddress = new Uri("https://api.example.com"));

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    public Task<Order> GetAsync(int id) => _http.GetFromJsonAsync<Order>($"/orders/{id}");
}
```

**Guardrails:** the socket-exhaustion tier fires only when the containing type is provably a registered implementation in the same compilation, so tests, `Program`/top-level statements, and unregistered helpers stay silent — a compilation with no registrations reports nothing at all. It also requires `IHttpClientFactory` to be available, so a diagnostic is never raised where the fix is unavailable. A handler supplied by the caller transfers pool ownership and stays silent, as does `disposeHandler: false` and any non-constant `disposeHandler`; handler arguments are bound by parameter symbol rather than position. A client stored in a member is judged against the owner's lifetime, since one pool shared by a singleton is correct while a transient owner rebuilds it per resolution. A bare handler construction stays silent: constructing `HttpClientHandler` or `SocketsHttpHandler` opens no connection until something sends through it, and the leak shape that matters — `new HttpClient(new SocketsHttpHandler())` — is already reported through the client. A client whose handler sets `PooledConnectionLifetime` is also silent at both stale-DNS tiers: that handler retires pooled connections on an interval and re-resolves DNS with them, which is the documented way to run a long-lived client without the factory. Exact type-backed scoped self-bindings now report when `IHttpClientFactory` is available because each scope creates and disposes an independent handler pool; direct, keyed, and `ServiceDescriptor` forms share that boundary. Factory-backed scoped registrations, scoped `HttpClient` subclasses, and projects without the factory API remain conservative and silent. `AddTransient<HttpClient>` remains **DI008**'s finding and is deliberately not double-reported. `HttpClient` subclasses are excluded at the singleton and static gates, `Lazy<HttpClient>` and dictionary-of-clients static wrappers are accepted false negatives, and a singleton factory that provably delegates to `IHttpClientFactory.CreateClient` stays silent. A single construction never yields two findings: static initializers belong to the static-member tier and an argument-position construction to the registration tier.

**Code Fix:** No — planned. Rewriting `new HttpClient()` into an injected `IHttpClientFactory` requires adding `services.AddHttpClient()` at a registration site that may be in another document or project, and possibly a `PackageReference` — which a code fix cannot do. Applying only the constructor and call-site half produces code that compiles and then throws `InvalidOperationException: No service for type 'IHttpClientFactory'`.

---

## DI030: Unbounded Singleton Or Static Cache

**What it catches:** Two shapes of a store that never shrinks. **Unbounded growth** — a `private` field of a concrete mutable collection (`ConcurrentDictionary<,>`, `Dictionary<,>`, `List<>`, `HashSet<>`, `Queue<>`, `ConcurrentBag<>`, `ConcurrentQueue<>`) that is `static` or owned by a **singleton**-registered service, written on a per-invocation path with a key derived from request input, where nothing in the declaring type ever removes, clears, drains, or size-checks it. **Unbounded cache entries** — an `IMemoryCache.Set` / `GetOrCreate` / `CreateEntry` call with an unbounded key and neither an expiration nor a `Size`, in a compilation whose cache has no `SizeLimit`.

**Why it matters:** A store held by a singleton or a static field lives as long as the process. Keyed by a user id, tenant id, or correlation id, it accumulates one entry per distinct caller forever: memory climbs monotonically and the process eventually dies of `OutOfMemoryException`, typically days into a deployment — which makes it one of the hardest leaks to attribute. `IMemoryCache` is not automatically safer: with no expiration, no entry size, and no configured `SizeLimit` it is an unbounded dictionary with extra steps.

**Problem:**

```csharp
services.AddSingleton<PriceService>();

public class PriceService
{
    private readonly ConcurrentDictionary<string, Quote> _cache = new();

    public Quote Get(string userId) =>
        _cache.GetOrAdd(userId, id => Load(id));  // DI030: unbounded, never evicted

    private Quote Load(string id) => new();
}
```

**Better pattern:** bound the store — an expiration, a size limit, or an explicit eviction path.

```csharp
public class PriceService
{
    private readonly IMemoryCache _cache;

    public PriceService(IMemoryCache cache) => _cache = cache;

    public Quote Get(string userId) =>
        _cache.GetOrCreate(userId, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return Load(userId);
        })!;

    private Quote Load(string id) => new();
}
```

**Guardrails:** reported at **Info**, because a key space that is unbounded in the type system may be bounded in production. The "never evicted" proof is sound rather than heuristic: the field must be `private`, so every reference to it lives inside the declaring type and a complete scan of that type is a complete proof. Anything not recognized as a write or a pure read makes the candidate silent — a `Remove`/`TryRemove`/`Clear`/`Dequeue`/`Pop`, any read of `Count` or `Length` (a size cap), the field passed as an argument, reassigned, iterated with `foreach`, used in LINQ, or captured into a lambda (a background eviction timer). Bounded keys are excluded up front: any compile-time constant, and any `enum`, `bool`, `System.Type`, or `char` key. One-time initialization is excluded too — constructor, static-constructor and initializer writes, assembly and `Enum.GetValues` scans, `Lazy<>` factories, and one-shot flag guards. Interface-typed fields (`IDictionary<,>`) stay silent because the backing type may be frozen or capped, as do `ImmutableDictionary`/`FrozenDictionary`, lock registries (`SemaphoreSlim`, `Lazy<>`, `Task`, `Mutex`-shaped value types), non-private fields, and types registered both singleton and scoped. Shapes owned by other rules are excluded rather than duplicated: a scope-resolved value stored into a collection is **DI002**, a scoped service cached by a singleton is **DI003**, and a static dictionary of providers is **DI006**. For `IMemoryCache`, options built anywhere other than inline at the call site stay silent, and a compilation-wide `MemoryCacheOptions.SizeLimit` disables the tier entirely. Two editorconfig knobs are available: `dotnet_code_quality.DI030.allowed_cache_types` and `dotnet_code_quality.DI030.detect_memory_cache_bounds`. Accepted false negatives: a key reached through a local alias, non-private and static-property caches, multi-level nested dictionaries, and collection types outside the seven recognized generics.

**Code Fix:** No — and none planned. There is no single correct eviction policy: LRU, a TTL, a size cap, or a documented decision that the key space really is bounded are all valid answers, and a fixer that silently picks one would be worse than the diagnostic.
