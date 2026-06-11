# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.11.6] - 2026-06-11

### Fixed

- **DI021 fixer non-compiling/behavior-breaking output**: plumbing `IServiceScopeFactory` into a constructor that already had a parameter or local named `scopeFactory` produced a duplicate name (non-compiling) — the collision check inspected type members only, and ctor parameters/locals are not members. Partial types are now refused outright: the constructor count, the plumbing target, and the dead-field-removal reference scan all operate on a single declaration, so a constructor or field reference in another part would leave a second construction path with a null factory or remove a still-used field.
- **DI022 scoped-tier false positive on property initializers**: a manually constructed instance captured via a property initializer (`private EmailSender Email { get; } = new EmailSender();`) reported as a reused scoped DI instance — the single-origin scan only enumerated field declarators and assignments. Property `EqualsValueClause` initializers now participate, matching the existing field-initializer suppression.

## [2.11.5] - 2026-06-11

### Changed

- **DI024 recall: wrapper invocations, provider aliases, declare-then-assign scopes**: three canonical worker shapes were invisible. (1) `await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)`, `await reader.WaitToReadAsync(ct).ConfigureAwait(false)`, and `await foreach (... in reader.ReadAllAsync(ct).ConfigureAwait(false))` / `.WithCancellation(ct)` — the loop-shape gates saw the wrapper invocation's name instead of the wrapped call, so any hosted service following the ConfigureAwait(false) library guidance lost all DI024 coverage; wrappers are now peeled before gating. (2) `var sp = scope.ServiceProvider;` — the alias local hid hoisted-scope usage; in-loop resolutions through the alias now attribute to the hoisted scope (including pre-loop service resolutions through the alias), and an alias repointed inside the loop makes its uses unattributable rather than refreshing the scope; aliases resolve after field scope symbols are known (`var sp = _scope.ServiceProvider;` over a hoisted scope field reports), and an alias binds to the creation that dominated its own declaration, so a later pre-loop reassignment or clear of the scope local does not repoint or silently drop it; the same pinning applies to pre-loop service resolutions (direct or through an alias) — the diagnostic lands on the creation that actually produced the service. (3) `IServiceScope? scope = null; try { scope = factory.CreateScope(); while (...) { ... } } finally { scope?.Dispose(); }` — the try/finally ownership pattern assigns rather than initializes, and only declarator initializers were inspected; direct pre-loop assignment statements now participate with last-write-wins semantics, which also fixes a pre-existing false positive: an initializer-created scope explicitly disposed and cleared (`scope = null`) before the loop no longer reports, because the closest pre-loop write is a clear and the created instance provably never feeds the loop. Conditional (nested) pre-loop writes never dominate, and dispose-and-recreate reassignment inside the loop suppresses as before.

## [2.11.4] - 2026-06-11

### Fixed

- **DI013 instance-registration false positive (Error severity)**: an instance argument whose declared type is a base type or interface — `IService instance = new Service(); services.AddSingleton(typeof(Service), instance);` — reported an Error on correct code, because the mismatch check judged the instance by its static type while the runtime type can be any subtype. DI013 is the package's only Error-severity rule, so zero false positives is non-negotiable: instance-backed registrations (including all `ServiceDescriptor` shapes) now report only when the runtime type is provably known — the argument is an object creation (unwrapped through parentheses and runtime-object-preserving casts, so `(IService)new Service()` is also correctly judged by its created type while user-defined conversion operators, which produce a different object, are never unwrapped), or its static type is sealed or a value type. The conservative flip side — a non-sealed-class-typed local that genuinely holds an incompatible instance — stays silent and is documented as a known false-negative direction.
- **DI013 fixer non-compiling candidates**: the implementation-candidate scan offered generic type definitions, emitting `typeof(Repo<T>)` with an undefined `T` (non-compiling), and structs, which violate the generic registration's class constraint and have no container-activatable constructor in the typeof form. Both are now excluded from candidate offers.

## [2.11.3] - 2026-06-11

### Fixed

- **Cross-file method-group factory crash (AD0001)**: a factory registered as a method group declared in another file — `services.AddSingleton<IMyService>(Factories.Create)` with `Factories` in a second file — crashed DI010 with `ArgumentException: Syntax node is not within syntax tree`, and an AD0001 analyzer crash kills every diagnostic for the compilation. Factory analysis follows the method group's declaration into the declaring file's syntax tree, but the resolved body nodes were then queried against the registration site's semantic model. DI010's factory return analysis and the shared factory dependency-request walk (consumed by DI015, DI017, and the scoped dependency graph) now resolve the semantic model that owns each foreign node, so cross-file method-group factories are analyzed correctly instead of crashing: DI010 reports over-injected constructors and DI015 reports missing dependencies through factory bodies declared in other files. C# 12 primary-constructor services were also pinned with regression tests (constructor selection already handled the synthesized primary constructor).

## [2.11.2] - 2026-06-11

### Changed

- **DI024 field-stored hoisted scopes and services**: a scope created in the constructor or `StartAsync` and stored in a field, then used inside the execution loop, is the same bug as the hoisted local — worse, because the scope also outlives the method — and was previously invisible. Fields qualify only when provably hoisted: every assignment (including the field initializer) is a scope creation (or a resolution of one service type), and every assignment site lives in a field initializer, a constructor, or a hosted execution method — an assignment in any helper method or property disqualifies the field, because the helper may run per iteration. `??=` lazy initialization qualifies as a hoisted assignment, and a `??=` inside the loop reports rather than suppressing — the first iteration's scope is reused forever, which is lazy hoisting, not dispose-and-recreate. Partial hosted services are fully covered: fields, assignments, and execution methods are collected across every partial declaration (including other files), so a field in one part used by a loop in another reports, and a helper-method assignment in any part disqualifies. Teardown clears (`_scope = null`, `= null!`, `= default`, `= default(T)` in `StopAsync`) are neutral rather than disqualifying, and an assignment must dominate the loop — textually before it, or in an earlier-or-equal lifecycle stage (constructor -> StartingAsync -> StartAsync -> StartedAsync -> ExecuteAsync); a field whose only creation runs after the loop, in a later stage (StartedAsync feeding a StartAsync loop), or whose closest pre-loop write is a null clear, stays silent. With multiple assignments the diagnostic lands on the site that actually feeds the loop, and in-loop error-branch null clears do not suppress as dispose-and-recreate. Field-stored scopes share all existing suppressions (all-singleton resolutions, dispose-and-recreate reassignment inside the loop — including `this._scope = ...` — and registration gating for service fields), service fields resolved from a hoisted scope field attribute to the scope's report, and a field used by loops in multiple execution methods reports once at its creation site. `BackgroundService.StartAsync` overrides are now recognized as execution-loop entry points (the interface mapping previously only matched direct implementations).

## [2.11.1] - 2026-06-10

### Changed

- **DI024 channel-consumer loop shapes**: `await foreach (var item in reader.ReadAllAsync(token))` and `while (await reader.WaitToReadAsync(token))` now qualify as long-running execution loops — these are the canonical modern queue-worker shapes and were invisible to the 2.11.0 release, so a scope hoisted above a channel-draining loop went unreported. Both shapes are gated semantically on `System.Threading.Channels.ChannelReader<T>` (resolved through base types), so a repository-style `ReadAllAsync` (bounded read-everything-once enumeration) and plain `foreach` batches stay silent. Channel loops nested inside an outer cancellation loop are analyzed against locals declared in the enclosing loop body, so a scope created per outer iteration but hoisted above an unbounded inner channel drain reports, while a scope hoisted above both loops reports exactly once (at the outer loop). All existing tiers and suppressions apply unchanged inside the new loop shapes.

## [2.11.0] - 2026-06-10

### Added

- **DI024: Hosted service creates scope outside execution loop** (Warning): a `BackgroundService.ExecuteAsync` override or `IHostedService`/`IHostedLifecycleService` start method that creates an `IServiceScope` once before its long-running execution loop (`while (!token.IsCancellationRequested)`, `while (true)`, `for (;;)`, `PeriodicTimer` tick loops) and uses it inside the loop now reports at the `CreateScope`/`CreateAsyncScope` call — the hoisted scope keeps the same scoped instances (DbContexts, units of work) alive for the entire process lifetime instead of per iteration. A second tier reports a service whose registration is provably scoped resolved once before the loop and reused across iterations (RegistrationCollector-backed, compilation-end). Scopes created inside the loop, startup scopes consumed before the loop, dispose-and-recreate reassignment inside the loop, hoisted scopes whose every resolution is provably singleton, bounded loops, shutdown paths, and unprovable lifetimes all stay silent. No code fix in v1 — the scope-into-loop move is a statement-level rewrite with disposal implications.

## [2.10.12] - 2026-06-10

### Added

- **DI022 scoped-lifetime tier (registration-backed)**: services outside the non-thread-safe catalog now report the config-gated Info when their *effective registration is scoped* and they are captured (field, closure, enclosing parameter) into a concurrently-invoked handler — a scoped service captured for the application lifetime reuses one instance across all invocations, accumulating state exactly like the documented ServiceBus case. Lifetimes come from the shared `RegistrationCollector` at compilation end (last registration wins, closed constructed types fall back to their open-generic registration; keyed registrations excluded in v1), and the tier carries its own DI022 wording describing the scoped capture itself rather than a configuration knob. Singleton-registered, unregistered, and manually-constructed captures (the symbol's unique origin is a `new` expression, with casts/`as`/parentheses/null-forgiving unwrapped — no scoped DI instance is being reused) stay silent, and every existing guardrail (whitelisted captures, in-handler creation, serialization guards, dispose-only uses) applies. The tier is Info-only by design — concurrency of an arbitrary scoped service is not provably unsafe, but the lifetime violation is real.

## [2.10.11] - 2026-06-10

### Changed

- **DI021/DI022 RabbitMQ instance-correlated chain proofs**: the consumer's own creation chain — the consumer constructor's channel argument, the channel's `CreateModel`/`CreateChannel`/`CreateChannelAsync` call (awaits unwrapped), the connection's `CreateConnection(Async)` call — now traces to the `ConnectionFactory` that actually feeds this consumer. A chain-proven `ConsumerDispatchConcurrency = 1` (or a fresh factory that never sets the knob — the sequential default) silences the sink even when an unrelated factory in the same type sets the knob above 1; a chain-proven value above 1 reports DI021; an actual `CreateChannelOptions` argument (type-checked — cancellation tokens and null literals are not overrides) forces the config-gated tier in both directions, because the per-channel knob can override the factory in either direction. Every chain link must have a unique origin in the containing type (origin resolution for the factory ignores writes after the chain consumed it, so later reuse of the variable does not break the proof); untraceable chains keep the previous strengthen-only scan with DI022. This removes the last documented RabbitMQ noise source and completes the instance-correlation roadmap for this sink.

## [2.10.10] - 2026-06-10

### Added

- **DI021 EventHubs batch-processor sink**: overrides of `Azure.Messaging.EventHubs.Primitives.EventProcessor<TPartition>.OnProcessingEventBatchAsync` and `OnProcessingErrorAsync` now participate in concurrent-handler shared-state analysis — the override body is the handler (partitions are dispatched concurrently), and instance fields are the capture channel. The base type matches by fully-qualified name through the override chain, so same-named user base types stay silent, and all existing guardrails (`IDbContextFactory<T>`, in-handler creation, serialization guards) apply. This completes the DI021 v2 sink registry.

## [2.10.9] - 2026-06-10

### Added

- **DI002 composite-construction return escapes**: scoped services smuggled out through returned tuples (`return (service, count);`) and anonymous objects (`return new { Service = service };`) now report — for tracked locals, capturing delegates, and direct resolutions nested in the composite — recursing through nested composites. Returned object creations with initializer assignments (`return new Holder { Service = service };`) were already detected through the property-assignment sink and are now pinned. Composites consumed inside the scope stay quiet. This closes the last named DI002 audit-debt item.

## [2.10.8] - 2026-06-10

### Added

- **DI021/DI022 TPL Dataflow sinks**: delegates passed to `ActionBlock<T>`, `TransformBlock<TIn,TOut>`, and `TransformManyBlock<TIn,TOut>` constructors now participate in concurrent-handler shared-state analysis. Execution blocks default to `MaxDegreeOfParallelism = 1`, so a block without options stays silent; a traced options object proving the knob above 1 (or `DataflowBlockOptions.Unbounded`) reports DI021; a fresh creation (inline or via a same-tree helper) that never sets the knob keeps the sequential default, while caller-provided parameters and fields with no observed writes are config-gated DI022 — the caller may have raised the knob. The full knob-proof machinery applies (inline creations, locals with consumption cutoffs, same-tree helper methods, escape/deferred-write poisons).

## [2.10.7] - 2026-06-10

### Added

- **DI021 PLINQ `ForAll` sink**: delegates passed to `ParallelEnumerable.ForAll` now participate in concurrent-handler shared-state analysis — PLINQ partitions run concurrently by default, so a captured `DbContext` fails under load exactly like the covered `Parallel.*` shapes. A proven `WithDegreeOfParallelism(1)` on the query's own fluent chain suppresses (nearest setting wins; the walk follows receiver-position arguments so binary operators like `Concat`/`Zip`/`Join` do not hide the proof); unprovable degrees and untraceable queries keep the truthful concurrent default. All existing capture/guardrail machinery applies.

## [2.10.6] - 2026-06-10

### Fixed

- **`Replace(...)` registrations were invisible**: `ServiceCollectionDescriptorExtensions.Replace(services, ServiceDescriptor.Scoped<TService, TImpl>())` recorded only the removal — the replacement descriptor itself was never collected as a registration, so a cycle (or captive, or resolution) introduced by the replacement evaded every rule. The shared `RegistrationCollector` now records the Replace descriptor as a registration ordered after its removal, mirroring runtime behavior. DI012 treats a Replace registration as a new baseline when at most one descriptor was active (intentional override semantics, no duplicate for Add-then-Replace), while a Replace after two or more active descriptors still reports — Replace removes only one matching descriptor, so one survives and the replacement overrides it, and the `new ServiceDescriptor(typeof(T), instance)` constructor shape registers as a singleton instance (its implicit MEDI lifetime), keeping DI015 quiet for dependencies provided by instance-based Replace.

### Added

- **DI017 test-density pass** (the health re-audit's last work-priority item): `Replace` mutations pinned in both directions (removing a cycle stays silent, introducing one reports), keyed-edge precision pinned (a dependency on a different key does not close a cycle; int `1` and string `"1"` keyed cycles report separately — regression guard for the PR #32 dedup fix), and `Lazy<T>` constructor parameters pinned as not modeled (no edge — silent, matching the default container's inability to resolve `Lazy<T>`).

## [2.10.5] - 2026-06-10

### Fixed

- **DI004 mutually-exclusive-branch false positive**: explicit-dispose tracking was linear and position-based, so a service use in the opposite arm of the same `if`/`else` as the `Dispose()` call — or in a different section of the same `switch` — was reported even though the two cannot both execute. The use-reporting pass now skips branches mutually exclusive with the dispose site; switch sections stay exclusive unless control can actually flow from the dispose's section to the use's section through `goto case`/`goto default` reachability (plain label gotos and unresolvable targets are conservatively treated as reaching everything; unrelated goto chains between other sections do not void the suppression), every dispose site participates — a branch mutually exclusive with the first dispose still reports a use after its own dispose — and alias/reassignment tracking observes every node regardless of branch gating, so a local reassigned to a caller-provided instance before its branch's dispose stays quiet. A use on the shared path after a conditional dispose still reports (the dispose may have run on the taken branch).

## [2.10.4] - 2026-06-10

### Fixed

- **DI020 explicit-argument false positive**: a scoped-typed constructor parameter satisfied by an explicit `UseMiddleware` argument (`app.UseMiddleware<MyMiddleware>(preBuilt)` / `app.UseMiddleware(typeof(MyMiddleware), preBuilt)`) was still reported, even though ActivatorUtilities binds the supplied argument and never resolves that parameter from the container. Constructor selection now threads its argument-fill map through to reporting; a parameter explicitly supplied at every registration site stays quiet, while one unfilled site still reports.

### Added

- **DI020 conditional-access registration**: `app?.UseMiddleware<MyMiddleware>()` on builder-typed instance members is now recognized (the receiver resolves through the enclosing conditional access). Extension-method registrations already worked through the reduced-method parameter type and are now pinned by tests.
- **DI020 audit-gap coverage**: the 2026-06-10 health re-audit's untested paths are now all covered — non-generic `UseMiddleware(typeof(T))` (positive and explicit-argument-suppressed), keyed scoped dependencies (`[FromKeyedServices]` reporting on key match, silent for a different-key singleton), `IEndpointRouteBuilder` registrations, and the extension-method (`ReducedFrom`) receiver path.

## [2.10.3] - 2026-06-10

### Added

- **DI002 collection and event-subscription escape sinks**: scoped services now report when they escape through mutation of a field/property-held container (`_cache.Add(service)`, `_cache.Insert(...)`, `Enqueue`, `Push`, `TryAdd` — both for tracked locals and direct resolutions passed as arguments) and when a handler bound to the scoped service is subscribed to an event whose owner outlives the scope (`_publisher.Changed += service.Handle`, captured-delegate subscriptions, method-group delegate locals (`EventHandler h = service.Handle;`), static events, and events on the enclosing instance). Method groups on tracked service locals participate in delegate-capture tracking generally, so they also report through the existing field/property/ref-out/return delegate sinks. Mutation matching requires the resolved method to return void/bool/int (real mutator signatures) and the receiver type to actually be a collection (implement `IEnumerable`), so value-returning shapes — `ImmutableList.Add`, fluent builders — and ordinary field-held objects with Insert/Add-style methods (repositories persisting data) stay quiet; conditional-access mutations (`_cache?.Add(service)`) are recognized through the enclosing conditional access. The new sinks require the resolution to precede the sink in document order (a local reassigned to a scoped resolution after the `Add`/subscription escaped its previous value, not the scoped one), method-group recognition gates on the member actually being a method (delegate-valued properties returning static handlers stay quiet) and on the resolution preceding the conversion site (method groups bind their receiver at conversion time), and event receivers are classified by the root of the access chain, so `wrapper.Publisher.Changed` with a scope-local wrapper stays quiet while field/property/parameter/static roots report. Local containers and scope-local publishers stay quiet too — they live and die with the scope. Indexer assignment to a field dictionary (`_byTenant[key] = service`) was already detected through the indexer property symbol and is now pinned by a regression test. These were the two highest-frequency real-world escape shapes missing from the sink table per the 2026-06-10 health re-audit.

## [2.10.2] - 2026-06-10

### Changed

- **DI021/DI022 cross-method knob proofs (same-tree helper methods)**: concurrency-knob evaluation now follows options built by helper methods — `var options = CreateOptions();` or `client.CreateProcessor("q", CreateOptions())` where the helper is non-virtual, singly-declared, and in the same file. A helper that provably returns a fresh creation with `MaxConcurrentCalls = 8` upgrades the config-gated DI022 Info to the DI021 warning; one that pins the knob to 1 silences the sink (the fresh creation is instance-correlated by construction). Supported helper shapes: expression-bodied `=> new Options {...}`, a single `return new Options {...};`, and a single returned local initialized with a creation (collecting its in-helper member writes). Stale proofs are invalidated: reassigning the options local from another helper re-derives the proof from the replacement, a fresh-creation replacement discards every value collected for the discarded instance (a stale `MaxDegreeOfParallelism = 1` can no longer silence a default-unlimited `ParallelOptions`), opaque reassignments make the knob unprovable, writes inside nested lambdas/local functions poison sequential proofs as unknown candidates (without erasing construction-time concurrent constants, and regardless of where the nested function is declared — declaration position says nothing about execution order), and writes or reassignments after the sink consumed the options (the SDK snapshots values at the creation call) are ignored as later variable reuse. Virtual/overridable helpers, parameter-driven knob values, multiple returns, and shared-instance returns likewise stay unproven (DI022) — reducing the Info-tier noise the health doc watchlists without weakening the instance-correlation principle. Applies to ServiceBus processor options and `ParallelOptions` alike.

## [2.10.1] - 2026-06-10

### Added

- **DI021/DI022 RabbitMQ consumer sinks**: `EventingBasicConsumer.Received` (v6 sync), `AsyncEventingBasicConsumer.Received` (v6 async), and `AsyncEventingBasicConsumer.ReceivedAsync` (v7) handlers now participate in concurrent-handler shared-state analysis — RabbitMQ consumers are among the most common .NET message-handler surfaces sharing a single `DbContext` across deliveries. The dispatch pump's `ConsumerDispatchConcurrency` knob lives on the `ConnectionFactory` (v7: also per-channel options), typically in another method or bound from configuration, so reports default to the config-gated DI022 Info tier; a constant knob above 1 in the containing type upgrades to the DI021 warning. Knob constants are recognized across integral types (RabbitMQ.Client v7 declares the property as `ushort`), consumer types match by fully-qualified name, and all existing guardrails apply (in-handler scopes, `IDbContextFactory<T>`, inline creation, serialization guards, whitelisted captures). Instance-correlated factory→connection→channel→consumer tracing for sequential proofs remains a v2 target.

## [2.10.0] - 2026-06-10

### Added

- **DI021: Non-thread-safe service shared across concurrent handler invocations** (Warning): catches the deferred captive dependency — a non-thread-safe service (EF Core `DbContext` and derived contexts, `DbConnection`/`DbCommand`/`DbTransaction`/`DbDataReader` and their interfaces, `IDbContextTransaction`, `HttpContext`) created or resolved once and captured via field, closure, or enclosing-method parameter into a handler a framework invokes concurrently. v1 sinks: `ServiceBusProcessor`/`ServiceBusSessionProcessor` message and error handlers, `EventProcessorClient` event handlers, `System.Threading.Timer` callbacks with a finite period, `System.Timers.Timer.Elapsed`, and `Parallel.For`/`ForEach`/`ForEachAsync`/`Invoke` bodies. Also detects resolution from a long-lived scope captured from outside the handler (the "moved `GetRequiredService` inside the lambda" loophole). Stays quiet for in-handler scopes, `IDbContextFactory<T>`, inline creation, proven-sequential configurations (`MaxConcurrentCalls = 1`, `MaxConcurrentSessions = 1`, `MaxDegreeOfParallelism = 1`, one-shot timers, `AutoReset = false`, `SynchronizingObject`), and handlers that serialize themselves (`lock`, `SemaphoreSlim` wait/`finally`-release, `Interlocked`/`Monitor.TryEnter` reentrancy guards, timer re-arm, disposable async-lock idiom). Scope-per-message frameworks (MassTransit, NServiceBus, Quartz, Hangfire, SignalR, Azure Functions) are deliberately not sinks.
- **DI022: Service instance reused across handler invocations** (Info): the same capture shape on a config-gated sink whose concurrency knob cannot be proven at compile time (canonically `ServiceBusProcessor.MaxConcurrentCalls` from configuration or default). Conditional wording: raising the knob above 1 makes it a concurrency crash, and even sequential dispatch accumulates state across all messages. Proven `> 1` upgrades to DI021; proven `== 1` is silent.
- **DI021/DI022 code fix**: rewrites the handler to scope-per-invocation — inserts `await using var scope = _scopeFactory.CreateAsyncScope();` (or the sync `CreateScope` form for synchronous delegates, converting expression-bodied lambdas to blocks), re-resolves the service from the new scope, plumbs `IServiceScopeFactory` through the constructor when no factory field exists, and removes the captured field, its constructor assignment, and the feeding parameter when the handler was their only consumer. Refuses safely for static handlers, scope-resolution diagnostics, and types without a declared constructor.

## [2.9.6] - 2026-06-10

### Changed

- **DI014 wrapped-result disposal precision**: DI014 now treats root providers assigned or returned through parenthesized, provable upcast, or null-forgiving `BuildServiceProvider()` results as disposed or caller-owned when appropriate — including combinations with conditional-access creations such as `(services?.BuildServiceProvider())!`. Flows that pass the result through a user-defined conversion (explicit *or* implicit operator) or an unproven downcast (`(Wrapper)(object)...`, downcast from an interface) still report, because they are not proven to hand the root provider itself to the disposal or return site.

## [2.9.5] - 2026-06-09

### Changed

- **DI004 conditional-access resolution tracking**: DI004 now tracks services resolved through conditional access, so `service = scope?.ServiceProvider.GetRequiredService<T>();` inside a `using` block is recognized and a later use after the scope is disposed reports as it does for the plain form. Chained `scope?.ServiceProvider?.GetRequiredService<T>()` resolutions and scopes created with `using (var scope = factory?.CreateScope())` (declaration, using-statement, predeclared, and reassignment forms) participate too. Conditional resolutions consumed inside the scope stay quiet. Conditional *uses* after dispose (`service?.DoWork()`) were already covered.

## [2.9.4] - 2026-06-09

### Changed

- **DI002 conditional-access escape detection**: DI002 now reports scoped services that escape their scope through conditional-access shapes. Previously `return scope?.ServiceProvider.GetRequiredService<T>();`, chained `scope?.ServiceProvider?.GetRequiredService<T>()`, field captures `_field = scope?.ServiceProvider.GetRequiredService<T>();`, and locals resolved through `scope?.ServiceProvider...` that later escape were all silent, because resolution recognition required a plain `MemberAccessExpressionSyntax` receiver and the consumption-shape checks matched the invocation's direct parent. The analyzer now resolves the provider receiver through `MemberBindingExpressionSyntax`/`ConditionalAccessExpressionSyntax` shapes (including `using var scope = factory?.CreateScope();` creations) and classifies consumption from the outermost enclosing conditional access. Transient resolutions and locally-consumed services through the same shapes stay quiet.

## [2.9.3] - 2026-06-09

### Changed

- **DI014 conditional-access disposal proofs**: DI014 no longer reports root providers created through conditional access (`services?.BuildServiceProvider()`) that are in fact handled. As with DI001 in 2.9.2, the consumption-shape checks matched the invocation's direct parent, so `var provider = services?.BuildServiceProvider();` with a later `provider?.Dispose()` (including `finally` cleanup and predeclared reassignment), `return services?.BuildServiceProvider();`, and arrow-bodied returns all produced false positives. The analyzer now resolves the enclosing `ConditionalAccessExpressionSyntax` before matching initializer/assignment/return/arrow parents. Undisposed conditional-access creations still report, and `using var provider = services?.BuildServiceProvider();` stays quiet as before.
- **DI014 fixer conditional-access support**: The dispose-provider code fix now also offers the `using` / `await using` rewrite for conditional-access creations. The rewrite stays valid for that shape because the local is a nullable reference type (`ServiceProvider` implements both `IDisposable` and `IAsyncDisposable`, and `using` accepts null values).

## [2.9.2] - 2026-06-09

### Changed

- **DI001 conditional-access disposal proofs**: DI001 no longer reports scopes created through conditional access (`_provider?.CreateScope()`) that are in fact handled. The consumption-shape checks (`IsReturned`, explicit-disposal local extraction) previously matched the invocation's direct parent, but a conditional-access creation hangs the initializer/assignment/return/arrow shape off the enclosing `ConditionalAccessExpressionSyntax`, so `var scope = _provider?.CreateScope();` with a later `scope?.Dispose()` (including `finally` cleanup and predeclared reassignment), `return _provider?.CreateScope();`, and arrow-bodied `=> _provider?.CreateScope()` all produced false positives. Undisposed conditional-access creations still report, and `using var scope = _provider?.CreateScope();` stays quiet as before.
- **DI001 fixer await-using guardrail**: The "Add 'await using'" fix is no longer offered for conditional-access creations. `factory?.CreateAsyncScope()` produces a nullable `AsyncServiceScope` (a `Nullable<T>` with no `DisposeAsync`), so the rewrite could not compile; the plain "Add 'using'" fix remains available and valid for that shape because the scope local is a nullable reference type.

## [2.9.1] - 2026-06-09

### Changed

- **DI019 conditional-access receiver hardening**: DI019 now resolves the *true* provider receiver of a resolution call before classifying it. Previously, for `host?.Services.GetRequiredService<T>()` the analyzer classified the conditional-access receiver `host` (never a known root provider) instead of the `.Services` member binding, so scoped resolutions through `host?.Services...`, chained `app?.Services?...`, and local aliases such as `var rootServices = app?.Services;` were silently missed. Known root-provider properties (`Services`, `ApplicationServices`, `ServiceProvider`) and known scoped-provider properties (`RequestServices`, scope `ServiceProvider`) are now recognised when they appear as a `MemberBindingExpressionSyntax`, with the owner resolved from the enclosing `ConditionalAccessExpressionSyntax`. The scoped-provider recognition keeps `httpContext?.RequestServices...` and `scope?.ServiceProvider...` quiet inside singleton implementations now that the receiver reorder makes those shapes reachable.
- **DI019 code fix conditional-access guardrail**: The scope-wrapping code fix now refuses resolutions evaluated inside a conditional access's `WhenNotNull` (e.g. `var s = host?.Services.GetRequiredService<T>();`). Lifting that receiver into `using var scope = ....CreateScope();` would have emitted a standalone member binding that does not compile, and the wrap would also have dropped the null-shortcut semantics.

## [2.9.0] - 2026-06-01

### Added

- **DI020 — Middleware captures scoped dependency in constructor (new rule)**: Conventional middleware (registered via `app.UseMiddleware<T>()`) is instantiated once for the application lifetime, so a scoped service captured by its constructor is held for the whole process. DI020 flags constructor dependencies that reach a scoped service — both directly and transitively through the activation graph (reusing the same `ScopedDependencyGraph` walker as DI019) — and points to resolving them from the `Invoke`/`InvokeAsync` parameters instead. Warning severity.
- **DI019 resolution-path diagnostics**: When a scoped service is reached *indirectly* from a root provider, DI019 now renders the full activation chain in the message — `Service 'OrderProcessor' resolves scoped dependency from the root provider: OrderProcessor -> IInvoiceBuilder -> IRepository -> AppDbContext` — instead of naming only the two endpoints. `ScopedDependencyGraph` reconstructs the path as its depth-first search unwinds (cache-safe, no detection-behavior change), and the chain is also exposed through a `ResolutionPath` diagnostic property. This is strictly more actionable than the container's own `ValidateOnBuild` exception, which reports only the endpoints.
- **DI019 code fix**: DI019 now offers a code fix that wraps the offending resolution in a `using` scope (`CreateScope()` / `CreateAsyncScope()`) so scoped services are resolved from a child scope rather than the root provider.

## [2.8.26] - 2026-05-13

### Changed

- **DI003 fixer conditional-access lifetime rewrite**: The DI003 lifetime-adjustment code fix now also rewrites conditional-access registration invocations such as `services?.AddSingleton<TFoo, TFooImpl>()` to `services?.AddScoped<TFoo, TFooImpl>()` (or the appropriate replacement lifetime) when the invocation expression is a `MemberBindingExpressionSyntax`. Both `TryRewriteServiceCollectionRegistrationInvocation` and `TryGetCurrentLifetime` recognise the conditional-access shape so the fix is discovered and applied. The rewrite preserves the trigger's null-safe receiver.

## [2.8.25] - 2026-05-13

### Changed

- **DI015 fixer conditional-access self-binding**: The DI015 add-missing-registration code fix now also offers to insert a self-binding registration before a `services?.AddXxx<TService, TImpl>()` trigger when the invocation expression is a `MemberBindingExpressionSyntax` inside a `ConditionalAccessExpressionSyntax`. The inserted statement mirrors the trigger's null-safe shape (`services?.AddSingleton<MissingType>();`), so the new line stays null-safe under the same receiver guarantees. The standalone-block requirement is preserved.

## [2.8.24] - 2026-05-13

### Changed

- **DI013 fixer conditional-access removal**: The DI013 remove-invalid-registration code fix now also offers to remove `services?.AddSingleton(typeof(IService), typeof(WrongService))` and other conditional-access registrations when the invocation expression is a `MemberBindingExpressionSyntax` inside a `ConditionalAccessExpressionSyntax`. The fix still requires the enclosing statement to be a standalone block or top-level expression statement, so embedded single-line conditional-access bodies stay manual.

## [2.8.23] - 2026-05-13

### Changed

- **DI012 fixer conditional-access support**: The DI012 code fix now also offers to remove ignored `services?.TryAdd*(...)` registrations when the invocation expression is a `MemberBindingExpressionSyntax` inside a `ConditionalAccessExpressionSyntax`. The fixer still requires the enclosing statement to be a standalone block or top-level expression statement, so embedded single-line conditional-access bodies stay manual.

## [2.8.22] - 2026-05-13

### Changed

- **DI016 conditional-access receiver hardening**: DI016 now reports `BuildServiceProvider()` misuse when the call uses conditional access on the receiver chain, such as `builder.Services?.BuildServiceProvider()` (where the invocation's expression is a `MemberBindingExpressionSyntax` and the analyzer walks up to the enclosing `ConditionalAccessExpressionSyntax` to read the real receiver), `builder?.Services.BuildServiceProvider()` (where the `.Services` member-binding expression is recognized as a Services source), and chained `builder?.Services?.BuildServiceProvider()`. Provider-factory methods that wrap the same shape stay quiet.

## [2.8.21] - 2026-05-13

### Changed

- **DI005 conditional-access receiver coverage**: DI005 now detects `CreateScope()` calls whose invocation expression is a `MemberBindingExpressionSyntax` (the conditional-access form `_scopeFactory?.CreateScope()` or `_provider?.CreateScope()`) inside async methods, lambdas, local functions, anonymous methods, and async top-level statements. Symbol verification still requires the receiver to be `IServiceScopeFactory` or `IServiceProvider`, so unrelated `?.CreateScope()` calls on other types stay quiet.

## [2.8.20] - 2026-05-13

### Changed

- **DI018 delegate-type non-instantiable detection**: DI018 now reports delegate types registered without a factory expression, such as `services.AddSingleton<MyHandler>()` or `services.AddSingleton(typeof(MyHandler))` where `MyHandler` is a `delegate`. Delegates carry only implicit `(object, IntPtr)` and `(object, UIntPtr)` constructors that the default DI container cannot populate, so the registration fails at activation. Factory registrations and explicit delegate-instance registrations stay quiet.
- **RegistrationCollector one-Type self-binding**: the shared `RegistrationCollector` now self-binds the one-`Type` non-generic registration overloads (`services.AddSingleton(typeof(T))`, `AddScoped(typeof(T))`, `AddTransient(typeof(T))`, and the `TryAdd*` variants) by defaulting `implementationType` to `serviceType` in the operation-arguments extractor, matching the existing typeof-syntax fallback. The self-binding is gated to overload signatures that actually omit `implementationType` / `implementationFactory` / `implementationInstance`, so the two-`Type` overload with a non-extractable implementation argument such as `services.AddSingleton(typeof(IFoo), variableHoldingType)` keeps `implementationType = null` and no rule misreports a `IFoo -> IFoo` self-binding. This makes the previously-invisible one-`Type` registration shape participate in every downstream rule, not just DI018.

## [2.8.19] - 2026-05-13

### Changed

- **DI009 known-scoped-framework captive coverage**: DI009 now consults the shared `KnownServiceLifetimeClassifier` alongside the registration collector when resolving open-generic constructor dependency lifetimes, so open-generic singletons that capture `IOptionsSnapshot<T>` are reported as scoped captures even when the application does not register Options manually. Explicit closed user registrations such as `services.AddSingleton<IOptionsSnapshot<MyOptions>, MySnapshot>()` keep their declared lifetime and override the framework default for direct captures, and `IEnumerable<T>` captures take the worst (shortest-lived) lifetime across the user registration and the framework classifier so an explicit closed singleton element does not hide an additional open-generic framework scoped element the container still includes. `IOptions<T>` and `IOptionsMonitor<T>` keep their singleton classification and stay quiet.

## [2.8.18] - 2026-05-13

### Changed

- **DI019 nullable root-provider precision**: DI019 now recognizes nullable-known root provider surfaces used through the null-forgiving operator, such as `app.Services!.GetRequiredService<T>()`, so nullable annotations do not hide scoped root-resolution diagnostics.
- **DI016 services-source unwrap precision**: DI016 now unwraps the null-forgiving operator (`builder.Services!`) and same-type `IServiceCollection` casts (`(IServiceCollection)builder.Services`) when resolving the registration receiver, helper return expressions, and local-variable initializers, so builder-style flows that suppress nullable warnings or assert the interface no longer hide `BuildServiceProvider()` misuse. Provider-factory methods that return `IServiceProvider` remain silent.

## [2.8.17] - 2026-05-08

### Changed

- **DI001 branch-exit precision**: DI001 now reports `IServiceScope` creations whose later shared `Dispose()` / `DisposeAsync()` cleanup can be bypassed by a `return` or `throw` after the scope is created, while still accepting direct same-branch disposal before the exit and mutually exclusive branch exits that cannot run after the creation.

## [2.8.16] - 2026-05-08

### Changed

- **DI014 ownership-flow precision**: DI014 now treats `if`/`else` root-provider assignments to the same outer local as mutually exclusive when a later dispose covers the selected provider, while still reporting providers created repeatedly inside loops, disposed only after the loop, or created in branches with direct or nested `return`/`throw` exits before shared cleanup. Direct same-block or branch-block `Dispose()` / awaited `DisposeAsync()` before an exit, `finally` cleanup from `try` or `catch`, filtered catch exits, and switch `goto case` target matching remain recognized.

## [2.8.15] - 2026-05-08

### Changed

- **DI019 root-provider precision**: DI019 now treats `Services` properties as root providers only on known ASP.NET Core / Generic Host root types such as `WebApplication`, `WebApplicationFactory<TEntryPoint>` including derived or generic-constrained custom factories, `TestServer`, `IHost`, and `IWebHost`, avoiding false positives for arbitrary holder types whose scoped provider property happens to be named `Services`.
- **Stable local delegate factory coverage**: DI003 and DI015 now inspect stable local delegate factories passed to registrations, including later definite simple reassignments, predeclared delegates assigned after declaration, same-declaration delegate reassignments, recursive local delegate aliases, exhaustive local-function branch rewrites, method-group delegate aliases to local functions that rewrite the factory, and synchronous writes before the first `await` in unawaited async local functions, catching scoped captures or missing dependencies hidden behind `Func<IServiceProvider, T>` variables while keeping guard-clause / throwing exits, nested branches that return before registration, overwritten branch-local helper rewrites, branch-local helper rewrites on the only completing path, local-function guard returns, iterator local-function rewrites, no-op, mixed-value, and intervening-write alias cycles, reassigned helper delegate aliases, unrelated assignment left-hand-side uses including invoked anonymous delegates, short-circuit, switch-arm, `for` incrementor, and conditional-expression writes, conditional returns, unreachable `ref`/`out` writes, duplicate method-group branch requests, and opaque delegate-local writes such as direct delegate calls, delegate `.Invoke()` calls, or reachable `ref`/`out` reassignments conservative.
- **DI015 delegate branch de-duplication**: DI015 no longer reports duplicate missing-dependency diagnostics when an exhaustive `if` / `else if` / `else` local delegate factory branch contributes the same factory request during stable local factory analysis.

## [2.8.14] - 2026-05-07

### Changed

- **DI007 middleware boundary precision**: DI007 now treats `Invoke` / `InvokeAsync` as middleware exceptions only when they match the ASP.NET Core middleware shape (`Task` return and first `HttpContext` parameter), so arbitrary `Invoke` methods no longer suppress service-locator diagnostics.
- **DI007 generic-task middleware guardrail**: DI007 now rejects `Task<T>` returning `Invoke` / `InvokeAsync` methods as middleware boundaries, preserving diagnostics for non-middleware invoker methods.
- **DI007 factory-method precision**: DI007 now treats `Create*` / `Build*` methods as factory exceptions only when they return a value, so `void`, plain `Task`, and plain `ValueTask` side-effect methods no longer bypass service-locator reporting by name alone.
- **DI011 middleware boundary precision**: DI011 now uses the same ASP.NET Core middleware shape before suppressing provider-injection diagnostics, so arbitrary invoker classes no longer bypass `IServiceProvider` injection reporting.
- **DI011 generic-task middleware guardrail**: DI011 now rejects `Task<T>` returning `Invoke` / `InvokeAsync` methods as middleware boundaries, preserving provider-injection diagnostics for ordinary registered services.
- **DI011 factory-shape precision**: DI011 now treats `*Factory` classes and interfaces as provider-injection exceptions only when they expose value-returning factory members, so name-only factory markers and side-effect methods no longer suppress reporting. Singleton `IServiceScopeFactory` bridge patterns stay quiet to avoid conflicting with DI003's recommended scoped-work pattern.
- **DI003 fixer shape coverage**: Added lifetime-rewrite coverage for `TryAddSingleton(...)` registrations, inline factory diagnostics, named `ServiceDescriptor.Describe(..., lifetime: ...)` arguments, and `new ServiceDescriptor(...)` registrations so the fixer continues to update the owning registration or lifetime argument precisely.
- **DI012 fixer safety**: The ignored-`TryAdd*` code fix is now offered only for standalone block or top-level statements, avoiding unsafe removal of embedded single-line statement bodies such as `if (...) services.TryAdd...;` while preserving minimal-hosting `Program.cs` fixes.
- **DI012 keyed fixer coverage**: Added code-fix coverage for ignored `TryAddKeyed*` registrations so keyed conditional-registration fixes stay aligned with DI012's keyed analyzer grouping.
- **DI012 EF helper precision**: DI012 now respects EF Core helper registrations that preserve earlier explicit context registrations, avoiding duplicate-registration noise after `AddDbContextFactory(...)` and `AddPooledDbContextFactory(...)`.
- **DI013 fixer safety**: The invalid-registration removal assist is now offered only for standalone block or top-level statements, avoiding unsafe deletion of embedded single-line registration bodies while keeping symbol-backed type rewrite assists and minimal-hosting `Program.cs` removals available.
- **DI013 implementation-instance fixer coverage**: Added code-fix coverage for removing invalid implementation-instance registrations, retargeting them to an interface/base type implemented by the supplied instance, and preserving embedded single-line bodies by using symbol-backed rewrites instead of removal.
- **DI001 fixer safety**: The scope-disposal fixer no longer offers `await using` in synchronous callables just because `CreateAsyncScope()` was used, avoiding uncompilable fixes while preserving the plain `using` assist.
- **DI004 awaited move-fix coverage**: Added coverage proving the move-into-scope fixer keeps awaited immediate service calls inside the owning `using` block, preserving async use-after-dispose repairs.
- **DI005 fixer safety**: The async-scope fixer now rewrites only direct `using` resources, avoiding unsafe transformations when `CreateScope()` is nested inside another disposable resource initializer.
- **DI006 fixer safety**: The remove-`static` fixer now treats static lambdas as static contexts, avoiding invalid rewrites when cached providers are referenced from `static () => ...` delegates.
- **DI008 descriptor fixer coverage**: DI008 lifetime fixes now update `ServiceLifetime.Transient` arguments inside `ServiceDescriptor.Describe(...)` and `new ServiceDescriptor(...)` descriptor registrations, matching the analyzer's descriptor coverage.
- **DI014 nearest-callable fixer guardrail**: Added coverage ensuring the root-provider disposal fixer emits plain `using` inside synchronous lambdas even when they are nested in async methods, preserving the nearest-callable async boundary.
- **Public constructor activation precision**: DI010, DI011, and shared lifetime analysis now evaluate public activatable constructors instead of protected/internal helper constructors, matching DI018's no-public-constructor model and avoiding noisy design-smell diagnostics on constructors the container cannot call.
- **DI010 factory-return coverage**: DI010 now recognizes straight-line factory blocks and local-function method groups that perform setup statements before a single final `return new Service(...)`, including setup code with nested helper returns, while keeping branching multi-return factories conservative.
- **DI017 factory coverage lock**: Added regression coverage for circular dependencies through `ActivatorUtilities.GetServiceOrCreateInstance<T>(sp)` factory registrations, keeping it aligned with the shared factory-dependency path used by DI015.
- **Factory opacity guardrails**: Added paired DI015/DI017 regression coverage for mixed factory bodies that contain both a recognized `ActivatorUtilities` request and an unrelated helper invocation. DI015 still reports high-confidence missing dependencies, while DI017 keeps speculative cycle detection quiet through opaque factory behavior.
- **DI015 fixer registration-site coverage**: Added self-binding code-fix coverage for `TryAddTransient(...)` registrations and local `IServiceCollection` aliases, keeping the fixer aligned with the analyzer's reachable registration flow.
- **EF Core factory registration modeling**: DI003, DI015, and DI019 now model `AddDbContextFactory<TContext>()` registrations, treating the convenience `TContext` registration as scoped while keeping the factory and options lifetimes aligned with EF Core's configured factory lifetime. Custom `AddDbContextFactory<TContext,TFactory>()` context resolution is modeled as factory-backed so DI015 does not inspect dependencies owned by the custom factory.
- **EF Core pooled registration modeling**: DI003, DI015, and DI019 now model `AddDbContextPool(...)` and `AddPooledDbContextFactory(...)` registrations, including singleton options/factory services and scoped pooled context resolution. `AddDbContextFactory(..., ServiceLifetime.Transient)` now models the convenience context registration as transient instead of scoped.
- **EF Core service/implementation self-registration modeling**: DI003, DI015, and DI019 now model the implementation-type self-registration added by `AddDbContext<TService,TImplementation>(...)` and `AddDbContextPool<TService,TImplementation>(...)`, so direct `TImplementation` dependencies are no longer invisible when EF registers through an interface. Existing explicit `TImplementation` registrations keep their original lifetime.
- **EF Core `TryAdd` lifetime precision**: EF helper registrations now preserve existing explicit context, options, and factory registrations instead of overwriting them with synthetic `AddDbContext*` lifetimes, including scoped options/factory guardrails for DI003 and DI019 across factory and pooled registration helpers.

## [2.8.13] - 2026-04-27

### Changed

- **DI003 DbContext Guardrails**: Added regression coverage and documentation for DbContext-backed singleton captive dependencies, including `DbContextOptions<TContext>`, scoped repository/unit-of-work abstractions, `IDbContextFactory<TContext>` no-diagnostic behavior, and hosted-service scoped processor guidance.

## [2.8.12] - 2026-04-27

### Changed

- **DI006 Provider Cache Hardening**: DI006 now reports nested provider wrappers, provider-valued dictionaries, and simple holder types, with an `.editorconfig` opt-out for intentional holder patterns.
- **DI007 Context Precision**: DI007 now verifies hosted-service, options, and factory-boundary contexts more precisely so legitimate framework factories stay quiet while local provider delegates remain reportable.
- **DI008 Registration Coverage**: DI008 now covers `ServiceDescriptor`, `TryAddTransient` / `TryAddKeyedTransient`, and `TryAddEnumerable` descriptor shapes while preserving factory-registration guardrails and adding an intentional disposable-type allowlist.
- **DI008 Code Fix Coverage**: DI008 lifetime fixes now support `ServiceDescriptor.Transient(...)` registrations in addition to direct `AddTransient(...)` calls.

## [2.8.11] - 2026-04-27

### Changed

- **DI019 Root Collection Resolution**: DI019 now reports root-provider `GetService` / `GetRequiredService` calls for `IEnumerable<T>` when any resolved collection element is scoped.
- **DI019 ASP.NET Core Root Surfaces**: DI019 now recognizes additional root provider entry points such as `IApplicationBuilder.ApplicationServices`, `IEndpointRouteBuilder.ServiceProvider`, null-conditional root-provider resolutions, and keyed `AnyKey` scoped fallback registrations.

## [2.8.10] - 2026-04-27

### Changed

- **DI003 Keyed Inheritance Coverage**: DI003 now detects keyed singleton registrations that inherit their service key into `[FromKeyedServices]` constructor dependencies or keyed factory lambdas.
- **DI003 Collection Capture Precision**: DI003 now evaluates all matching `IEnumerable<T>`, `GetServices<T>()`, and `GetKeyedServices<T>()` elements so earlier scoped/transient collection registrations cannot be hidden by a later singleton registration.

## [2.8.9] - 2026-04-26

### Changed

- **Known Framework Scoped Lifetimes**: DI003 and DI019 now treat `IOptionsSnapshot<T>` as scoped even when the framework registration is not visible in source, while keeping `IOptions<T>` and `IOptionsMonitor<T>` safe for singleton/root-provider scenarios.
- **EF Core AddDbContext Modeling**: Registration collection now models EF Core `AddDbContext(...)` calls, including explicit `contextLifetime` values, last-registration-wins options lifetimes, and synthetic `DbContextOptions<TContext>` registrations, so DI003/DI019/DI015 reason about DbContext lifetimes without false missing-registration reports.

## [2.8.8] - 2026-04-26

### Changed

- **DI006 Lazy Provider Cache Coverage**: DI006 now reports static `Lazy<IServiceProvider>`, `Lazy<IServiceScopeFactory>`, and `Lazy<IKeyedServiceProvider>` members so deferred static provider caches no longer slip past the rule.
- **DI006 Regression Guardrails**: Added analyzer and code-fix coverage for lazy provider caches, keyed lazy providers, lazy non-provider silence, sample diagnostics, and docs guidance while preserving the conservative remove-`static` fixer boundary.

## [2.8.7] - 2026-04-26

### Changed

- **DI008 Named Type-Argument Precision**: DI008 now maps `serviceType:` and `implementationType:` by Roslyn parameter binding for non-generic `AddTransient(...)` / `AddKeyedTransient(...)` overloads, so out-of-order named arguments report the disposable implementation instead of trusting source order.
- **DI008 Factory Guardrails**: Keyed transient factory registrations with named arguments out of order now stay quiet, preserving the rule's intentional factory-registration boundary.

## [2.8.6] - 2026-04-26

### Changed

- **DI005 Top-Level Async Coverage**: DI005 now treats top-level programs that use `await` as async flows, reporting `CreateScope()` and offering the safe `await using` / `CreateAsyncScope()` fix for using declarations.
- **DI005 Regression Guardrails**: Added top-level report/no-report coverage so nested async local functions or lambdas do not make otherwise synchronous top-level scope creation noisy.

## [2.8.5] - 2026-04-26

### Changed

- **DI001 Conditional Ownership Precision**: DI001 now recognizes predeclared nullable scope locals assigned inside conditional or `try` blocks when a later conditional-access, non-null-guarded, or `finally` disposal reliably closes ownership.
- **DI001 Regression Guardrails**: Added coverage for if/else conditional ownership, `try`/`finally` disposal, non-null guard disposal, reassignment leaks, and loop-created scopes that still require deliberate per-iteration disposal.

## [2.8.4] - 2026-04-26

### Changed

- **DI014 Disposal-Proof Precision**: DI014 now reports root providers whose only explicit disposal is conditional, catch-only, or applied after the local provider variable has been reassigned.
- **DI014 Code Fix Safety**: The `using` code fix is no longer offered when the provider local already has manual disposal code that should be rewritten deliberately instead of layered with another disposal pattern.

## [2.8.3] - 2026-04-26

### Changed

- **DI001 Disposal-Proof Precision**: DI001 now reports scopes whose only `Dispose()` / `DisposeAsync()` proof is guarded by a conditional branch, switch section, loop, or catch block, while preserving straight-line and `finally` disposal patterns.
- **DI001 Regression Coverage**: Added focused analyzer guardrails for conditional and catch-only disposal so the rule no longer suppresses leaks just because a later disposal call appears syntactically in the method.

## [2.8.2] - 2026-04-26

### Changed

- **DI002 Delegate Escape Coverage**: DI002 now reports scoped services captured by delegates that later escape through returns, fields, properties, or `ref` / `out` parameters, while staying quiet when the delegate is reassigned before escaping.
- **DI002 Fixer Regression Coverage**: Expanded pragma-suppression coverage for alias-return, local-function return, lambda-body assignment, `ref` parameter, and captured-delegate escape diagnostics so the fixer continues to suppress the originating resolution statement safely.

## [2.8.1] - 2026-04-26

### Changed

- **DI004 Code Fix Safety**: The move-into-scope fix is now offered only for immediate invocation-style uses whose diagnostic local was assigned inside the immediately preceding `using` block, preventing unsafe rewrites into unrelated later scopes or escape assignments.
- **DI004 Fixer Regression Coverage**: Added guardrails for unrelated adjacent scopes, escape assignments, nested-boundary assignments, invocation-argument diagnostics, and `await using` statement movement.

## [2.8.0] - 2026-04-23

### Changed

- **DI017 Circular Dependency Hardening**: DI017 now analyzes reachable, flow-aware effective registration graphs, including `TryAdd`, duplicate overrides, `RemoveAll` / `Replace` removal, high-confidence factory requests, keyed inherit-key dependencies, open generics, and registered `IEnumerable<T>` elements while staying silent for opaque or ambiguous paths.
- **DI017 Regression Coverage**: Added focused analyzer and performance coverage for factory cycles, keyed inheritance, collection element cycles, open-generic cycles, unreachable wrappers, separate service collections, registration mutation suppression, shadowed `TryAdd`, and larger factory/collection graphs.

## [2.7.0] - 2026-04-23

### Added

- **DI019 Root Scoped Resolution**: Added a warning-level analyzer that detects scoped services, or service graphs that reach scoped services, resolved from a root `IServiceProvider` such as `app.Services`, `host.Services`, or `BuildServiceProvider()` results.
- **Root-Provider Graph Coverage**: DI019 covers direct, transitive, enumerable, keyed, singleton implementation, and hosted-service root-provider resolutions while staying silent for scoped providers from `CreateScope()` / `CreateAsyncScope()`, `HttpContext.RequestServices`, dynamic service-type requests, and DI factory lambdas already handled by DI003.

### Changed

- **Hosted Service Registration Modeling**: Registration collection now models `AddHostedService<T>()` as a singleton `IHostedService` registration so lifetime analyzers can reason about hosted-service dependency graphs.
- **DI019 Samples and Docs**: Added sample diagnostics, rule docs, analyzer health metadata, release tracking, and sample/docs freshness mappings for the new rule.

## [2.6.0] - 2026-04-23

### Changed

- **DI015 Keyed Resolution Hardening**: Unresolvable-dependency analysis now supports inherited keyed constructor dependencies, treats `[ServiceKey]` parameters as container-provided, and recognizes `KeyedService.AnyKey` fallback registrations while continuing to suppress dynamic or uncertain keyed lookups.
- **DI015 Factory Coverage**: Factory analysis now covers `ActivatorUtilities.GetServiceOrCreateInstance(...)` and keyed factory key-parameter flows for high-confidence `GetRequiredKeyedService<T>(key)` requests.
- **DI015 Registration Mutation Precision**: Same-flow `RemoveAll(...)` and `Replace(...)` mutations now suppress diagnostics for registrations they definitely remove, reducing false positives in override-heavy registration modules.
- **DI015 Code Fixes and Docs**: The code fix can now add keyed concrete self-bindings and direct factory missing-dependency self-bindings when the registration site is local and safe. DI015 docs, samples, and regression coverage were refreshed around the expanded behavior.

## [2.5.2] - 2026-04-23

### Changed

- **DI004 Scope-State Hardening**: Use-after-dispose analysis now recognizes explicit `Dispose()` / `DisposeAsync()`, conditional/member access, field/property/parameter sinks, deconstruction, `await foreach`, keyed constant resolutions, mixed `GetServices<T>()` registrations, and high-confidence deferred delegate captures while keeping unknown or dynamic lifetimes silent.
- **DI004 False-Positive Reduction**: Post-disposal service and delegate reassignments now clear stale tracked state before later use, including delegate aliases and delegate overwrites after the owning scope has ended.
- **DI004 Code Fixes**: Added safe local assists to move simple immediate uses back into the owning `using` block and to add narrow `#pragma` suppression for context-dependent cases.
- **DI004 Regression Guardrails**: Added focused analyzer, code-fix, inventory, and performance coverage for the expanded DI004 behavior.

## [2.5.1] - 2026-04-23

### Changed

- **DI013 Precision Hardening**: Implementation type mismatch analysis now uses Roslyn assignability for closed types, reducing false positives for valid generic variance while preserving strict open-generic projection checks.
- **DI013 Registration Shape Coverage**: Direct registration extraction now handles named `serviceType`, `implementationType`, `implementationInstance`, and keyed `serviceKey` arguments in arbitrary order.
- **DI013 Code Fixes**: Added broad symbol-backed assists to remove invalid standalone registrations, replace implementation types with compatible candidates, or retarget the service type to an interface/base implemented by the current implementation. FixAll remains disabled for this rule.
- **Rule Documentation Sync**: Refreshed stale DI012 rule-reference and analyzer-health code-fix metadata while updating DI013, keeping public docs aligned with the reflected provider inventory.

## [2.4.6] - 2026-04-02

### Changed

- **Code Fix Inventory Guardrails**: Added parity tests that compare the README rule index against the reflected code-fix provider inventory, ensuring documented fixable rules stay aligned with shipped `FixableDiagnosticIds` and that providers only advertise public diagnostics.

## [2.4.5] - 2026-04-01

### Changed

- **DI012 Ignored TryAdd Code Fix**: Added a narrow, real code fix for the `TryAddIgnored` variant of DI012 that removes redundant ignored `TryAdd*` registrations when they appear as standalone statements, including wrapper-method cases, while leaving duplicate-registration scenarios diagnostic-only.

## [2.4.4] - 2026-04-01

### Changed

- **Analyzer Health Guardrails**: Added full diagnostic inventory parity coverage for public IDs, severities, descriptors, and shipped/unshipped release tracking so new diagnostics cannot drift silently from test and release metadata.
- **CI/Release Provenance Hardening**: CI now validates release tags against the project version, updates the coverage badge on the repository default branch, and publishes release-tag artifacts that the release workflow promotes directly to NuGet and GitHub Releases instead of rebuilding.
- **Compatibility Smoke Coverage**: Added a focused DI-version smoke matrix around DI016, DI017, and DI018 plus reusable reference-assembly sets in the verifier so representative analyzer scenarios stay covered across supported package combinations.
- **Code Fix Policy Cleanup**: DI016 remains diagnostic-only, and DI002 now offers only explicit pragma suppression instead of acknowledgement/TODO-style actions.

## [2.4.3] - 2026-04-01

### Changed

- **DI016 Builder-Flow Hardening**: BuildServiceProvider misuse detection now recognizes assignable `IServiceCollection` abstractions, same-boundary alias reassignment, and helper methods that forward builder-style `.Services` flows, while still staying quiet for provider-factory methods and standalone top-level `ServiceCollection` usage.
- **DI009 Constructor and Collection Precision**: Open-generic captive-dependency analysis now treats optional/default constructor parameters as activatable during likely-constructor selection, suppresses diagnostics on ambiguous equally-greedy constructors, and reports `IEnumerable<T>` captures against the element service lifetime.
- **DI012 Flow and Opaque-Barrier Accuracy**: Conditional-registration analysis now follows same-collection local aliases plus source-defined helper/local-function wrappers more reliably, isolates distinct object-created collection flows, and treats source-declared but bodyless registration helpers as opaque ordering barriers instead of speculating across them.

## [2.4.2] - 2026-04-01

### Changed

- **DI002 Executable-Root Coverage**: Scoped-service escape analysis now runs across constructors, accessors, local functions, lambdas, and anonymous methods instead of only ordinary methods, while still keeping escape tracking local to the current executable boundary.
- **DI004 Executable-Root Coverage**: Use-after-dispose analysis now follows the same broader executable-root set, including constructor and accessor bodies plus local and anonymous functions, without speculating across nested executable boundaries.
- **DI002/DI004 Regression Guardrails**: Added direct should-report and should-not-report coverage for constructor, property-getter, local-function, anonymous-method, and nested-boundary cases so the widened entry surface stays conservative.

## [2.4.1] - 2026-04-01

### Changed

- **DI010 Runtime-Faithful Constructor Selection**: Constructor over-injection now analyzes the constructor set the container could actually activate instead of every accessible constructor, reducing false positives on multi-constructor services where only a shorter constructor is resolvable.
- **DI010 Conservative Factory Coverage**: The analyzer now covers straightforward factory registrations that directly construct the service and `ActivatorUtilities.CreateInstance<T>(sp)` factory paths, while staying quiet for more dynamic factory bodies it cannot classify confidently.
- **DI010 Symbol-Accurate Exclusions and Config**: Logging, options, configuration, and provider-plumbing exclusions now use exact symbols instead of simple names, optional/default-value parameters are ignored, and the dependency threshold is configurable through `.editorconfig` with `dotnet_code_quality.DI010.max_dependencies`.
- **DI010 Regression Coverage and Health**: Added direct tests for constructor selection, factory registrations, namespace-accurate exclusions, optional parameters, and threshold overrides, and refreshed the analyzer health snapshot to reflect the stronger rule quality.

## [2.4.0] - 2026-03-31

### Changed

- **DI002 Scope/Alias Tracking**: Scoped-service escape analysis now follows scopes created before a `using (scope)` statement, recognizes provider aliases, and stays quiet after safe alias overwrites instead of keeping stale escape state alive.
- **DI004 Scope/Alias Disposal Coverage**: Use-after-dispose analysis now recognizes services resolved from predeclared scopes and provider aliases, and it clears stale tracked aliases after safe overwrites to avoid new false positives.
- **DI009 Registration-Shape Coverage**: Open-generic captive-dependency coverage now has direct tests for `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, and keyed open-generic singleton registrations, plus no-diagnostic guardrails for mismatched keys and ineffective `TryAddSingleton(...)`.
- **Analyzer Health Snapshot**: Refreshed the rule-health scoring after the DI002/DI004/DI009 hardening pass and rolled the direct-test counts forward to the current suite.

## [2.3.9] - 2026-03-31

### Changed

- **DI016 Minimal-Hosting Coverage**: BuildServiceProvider misuse detection now recognizes top-level registration code that flows from a builder-style `.Services` property, closing the missed-diagnostic gap for minimal-hosting registration composition without expanding the rule to every top-level `IServiceCollection` usage.
- **DI016 Diagnostic Precision**: DI016 now reports on the `BuildServiceProvider` member name instead of highlighting the full invocation, producing tighter squiggles in the IDE and test output.
- **DI016 Regression Coverage**: Added tests for `IServiceCollection` variable indirection, top-level `.Services` aliases, and a top-level standalone `new ServiceCollection()` no-diagnostic guardrail so the rule stays conservative.

## [2.3.8] - 2026-03-31

### Changed

- **DI018 Open-Generic Activatability**: Non-instantiable implementation detection now checks unbound generic registrations against the generic type definition, so open generic implementations with only private, internal, or protected constructors are reported instead of slipping through to runtime activation failures.
- **DI018 Registration-Shape Coverage**: Added direct guardrails for keyed registrations, `TryAddSingleton(...)`, `ServiceDescriptor.Singleton(...)`, `ServiceDescriptor.Describe(...)`, `typeof(...)` registrations, and factory/implementation-instance silence so DI018 stays aligned with the collector paths the analyzers actually support.
- **DI018 Sample and Docs Parity**: Expanded the DI018 sample, contract, and rule docs to include constructor-accessibility failures in addition to abstract implementations, and refreshed the analyzer health snapshot to reflect the stronger coverage.

## [2.3.7] - 2026-03-31

### Changed

- **Instance-Backed Registration Fidelity**: Shared registration metadata now tracks pre-built implementation instances explicitly so constructor-driven analyzers and dependency resolution stay aligned with runtime behavior instead of treating existing objects like activatable implementation types.
- **False-Positive Reduction Across Rules**: DI003, DI010, DI011, DI015, DI017, and DI018 now stay silent for valid implementation-instance registrations, including `ServiceDescriptor` instance forms and generic `implementationInstance:` overloads.
- **DI017 Determinism Hardening**: Circular-dependency analysis now builds its effective registration set from stable source order, preventing duplicate or mixed instance-plus-constructed registrations from changing results due to concurrent discovery order.
- **Sample Verification Stability**: Full-solution test runs now prebuild the analyzer and rebuild sample projects without rebuilding project references, avoiding fragile analyzer-output races in the sample diagnostics verifier.
- **Regression Coverage Expansion**: Added direct tests for generic and named implementation-instance collection, `ServiceDescriptor` instance shapes, mixed instance-plus-constructed DI017 graphs, and the cross-rule fallout cases that motivated this pass.

## [2.3.6] - 2026-03-31

### Changed

- **DI013 Open-Generic Compatibility Hardening**: Implementation-type mismatch analysis now rejects open-service registrations backed by closed generic implementations and validates open-generic projection ordering and arity more precisely across interface and base-type mappings.
- **DI013 Registration-Shape Coverage**: Added direct guardrails for `ServiceDescriptor.Singleton(...)`, `ServiceDescriptor.Describe(...)`, keyed registrations, `TryAddSingleton(...)`, implementation-instance registrations, and other collector-fed registration shapes so compatibility checks stay aligned with the registrations the analyzers actually understand.
- **Analyzer Health Snapshot**: Added `docs/ANALYZER_HEALTH.md` with per-rule scoring, pass recommendations, and current verification status to make future hardening priorities explicit.

## [2.3.5] - 2026-03-31

### Changed

- **DI005 Extension-Method Coverage**: Added analyzer and code-fix coverage for `IServiceProvider.CreateScope()` in async and sync contexts, including both `using var` and block-form `using (...)` fixer rewrites to `CreateAsyncScope()`.
- **DI005 Ownership and Boundary Guardrails**: Locked expected behavior for `CreateScope()` passed into helper methods, returned from async methods, synchronously disposed inside async methods, and used inside async anonymous methods.
- **DI005 Async-Context Precision**: Added no-diagnostic guardrails for synchronous lambdas and synchronous local functions nested inside async methods so future DI005 changes preserve correct executable-boundary behavior.

## [2.3.4] - 2026-03-31

### Changed

- **DI001 Explicit Disposal Accuracy**: Scope-disposal analysis now recognizes `scope?.Dispose()` and reassignment-to-local disposal patterns, reducing false positives for valid explicit disposal code.
- **DI001 Reassignment Leak Detection**: Explicit disposal tracking now rejects dispose proofs when the local is reassigned before disposal, including non-scope reassignments, while respecting executable boundaries so lambda-only reassignments do not create noise.
- **DI001 Regression Coverage**: Added guardrail tests for conditional disposal, reassignment-to-local, intervening reassignments, expression-based `using (CreateScope())`, and `IServiceProvider.CreateScope()` report/no-report behavior.

## [2.3.3] - 2026-03-31

### Changed

- **DI003 Deduplication**: Captive-dependency reporting now emits one diagnostic per unique captured dependency per registration, reducing duplicate reports when the same scoped/transient service is resolved multiple times inside one singleton factory.
- **Shared Key and Constant Helpers**: Extracted keyed-parameter parsing and constant-value extraction into shared infrastructure so DI003, DI015, and registration/factory analysis stay aligned on keyed-service semantics.
- **DI003 Regression Coverage**: Added guardrail tests for `TryAddSingleton` captive dependencies, open-generic constructor no-diagnostic behavior, repeated factory resolutions of the same scoped dependency, and keyed factory resolutions with a `null` key.

## [2.3.2] - 2026-03-30

### Changed

- **DI008 Keyed Registration Fixes**: `AddKeyedTransient(...)` is now fully supported by the DI008 code fix provider, including keyed lifetime rewrites to `AddKeyedScoped` / `AddKeyedSingleton` and keyed factory rewrites that preserve the original key argument and generate the correct two-parameter factory lambda shape.
- **DI008 Factory Detection Hardening**: Disposable-transient analysis now exercises both delegate-variable and method-group factory registrations for keyed and non-keyed DI paths, keeping DI008 conservative when registrations already use caller-owned factory delegates.
- **DI008 Regression Coverage**: Added DI 8 keyed-service analyzer/code-fix tests, guardrails for no-fix `typeof(...)` factory conversions, and named-key preservation checks so the highest-risk keyed paths are locked by executable coverage instead of code inspection alone.

## [2.3.1] - 2026-03-30

### Changed

- **DI006 Inherited-Type Coverage**: Static provider cache analysis now catches custom interfaces that inherit from `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider`, closing a real false-negative path for wrapper abstractions.
- **DI006 Fixer Safety**: The remove-`static` code fix now stays conservative by refusing fixes for static classes, non-private members, partial types, static local-function usage, and other cases where rewriting could silently break references or produce invalid code.
- **DI006 Regression Coverage**: Added targeted should-fix and should-not-fix tests for inherited interface detection, public member no-fix behavior, implicitly private members, static local functions, static classes, multi-variable fields, and partial-type guardrails.

## [2.3.0] - 2026-03-30

### Added

- **DI017 Circular Dependency Detection**: New diagnostic that detects direct, transitive, and self-referential circular dependencies in constructor injection chains. Uses keyed-service-aware resolution with memoized graph traversal for performance. Conservative by design — skips factory registrations, optional parameters, and `IEnumerable<T>` dependencies to avoid false positives. Reports deterministically on the lexicographically smallest service type in each cycle.
- **DI018 Non-Instantiable Implementation Detection**: New diagnostic that catches implementation types the DI container cannot construct — abstract classes, interfaces, static classes, and types with no public constructors. Skips factory registrations where the factory handles construction. Catches registration mistakes that would otherwise surface as runtime `InvalidOperationException`.
- **Sample Parity Enforcement**: New contract tests that enforce every public diagnostic ID has sample coverage with concrete claim anchors, all contract folder claims reference existing sample directories, and all claimed diagnostic IDs exist in the public inventory.
- **Cross-Rule Interaction Tests**: New test suite validating expected multi-rule behavior — DI007+DI011, DI003+DI015, DI008+DI012b, and DI010+DI011 interactions on shared source.
- **Performance Regression Gate**: Deterministic stress tests with 200-service registration sets, diamond dependency patterns, and large-scale cycle detection. Memoized cycle detection prevents exponential blowup on acyclic graphs.

### Changed

- **DI012 Accuracy**: Wrapper flow ordering restored for invoked helper methods so TryAdd-after-Add detection correctly follows service collection parameters across method boundaries.
- **DI011 Precision**: Provider heuristics narrowed to honor selected constructors and reduce service-locator escape hatches, keeping diagnostics focused on real DI misuse.
- **DI012 Flow Scoping**: Duplicate registration analysis scoped to service-collection flows to prevent cross-wrapper false positives.
- **Sample Verification Hardening**: SARIF-based contract verification tightened with approved secondary diagnostic allowlisting, public diagnostic parity checks, and sample/docs freshness gate integration.

## [2.2.2] - 2026-03-17

### Changed

- **DI015 Wrapper Reachability**: DI015 now expands source-visible `IServiceCollection` wrapper extensions from real invocation sites instead of treating registrations inside uncalled wrappers as globally available.
- **DI015 Opaque Wrapper Suppression**: Earlier opaque or external wrappers on the same `IServiceCollection` flow now suppress DI015 when registration state is uncertain, reducing false positives in layered registration modules.
- **DI015 Ordering and Flow Isolation**: Wrapper-aware resolution now respects call order and per-collection flow, so wrappers invoked on a different `IServiceCollection` instance or after a registration do not hide genuine missing-dependency diagnostics.
- **DI015 Regression Coverage**: Added should-report and should-not-report guardrails for invoked vs uninvoked wrappers, nested wrapper chains, cyclic wrappers, keyed mismatches, opaque external wrappers, same-method flow isolation, and registration ordering.
- **Release Metadata Sync**: Backfilled the missing `2.2.1` changelog entry and resynced package metadata so the tagged release train and project version history are aligned again.

## [2.2.1] - 2026-03-16

### Changed

- **DI013 Compatibility Hardening**: Expanded implementation-type validation beyond simple `typeof(service), typeof(implementation)` assignability checks so DI013 now also reports deterministic-invalid self-registrations, abstract/interface implementations, private-constructor implementations, and invalid implementation instances when their exact runtime type is known.
- **DI013 Open Generic Precision**: Reworked open-generic validation to require exact generic-parameter projection compatibility, rejecting arity mismatches, reordered parameters, transformed generic arguments, and other registrations that compile but cannot be activated by the built-in container.
- **Collector and Engine Support**: Extended registration collection to recognize non-factory implementation-instance overloads, keyed registrations, and equivalent `ServiceDescriptor` forms while keeping factory registrations out of DI013 scope and preventing constructor-based analyzers from treating pre-built instances like activatable implementation types.
- **Regression Coverage and Docs**: Expanded DI013 tests across valid/invalid closed, open-generic, keyed, `TryAdd*`, `ServiceDescriptor`, and implementation-instance scenarios, and updated `README.md`, `docs/RULES.md`, the sample app, and descriptor wording to match the hardened behavior.

## [2.2.0] - 2026-03-16

### Changed

- **DI015 Precision Refactor**: Moved unresolved-dependency analysis onto a shared resolution engine with explicit confidence/provenance tracking, so constructor, factory, keyed, open-generic, and strict-mode paths share the same conservative resolution rules.
- **DI015 TryAdd/Descriptor Coverage**: Effective `TryAdd*` registrations now participate in DI015 analysis, while shadowed `TryAdd*` registrations stay silent to match runtime behaviour; coverage also now includes `ServiceDescriptor` registration forms and direct `IKeyedServiceProvider` resolutions.
- **DI015 Code Fix**: Added a narrow safe fix that inserts a self-binding registration for one direct concrete constructor dependency when the registration site is local and unambiguous; factory-rooted, keyed, abstract, multi-missing, and transitive-only cases intentionally remain no-fix.
- **DI015 Guardrails**: Expanded DI015 regression coverage with paired should-report / should-not-report scenarios for `TryAdd*`, `ServiceDescriptor`, framework-provided dependencies, opaque duplicate registrations, and fixer/no-fixer boundaries.
- **Packaging Metadata**: Expanded NuGet description, package tags, and release notes so search and package landing pages better describe DI lifetime, scope, and registration coverage.
- **Adoption Docs**: Added `docs/ADOPTION.md` and linked it from `README.md` so teams evaluating the analyzer have a fast install and rollout path.
- **Repository Intake**: Added GitHub issue-template routing to point users toward setup guidance and the full rule reference before they file issues.
- **Growth Automation**: Added `tools/generate-growth-assets.mjs` to generate a searchable static docs site, problem-intent landing pages, version-synced README install snippets, and curated release notes from the repo source material.
- **GitHub Pages**: Added automated Pages publishing plus CI verification for the generated docs site, sitemap, robots.txt, and search index.
- **Release Surfaces**: Updated release automation to generate GitHub Release body content and package release-note input from `CHANGELOG.md` instead of relying on generic auto-generated notes.

## [2.1.4] - 2026-03-16

### Changed

- **DI003 Factory Coverage**: Expanded captive-dependency analysis to cover high-confidence factory paths including method-group factories, local-function factories, keyed resolutions, `ActivatorUtilities.CreateInstance(...)`, and open-generic lifetime lookups.
- **DI003 Signal Quality**: Added diagnostic deduplication for equivalent captive constructor findings and kept analysis conservative when dependency lifetimes or constructor activation paths are ambiguous.
- **DI003 Code Fixes**: Extended safe lifetime rewrites beyond direct `AddSingleton` calls to include keyed registrations, non-generic `typeof(...)` registrations, and supported `ServiceDescriptor` forms while continuing to skip unsafe refactors.
- **Shared Infrastructure**: Extracted reusable factory-invocation analysis so DI003 and DI015 stay aligned on method-group and helper-body traversal behavior.
- **Regression Coverage and Docs**: Added targeted DI003 tests for new analyzer/fixer paths and updated `README.md` plus `docs/RULES.md` to reflect the broadened factory support and safe-fix scope.

## [2.1.3] - 2026-03-16

### Changed

- **False Positive Reduction**: Narrowed `DI003` to true singleton captive-dependency cases, allowed `DI011` infrastructure patterns such as hosted services and endpoint filter factories, and taught `DI014` to recognize explicit owner disposal in `Dispose`/`DisposeAsync`.
- **Sharper Guidance**: Improved `DI007` so non-generic `GetService(typeof(...))` calls report the actual requested service type instead of degrading to `object`.
- **Safer Code Fixes**: Hardened `DI005`, `DI006`, and `DI008` so fixes are only offered when they can be applied without breaking code or generating uncompilable output.
- **Fixer Robustness**: Updated `DI003` and `DI009` code fixes to consume structured diagnostic metadata instead of parsing English diagnostic messages.
- **Documentation and Samples**: Synced `README.md`, `docs/RULES.md`, and the sample app with the hardened analyzer behavior and current warning output.

## [2.1.2] - 2026-03-16

### Changed

- **DI007 / DI011**: Lowered default severity from `Warning` to `Info` so broad service-location and `IServiceProvider`-injection guidance is still visible without warning-level noise by default.
- **Severity Guardrails**: Added a descriptor severity regression test to lock the intended default confidence split between runtime-failure rules and softer design-smell rules.

## [2.1.1] - 2026-02-06

### Changed

- **DI001 / DI014**: Reduced false positives by recognising explicit disposal inside the same lambda/local-function execution boundary while still warning when disposal is only deferred into a different boundary.
- **DI007**: Reduced false positives by continuing context analysis from lambdas up to containing methods (for example `Create*`/`Build*` factory methods), instead of stopping early.
- **DI008**: Reduced false positives with stricter symbol-based `AddTransient` detection and factory method-group detection for member-access expressions (for example `FactoryMethods.Create`).
- **Tests**: Added dedicated regression coverage for all fixed cases plus negative guardrail scenarios to ensure diagnostics still fire for deferred-lambda disposal patterns.

## [2.1.0] - 2026-02-06

### Added

- **DI016**: New analyser detecting `BuildServiceProvider()` misuse during service-registration composition (for example in `ConfigureServices`, `IServiceCollection` extension registration methods, and registration lambdas).

### Changed

- **Documentation/Samples**: Added DI016 rule documentation and sample coverage, including conservative false-positive guardrail notes.

## [2.0.0] - 2026-02-06

### Changed

- **DI015**: Expanded dependency resolution from direct constructor parameters to transitive constructor chains, including open-generic dependency closures.
- **DI015**: Factory registrations using `GetRequiredService`/`GetRequiredKeyedService` now resolve transitive dependency graphs and report missing leaf dependencies.
- **DI015**: Added conservative cycle handling (treat cycles as resolvable) and preserved framework/factory assumptions to keep false positives low.
- **Tests**: Added transitive DI015 regression coverage for constructor chains, factory-rooted chains, open-generic transitive resolution, and circular dependency paths.

## [1.13.0] - 2026-02-06

### Changed

- **DI002**: Reduced false positives by reporting scope-escape diagnostics only when the resolved service lifetime is known and scoped.
- **DI004**: Reduced false positives by reporting use-after-dispose diagnostics only when the resolved service lifetime is known and scoped/transient.
- **RegistrationCollector**: Improved fallback symbol handling to track DI registrations in unresolved/ambiguous invocation cases, including `Add*(typeof(...))` patterns.
- **Tests**: Added and updated regression coverage for unknown-lifetime suppression and `typeof`-based registration tracking in collector/rule/code-fix scenarios.

## [1.12.0] - 2026-02-05

### Added

- **Quality Gates**: Added CI coverage thresholds (line + branch) and release-time tag/version validation.
- **Community Files**: Added issue templates, pull request template, and `CODE_OF_CONDUCT.md`.
- **Documentation**: Added `docs/RULES.md` as the deep-dive rule reference and reshaped `README.md` into a quickstart-focused guide.

### Changed

- **RegistrationCollector**: Added all-registration tracking (`AllRegistrations`) so analyzers can inspect duplicate `Add*` registrations instead of only the last one.
- **Analyzer Coverage for Duplicates**: Updated DI003, DI009, DI010, DI011, DI013, and DI015 to analyze all discovered `Add*` registrations.
- **DI002**: Switched to symbol-based tracking and lifetime-aware filtering to reduce false positives (known singleton/transient registrations no longer trigger scope-escape diagnostics).
- **DI004**: Switched to symbol-based tracking and lifetime-aware filtering (known singleton services resolved from scope are excluded from use-after-dispose diagnostics).
- **DI001 / DI014**: Hardened explicit-dispose detection by requiring symbol-matched dispose calls that occur after creation, reducing false negatives from name-only matching.
- **Symbol Matching**: Improved service-provider and registration matching logic to prefer robust symbol/namespace checks while preserving compatibility with test stubs.
- **Tests**: Expanded regression coverage for variable shadowing, singleton scope-usage scenarios, duplicate registration analysis, and dispose-before-create edge cases.

## [1.11.0] - 2026-02-05

### Changed

- **DI015**: Added factory method-group analysis support (for registrations such as `AddSingleton<IMyService>(CreateMyService)`) and corresponding test coverage.
- **DI015**: Added factory-path analysis for `ActivatorUtilities.CreateInstance(...)` and introduced `.editorconfig` option `dotnet_code_quality.DI015.assume_framework_services_registered` for strict vs framework-assumed dependency checks.
- **DI015**: Hardened factory analysis by supporting named factory arguments, limiting method-body traversal to true method-group registrations, and applying `.editorconfig` framework-service assumptions per syntax tree.

## [1.10.0] - 2026-02-05

### Added

- **DI015**: New analyzer detecting registered services that depend on unregistered dependencies in constructor injection and `GetRequiredService` factory paths, including keyed and open-generic checks.
- **Samples**: Added `samples/DI015InAction`, a dedicated runnable sample showing broken and fixed DI015 registration patterns.

### Changed

- **DI015**: Reduced false positives for multi-constructor services by only reporting when no constructor is fully resolvable, and avoided duplicate reports for factory registrations that include implementation type metadata.

## [1.9.0] - 2026-02-05

### Fixed

- **DI001 / DI014**: Avoided false negatives where any enclosing `using` statement was incorrectly treated as disposing a scope/provider created elsewhere.
- **DI003**: Fixed non-generic service-resolution argument mapping for reduced extension methods (`GetService(typeof(...))`, `GetRequiredService(typeof(...))`), preventing missed captive-dependency reports.
- **Constructor Selection**: Added shared constructor-selection logic honoring `[ActivatorUtilitiesConstructor]` and preventing regressions with new analyzer tests.

### Changed

- **DI003**: Expanded non-generic and keyed resolution handling for factory delegates.
- **CI/Tooling**: Added `global.json`, updated CI/release workflows to install .NET 8 and .NET 10, and aligned test infrastructure/packages with current Roslyn testing defaults.
- **Tests**: Added targeted regression coverage across DI003, DI009, DI010, and DI011 for constructor-selection behavior and factory-resolution edge cases.

## [1.8.0] - 2025-12-11

### Fixed

- **DI012 (Conditional Registration Misuse)**: Keyed service registrations are now grouped by `(serviceType, key)` so different keys no longer trigger false duplicate/TryAdd diagnostics.

### Changed

- **RegistrationCollector**: Returns `null` when `IServiceCollection` is unavailable, avoiding unnecessary analysis in projects without DI references.
- **Dependencies**: Upgraded Roslyn to 5.0.0 and analyzer infrastructure packages to latest stable versions.
- **Tests**: Migrated from deprecated `*.Testing.XUnit` packages to core testing packages with `XUnitVerifier`, and bumped xUnit/Test SDK/Coverlet to current versions.

## [1.6.0] - 2025-11-26

### Changed

- **RegistrationCollector**: Improved robustness for parsing `ServiceDescriptor` arguments, correctly handling named arguments and integer-casted `ServiceLifetime` values.
- **DI014 Code Fix**: Enhanced to apply `await using` in asynchronous contexts and to preserve all leading/trailing trivia (comments, indentation) correctly.

## [1.5.0] - 2025-11-26

### Added

- **DI014 Code Fix**: Added code fix to automatically dispose root `IServiceProvider` instances.

### Changed

- **RegistrationCollector**: Enhanced to support services registered via `new ServiceDescriptor(...)` and `ServiceDescriptor.Describe(...)`. This improves detection accuracy for all analyzers relying on service registration data.

## [1.4.0] - 2025-11-26

### Added

- **DI013**: New analyzer detecting implementation type mismatches in `typeof` registrations (e.g. `AddSingleton(typeof(IService), typeof(BadImpl))`).
- **DI014**: New analyzer detecting undisposed root `IServiceProvider` instances created by `BuildServiceProvider()`.

## [1.3.0] - 2025-11-26

### Added

- **DI003**: Enhanced Captive Dependency analyzer to support factory delegate registrations (e.g., `AddSingleton(sp => new Service(sp.GetRequiredService<IScoped>()))`).

### Changed

- **RegistrationCollector**: Updated to parse and store factory expressions for analysis.

## [1.2.0] - 2025-11-25

### Added

- **DI010**: New analyzer detecting constructor over-injection (5+ dependencies suggests class may violate SRP)
- **DI011**: New analyzer detecting `IServiceProvider`, `IServiceScopeFactory`, or `IKeyedServiceProvider` injection
  - Excludes factory classes (name ends with "Factory") and middleware classes (has Invoke/InvokeAsync method)
- **.NET 8 Keyed Services Support**: All analyzers now support keyed service patterns
  - `AddKeyedSingleton`, `AddKeyedScoped`, `AddKeyedTransient` registrations
  - `GetKeyedService`, `GetRequiredKeyedService`, `GetKeyedServices` service resolution
  - `IKeyedServiceProvider` detection in DI006, DI007, DI011

### Changed

- Enhanced `WellKnownTypes` with `IKeyedServiceProvider` support
- Updated `RegistrationCollector` to track keyed service registrations
- Updated `DI006_StaticProviderCacheAnalyzer` to detect `IKeyedServiceProvider` in static fields
- Updated `DI007_ServiceLocatorAntiPatternAnalyzer` to detect keyed service resolution methods
- Updated `DI008_DisposableTransientAnalyzer` to detect `AddKeyedTransient` registrations

---

## [1.1.0] - 2025-11-25

### Added

- **DI012**: New analyzer detecting conditional registration misuse
  - **DI012**: `TryAdd*` called after `Add*` for the same service type (will be silently ignored)
  - **DI012b**: Multiple `Add*` calls for the same service type (later registration overrides earlier)
- **DI002 Code Fix**: Added pragma suppression and TODO comment code fixes for scope escape diagnostics
- Extended `RegistrationCollector` infrastructure to track registration order for DI012 analysis

### Changed

- Updated README with DI012 documentation and corrected DI002 code fix availability

---

## [1.0.0] - 2025-11-24

### Added

- **DI004**: Support for modern `using var` declarations (previously only `using` statements were detected)
- Additional edge case test coverage for DI001, DI004, and DI007 analyzers
- Analyzer release tracking files for Roslyn best practices
- CONTRIBUTING.md with contribution guidelines
- Known Limitations section in README

### Fixed

- Build warnings RS2008 and RS1037 resolved
- DI004 now properly detects services used after `using var` scope ends in nested blocks

### Changed

- Version bumped to 1.0.0 for stable release

---

## [0.1.0] - 2024-11-24

### Added

#### Analyzers

- **DI001**: Detect undisposed `IServiceScope` instances
- **DI002**: Detect scoped services escaping their scope lifetime
- **DI003**: Detect captive dependencies (singleton capturing scoped/transient)
- **DI004**: Detect service usage after scope disposal
- **DI005**: Detect `CreateScope()` usage in async methods (should use `CreateAsyncScope()`)
- **DI006**: Detect `IServiceProvider` or `IServiceScopeFactory` cached in static members
- **DI007**: Detect service locator anti-pattern
- **DI008**: Detect transient services implementing `IDisposable`/`IAsyncDisposable`
- **DI009**: Detect open generic singletons capturing scoped/transient dependencies

#### Code Fixes

- **DI001**: Add `using` or `await using` statement
- **DI003**: Change service lifetime to `Scoped` or `Transient`
- **DI005**: Replace `CreateScope()` with `CreateAsyncScope()`
- **DI006**: Remove `static` modifier from field/property
- **DI008**: Change lifetime to `Scoped` or `Singleton`
- **DI009**: Change open generic lifetime to `Scoped` or `Transient`
