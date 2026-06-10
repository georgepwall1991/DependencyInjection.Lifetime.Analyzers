# DI021/DI022 Final Spec — Deferred Captive Dependency in Concurrent Handlers

**Backbone decision:** The developer-experience-first design is the backbone (8/10 feasibility, cleanest analyzer↔fixer contract, single-phase v1, every cited reuse point verified against the codebase). Grafted in: the **ConcurrencySinkRegistry policy taxonomy** (AlwaysConcurrent / ConfigGated / ScopePerInvocation / NotASink) from framework-first, and the **three-leg proof + silence-on-unknown discipline, always-safe capture whitelist, and explicit NotASink entries** from precision-first. Every fatal flaw and load-bearing fixable issue raised by the six reviews is resolved inline below, each marked **[RESOLVED]**.

---

## 1. Rules

| ID | Name | Severity | Phase |
|----|------|----------|-------|
| **DI021** | `ConcurrentHandlerSharesNonThreadSafeService` — an instance of a documented non-thread-safe type, created/resolved once outside a handler, is used inside a delegate a framework provably invokes concurrently | **Warning**, enabled by default, operation-level (no CompilationEnd tag — live IDE squiggles, round-trippable fix tests) | v1 |
| **DI022** | `ConcurrentHandlerCapturesScopedOrConfigGatedService` — same capture shape but (a) the type is merely Scoped-registered (lifetime smell, not proven crash), or (b) the sink is config-gated and the knob is unprovable (ServiceBusProcessor with `MaxConcurrentCalls` from config — capture-across-messages is wrong even sequentially) | **Info**, enabled by default, CompilationEnd (needs RegistrationCollector) | v1 for the config-gated tier, v2 for the scoped-lifetime tier |
| **DI023** | `SharedInstanceAcrossFannedOutTasks` — Task.Run/WhenAll fan-out heuristic | Warning, independently demotable | v3, parked |

Two separate IDs, **not** the `effectiveSeverity` overload. **[RESOLVED — framework-first feasibility flaw: editorconfig severity is per rule ID; a single-descriptor Info tier would be unsuppressible independently.]** Teams tune `dotnet_diagnostic.DI021.severity` and `DI022.severity` separately; strict teams bump DI022 to warning with one line.

**Message formats** (post-mortem style, the DX differentiator — keep verbatim spirit):

- DI021: `"'{0}' is shared across concurrent invocations of {1}; concurrent use of a {2} fails at runtime (e.g. 'A second operation was started on this context'). Resolve it from a new scope inside the handler, or inject IDbContextFactory<TContext>."` — `{1}` carries the human concurrency description from the sink row, e.g. `"ServiceBusSessionProcessor.ProcessMessageAsync (up to 8 concurrent sessions by default)"`. EventProcessorClient wording is hedged: *"partitions are processed concurrently"*, not an unconditional crash claim. **[RESOLVED — single-partition-hub FP wording.]**
- DI022 (config-gated tier): conditional wording — `"'{0}' is captured once and reused across all invocations of {1}. If {2} is raised above 1 this becomes a concurrency crash; even sequentially, one instance accumulates state across all messages. Resolve per invocation instead."` **[RESOLVED — precision-first fatal flaw #1: the headline config-bound ServiceBusProcessor scenario is no longer silent; it reports at Info with factually correct wording. Also resolves the framework-first 'Info tier must not assert an active crash' issue.]**

---

## 2. Detection algorithm — concrete Roslyn mechanics

One analyzer class, two descriptors (DI012 multi-descriptor precedent). House-style registration shape — **compilation-start + operation actions + ConcurrentDictionary state**, NOT `RegisterSymbolStartAction`. **[RESOLVED — DX feasibility issue: the claimed "DI014 symbol-action precedent" is false; SymbolStart/End is untested in this repo and brings RS1013-class friction. We use the proven DI001/DI014/DI016 shape instead.]**

### Step 1 — CompilationStart setup
1. Build `WellKnownTypes` (existing `Create()` pattern), extended with: `BackgroundService`, `IHostedService`, the sink types below, `ParallelOptions`, `IDbContextFactory<T>`.
2. Build `ConcurrencySinkRegistry` and `NonThreadSafeTypeCatalog`. Type resolution uses **`Compilation.GetTypesByMetadataName` (plural)** with containing-assembly disambiguation; null/empty ⇒ that sink row is disabled. **[RESOLVED — both feasibility reviews: singular `GetTypeByMetadataName` returns null on cross-assembly ambiguity and silently disables sinks; repo is on Roslyn 5.0.0 so the plural form is available.]**
3. **Early whole-compilation bail**: if no sink type resolves, register nothing further. Near-zero cost for non-messaging codebases.
4. Wire the existing `RegistrationCollector` operation actions only if DI022's scoped tier is active (gated; skip entirely when no sink resolved).

### Step 2 — Sink detection (three operation channels)
- **`OperationKind.EventAssignment`**: `IEventAssignmentOperation` with `Adds == true`; match `EventReference.Event.Name` + `ContainingType` against the registry (`ProcessMessageAsync`, `ProcessEventAsync`, `Elapsed`). Receiver chains through conditional access (`_holder?.Processor` feeding a plain member-access event add) handled with the DI002 unwrapping helpers — note the **assignment target itself** cannot be conditional-access below C# 14. **[RESOLVED — framework-first feasibility: `processor?.Event += h` is illegal C# pre-14. The v1 test matrix uses receiver-chain conditional access only; one optional C#14 test pins `LanguageVersion` ≥ 14 explicitly.]**
- **`OperationKind.Invocation`**: `IInvocationOperation` where `TargetMethod.ContainingType` is `System.Threading.Tasks.Parallel` (name pre-filter via static HashSet before symbol work). Analyze **ALL delegate-typed arguments**, not "the last one". **[RESOLVED — `Parallel.For<TLocal>` overloads end with `localFinally`; walking every delegate argument is both simpler and correct.]**
- **`OperationKind.ObjectCreation`**: `IObjectCreationOperation` of `System.Threading.Timer`. Sink only when a **finite period is affirmatively present**: period argument matched as (a) int/long constant ≠ -1 via `ConstantValue`, (b) NOT an `IFieldReferenceOperation` of `Timeout.Infinite` / `Timeout.InfiniteTimeSpan`, (c) NOT `TimeSpan.FromMilliseconds(-1)`. The 1-arg `new Timer(cb)` ctor is a sink only if a finite `Change(...)` is found in the same type. **[RESOLVED — both feasibility reviews + FP review: `Timeout.InfiniteTimeSpan` is a static readonly field with no `ConstantValue`; field-symbol matching is mandatory or every one-shot TimeSpan timer false-positives. Never-started timers are not sinks.]**

### Step 3 — Handler delegate extraction
`HandlerValue` → `IDelegateCreationOperation.Target`:
- `IAnonymousFunctionOperation` → lambda path (Step 4).
- `IMethodReferenceOperation` → method-group path: closures are impossible, so check **instance fields and statics only**. **[RESOLVED — precision-first's incoherent "method group capturing a local" claim: method groups go straight to the field/static check.]** Body obtained via `DeclaringSyntaxReferences` + `ExecutableSyntaxHelper.TryGetExecutableBody`; semantic models via the repo's cached `GetSemanticModel` GetOrAdd pattern (DI003 lines 525-531). Same-named-type declarations only; cross-type/cross-tree ⇒ silent bail (explicitly RS1030-driven — do not "fix" this FN later by calling `GetSemanticModel` on foreign trees ad hoc).

### Step 4 — Capture classification (`HandlerCaptureWalker`, NEW ~250 LOC)
**No `AnalyzeDataFlow`.** Pure operation-tree walk of the handler body collecting `ILocalReferenceOperation` / `IParameterReferenceOperation` / `IFieldReferenceOperation` / `IPropertyReferenceOperation`. **[RESOLVED — precision-first feasibility: `AnalyzeDataFlow(...).Captured` semantics are wrong for this use and the API is novel to the codebase; the operation walk also yields the exact use locations the fixer needs, which DataFlow cannot.]** This walker is **greenfield code** — DI004's `TrackDelegateCapture`/`TrackServiceAlias` are private, syntax-based methods inside a sealed hardened analyzer; they are templates, not imports. The effort estimate (§9) budgets it as new code. **[RESOLVED — both feasibility reviews' "reuse accounting is inflated" finding: stated plainly, budgeted plainly.]**

A reference is a **shared capture** when:
- (a) field/property reference through `this` on the containing type — **this covers the canonical "DbContext field on a BackgroundService used in a Timer/lambda sink" shape in v1**; **[RESOLVED — precision-first fatal flaw #3: no DI021/DI022 seam; instance fields are first-class DI021 captures from day one]**
- (b) local whose declaring executable boundary (via `ExecutableSyntaxHelper.IsExecutableBoundary`, anchored at the **handler lambda**, not the enclosing method — nested/intermediate lambdas classified correctly) is outside the handler; **[RESOLVED — DX feasibility boundary-anchoring issue]**
- (c) parameter of an enclosing method (the handler's own parameters — `ProcessMessageEventArgs` etc. — are per-invocation, skipped).

Walks never descend into nested lambdas (each gets its own sink evaluation).

Each shared symbol's type goes through `NonThreadSafeTypeCatalog.Match` (base-type walk so derived DbContexts hit) — but **first** through the always-safe whitelist (§5.3), which short-circuits before any further analysis.

**One-hop delegation follow-through**: when the lambda body is a single invocation of a same-type instance method (`+= args => HandleAsync(args)`), apply the method-group field/static check to that target method. **[RESOLVED — both FP reviews' "thin-lambda delegation defeats the marquee detection" finding; real handlers never keep logic in the lambda. One hop, same type, source body required; anything else bails.]**

### Step 5 — Captured-scope resolution channel
Inside the handler body, any `GetService`/`GetRequiredService`/`GetKeyedService` invocation (DI004 `GetResolvedServiceType` pattern) whose **receiver chains to a captured `IServiceScope`/`AsyncServiceScope`/`IServiceProvider`** (existing `WellKnownTypes.IsServiceScope` etc. + DI001/DI004 alias machinery) is treated as a shared capture of the **resolved** type: DI021 if the resolved type is in the catalog, DI022 (v2) if merely Scoped per `RegistrationCollector`. The in-body-resolution safe escape applies **only** when the provider chains to a scope **created inside the handler** (DI001 CreateScope/CreateAsyncScope matching). **[RESOLVED — precision-first FP lens: closes the hole where `_longLivedScope.ServiceProvider.GetRequiredService<AppDb>()` per message returns the same instance to every concurrent invocation, AND closes the silencing loophole where a user "fixes" the warning by moving resolution inside the lambda against the same captured scope.]** This also subsumes framework-first's DI022 (shared scope across Parallel body) without a separate rule.

### Step 6 — Concurrency gates (per sink row)
- `ServiceBusProcessor`: trace the event receiver to `client.CreateProcessor(..., options)` and prove `MaxConcurrentCalls` is a compile-time constant > 1. Proof boundary = same executable boundary **+ containing-type field initializers + containing-type constructor bodies** (options built in ctor, subscribed in `StartAsync` is the dominant real shape). **[RESOLVED — framework-first feasibility: same-method-only proof misses the dominant shape.]** Proven >1 ⇒ DI021 Warning. Unprovable/absent ⇒ **DI022 Info**, never silence.
- `ServiceBusSessionProcessor`: AlwaysConcurrent, **with the symmetric escape**: provable `MaxConcurrentSessions == 1` (and `MaxConcurrentCallsPerSession` unset or 1) ⇒ suppressed. **[RESOLVED — strict-ordering single-session config is real.]**
- `Parallel.*`: provable `ParallelOptions.MaxDegreeOfParallelism == 1` constant ⇒ suppressed.
- `System.Timers.Timer`: provable `AutoReset = false` initializer/assignment, or `SynchronizingObject` assigned non-null ⇒ suppressed. **[RESOLVED — one-shot/marshalled timer FPs.]**

### Step 7 — Serialization-guard suppression (v1, not deferred)
Skip a candidate when every use of the symbol inside the handler is covered by one of:
1. a lexical `lock` statement;
2. a **`SemaphoreSlim` bracket**: `_gate.Wait()`/`await _gate.WaitAsync()` preceding the uses, with `_gate.Release()` in the `finally` of an enclosing `try` — Wait and Release in *different* blocks is the pattern, matched structurally (Wait dominates handler entry or the try, Release in the corresponding finally);
3. a **reentrancy guard dominating handler entry**: `if (Interlocked.CompareExchange(ref _flag, 1, 0) != 0) return;` (or `Monitor.TryEnter` early-return), or timer re-arm (`_timer.Change(Timeout.Infinite, Timeout.Infinite)` as first statement).

**[RESOLVED — the #1 disable-driver flagged by ALL three FP reviews: `lock` is illegal across `await`, so SemaphoreSlim try/finally and Interlocked guards are the ONLY correct serialization idioms in async handlers, and they are ubiquitous in disciplined codebases. These are cheap lexical/structural checks, in v1. Nito.AsyncEx-style `using (await _mutex.LockAsync())` disposable locks: any `using` whose resource expression awaits a member of a type whose name contains "Lock"/"Mutex" also suppresses — crude, documented, errs toward silence.]**

### Step 8 — Reporting
Once per (handler, symbol) at the **first use** inside the handler. `additionalLocations[0]` = capture/creation site, `[1]` = sink registration, `[2..]` = remaining uses. `Diagnostic.Properties`: `ServiceTypeDocId`, `SymbolName`, `SinkKind`, `HandlerIsAsync`, `CaptureKind`. The fixer never re-derives analysis. DI021 reports at the operation action; DI022 candidates queue into a per-compilation `ConcurrentBag` and report at CompilationEnd after lifetime lookup. **[RESOLVED — precision-first's DI022 cross-action ordering flaw: all registration-dependent reports defer to CompilationEnd, the DI012/DI015 precedent.]**

---

## 3. Sink table v1 (exact APIs)

| # | Fully-qualified sink | Shape | Policy | Gate/escape |
|---|---------------------|-------|--------|-------------|
| 1 | `Azure.Messaging.ServiceBus.ServiceBusSessionProcessor.ProcessMessageAsync` (+ `ProcessErrorAsync`) | event `+=` | AlwaysConcurrent — "up to 8 concurrent sessions by default" | constant `MaxConcurrentSessions == 1` suppresses |
| 2 | `Azure.Messaging.ServiceBus.ServiceBusProcessor.ProcessMessageAsync` (+ `ProcessErrorAsync`) | event `+=` | ConfigGated(`ServiceBusProcessorOptions.MaxConcurrentCalls`, default 1) | proven >1 ⇒ DI021; else DI022 Info |
| 3 | `Azure.Messaging.EventHubs.EventProcessorClient.ProcessEventAsync` (+ `ProcessErrorAsync`) | event `+=` | AlwaysConcurrent (per-partition pumps; hedged message) | — |
| 4 | `System.Threading.Timer..ctor` (all overloads) / `.Change` | ctor delegate | AlwaysConcurrent when finite period proven present | `Timeout.Infinite`/`Timeout.InfiniteTimeSpan`/`FromMilliseconds(-1)`/never-started suppress; re-arm guard suppresses |
| 5 | `System.Timers.Timer.Elapsed` | event `+=` | AlwaysConcurrent | `AutoReset = false` or `SynchronizingObject` set suppresses |
| 6 | `System.Threading.Tasks.Parallel.For` / `.ForEach` / `.ForEachAsync` / `.Invoke` (all overloads, all delegate args) | delegate args | AlwaysConcurrent | constant `MaxDegreeOfParallelism == 1` suppresses |

**Explicit `NotASink` registry rows** (precision-first's best idea — encoded so future contributors can't "helpfully" add noise): `System.Threading.PeriodicTimer.WaitForNextTickAsync`; `Confluent.Kafka.IConsumer<,>.Consume`; a lone awaited `Task.Run`; and **all scope-per-invocation frameworks**: `MassTransit.IConsumer<T>.Consume`, `NServiceBus.IHandleMessages<T>.Handle`, `Rebus.Handlers.IHandleMessages<T>.Handle`, `Quartz.IJob.Execute`, Hangfire job expressions, SignalR hub methods, Azure Functions.

v2 adds: RabbitMQ `Received`/`ReceivedAsync` + `HandleBasicDeliverAsync` consumer fields (ConfigGated on `ConsumerDispatchConcurrency`, with **consistent** tiering — field captures get DI022 Info wording at default config, DI021 only when the knob is proven >1; **[RESOLVED — framework-first's RabbitMQ severity inconsistency]**), PLINQ operator lambdas (`WithDegreeOfParallelism(1)` suppresses — **[RESOLVED]**), TPL Dataflow blocks (fully silent at default config), EventHubs batch overrides.

---

## 4. Non-thread-safe type list v1

Matched by base-type/interface walk, `SymbolEqualityComparer.Default`, null-tolerant per missing assembly:

1. `Microsoft.EntityFrameworkCore.DbContext` and any derived type
2. `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction`
3. `System.Data.Common.DbConnection` / `System.Data.IDbConnection`
4. `System.Data.Common.DbCommand` / `System.Data.IDbCommand`
5. `System.Data.Common.DbTransaction` / `System.Data.IDbTransaction`
6. `System.Data.Common.DbDataReader` / `System.Data.IDataReader`
7. `Microsoft.AspNetCore.Http.HttpContext` (the value; the accessor is safe)

**Always-safe capture whitelist** (short-circuits before flow analysis; pinned by tests): `IDbContextFactory<T>`, `IServiceScopeFactory`, `ILogger`/`ILogger<T>`, `IOptions<T>`/`IOptionsMonitor<T>`, `IHttpContextAccessor`. `IServiceProvider`/`IServiceScope` captures are not flagged *as captures* but feed the Step-5 resolution channel.

v2 rounds out: `Confluent.Kafka.IConsumer<,>`, `NHibernate.ISession`, `System.Transactions.TransactionScope`, pre-.NET-6 `System.Random`, crypto transforms — each is one catalog row + tests.

---

## 5. False-positive defenses (enumerated)

1. **Allowlist-only sink registry**: scope-per-message frameworks structurally cannot FP — they are absent from the registry (and pinned absent by `NotASink` rows + negative tests).
2. **Three-leg proof, silence-on-unknown**: catalog type + shared-capture proof + concurrent-sink proof; any unprovable leg ⇒ no diagnostic. Reassignment inside the handler before use, ref/out usage, multi-branch conflicting origins ⇒ bail.
3. **Always-safe whitelist** (§4) — recommended fix patterns can never re-warn.
4. **In-body origin exclusion**: values created inside the handler (`new MyContext(...)`, `factory.CreateDbContextAsync()`, resolution from an **in-handler-created** scope) are per-invocation ⇒ never flagged. The fixer's own output is verified diagnostic-clean.
5. **Captured-scope resolution is NOT a safe escape** (Step 5) — only in-handler-created scopes qualify.
6. **Config-gated sinks under-warn**: unprovable knob ⇒ Info with conditional wording, never a false Warning.
7. **Serialization-guard suppression in v1**: `lock`, SemaphoreSlim Wait/try/finally-Release, Interlocked/Monitor.TryEnter reentrancy guards, timer re-arm, disposable async-lock heuristic (Step 7).
8. **Timer one-shot/marshalled suppressions**: `Timeout.Infinite*` field-symbol matching, never-started timers, `AutoReset = false`, `SynchronizingObject`.
9. **Sequential-proof escapes**: `MaxDegreeOfParallelism == 1`, `MaxConcurrentSessions == 1`.
10. **Per-invocation symbols skipped**: handler parameters, in-handler locals.
11. **Dispose-only references don't count as uses** (DI004's territory; no double-report).
12. **Nested-lambda boundary discipline**: no descent, no double-reporting, correct boundary anchoring.
13. **Method-group conservatism**: same-type source bodies only; cross-tree ⇒ silent.
14. **No same-named user-type FPs**: sinks fire only when the registry type resolved from the real assembly (plural metadata lookup with assembly check).
15. **Docs**: README documents the test-project story (mock/in-memory DbContext under Parallel test harnesses ⇒ recommended `.editorconfig` severity override for test globs). **[RESOLVED — framework-first FP note.]**
16. **Cross-rule dedupe**: `CrossRuleInteractionTests` assert DI021 composes with DI003/DI019 at non-overlapping spans on the canonical BackgroundService repro (different locations, different fixes — deliberate, tested).

---

## 6. Code fix design

`DI021_ScopePerInvocationCodeFixProvider`, `FixableDiagnosticIds = [DI021, DI022]`. Title: **"Resolve '{T}' from a new scope per invocation"**. Driven entirely by the properties bag + additionalLocations.

1. Insert at handler-body top: `await using var scope = _scopeFactory.CreateAsyncScope();` for async handlers; **`using var scope = _scopeFactory.CreateScope();` for sync delegates** (Timer callbacks, `Parallel.For` Action bodies). **Expression-bodied lambdas are converted to block bodies first.** **[RESOLVED — precision-first feasibility: both the sync/async branch and the block-body conversion are explicit applicability steps, not discoveries for Codex pass 1.]**
2. `var db = scope.ServiceProvider.GetRequiredService<T>();` — name collision-checked via `SemanticModel.LookupSymbols`.
3. Rewrite all use locations to the new local.
4. `IServiceScopeFactory` plumbing: reuse an existing field/ctor param if present, else add field + ctor param + assignment (DI003 fixer machinery + `ConstructorSelection`).
5. **Dead-capture cleanup**: if the original field has zero remaining references in the type (single-document types only), remove field + ctor param + assignment.
6. Add `using Microsoft.Extensions.DependencyInjection;` if missing.

**Refusal conditions** (diagnostic stays, fix disappears — DI019 escape-discipline): any write to the symbol, ref/out argument, escape (returned/assigned elsewhere/passed to captured collection), method-group target also called from non-sink sites (degrade to insert-scope-only, field kept). `GetFixAllProvider() => null` in v1. Second action "Inject IDbContextFactory<TContext>" deferred to v2 (cross-document registration rewrite). **No fix for Timer→PeriodicTimer** (control-flow rewrite; recommended in the description instead).

**Escape valve**: the fixer is the predicted Codex sinkhole (DI019: ~12 passes). It is severable — if hardening drags, v1 ships analyzer-only and the fixer follows as v1.1 without touching the analyzer.

---

## 7. Deliberate cuts (and the v2/v3 roadmap)

**Cut from v1, with reasons:**
- **Task.Run/WhenAll fan-out (DI023)** — the noisiest heuristic class; ships v3 as its own demotable ID so tuning can't damage DI021's reputation. The FP reviewer's "same-method 2+ unawaited queries on one receiver + WhenAll" narrow sub-case is the first v2 candidate if dogfooding demands it.
- **RabbitMQ** — v6/v7 surface drift (`Received`→`ReceivedAsync`, `IModel`→`IChannel`, `CreateChannelOptions`) doubles matcher+stub surface; v2 with both forms encoded.
- **PLINQ, Dataflow, EventHubs batch processors** — v2 registry rows; trims v1 sink families per the feasibility reviewers' test-budget warning.
- **Scoped-lifetime DI022 tier** — v2; lifetime-only classification must be Info (stateless scoped services shared deliberately are legal) and needs RegistrationCollector wiring tested in anger. **[RESOLVED — framework-first's "scoped ≠ thread-unsafe" conflation: demoted to Info, deferred.]**
- **`.editorconfig` sink extensibility** (`di_analyzers.concurrent_sinks`) — v2; the grammar is a frozen public contract once shipped and deserves its own Codex pass on the parser (DI006 precedent makes it cheap then).
- **Cross-method knob proofs beyond ctor/field-initializer** (DI019 ProviderFacts-style cross-method chaining) — v2+; documented in ANALYZER_HEALTH.md as a known under-report.
- **Scope-per-message static-field checks** (MassTransit/Quartz/SignalR static `DbContext`), gRPC singleton-registration cross-check, SignalR fire-and-forget scope escape — v3.
- **`AnalyzeDataFlow`** — cut entirely, permanently (Step 4 rationale).
- **DI023-style mutable-collection rule** (StringBuilder/Dictionary mutation) — parked indefinitely; unproven noise profile.

**Roadmap**: v2 (2.10.x/2.11.0) = scoped-lifetime DI022 tier, RabbitMQ + PLINQ + Dataflow rows, IDbContextFactory code action, editorconfig sinks, FixAllProvider. v3 = DI023 fan-out, scope-per-message static fields, gRPC lifetime cross-check, extended type list.

---

## 8. Test plan (this repo's harness)

`AnalyzerVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer>` / `CodeFixVerifier<,>` with inline const-string stubs (DI019 `EfCoreStubs` pattern; no new package refs). New shared `tests/.../Infrastructure/TestStubs.cs`: ServiceBusStubs, EventHubsStubs, HostingStubs, EfCoreStubs extraction. BCL Timers/Parallel need no stubs. **Per-sink smoke tests assert the registry resolves against the stubs** (a namespace typo otherwise silently passes tests and never fires in production). Add a small **expected-diagnostic builder helper** for the additionalLocations-heavy assertions. **[RESOLVED — DX feasibility test-verbosity issue.]**

**~60 analyzer tests v1:**
- *Positives (~22)*: DbContext **field** on BackgroundService in SessionProcessor lambda (primary span on first use, additionalLocations verified); EventProcessorClient closure-captured local resolved from a long-lived scope; `System.Threading.Timer` finite period; `System.Timers.Timer.Elapsed`; `Parallel.ForEachAsync`/`ForEach`/`For<TLocal>` (delegate-arg coverage)/`Invoke`; ServiceBusProcessor with constant `MaxConcurrentCalls = 4` (options in initializer, in tracked local, **in ctor with subscribe in StartAsync**); method-group field use; **one-hop thin-lambda delegation**; **captured-scope in-body resolution** (Step 5, incl. the "user moved GetRequiredService inside the lambda" loophole); enclosing-method parameter capture; derived DbContext; SqlConnection-shaped DbConnection; HttpContext value; receiver-chain conditional access.
- *DI022 Info tier (~4)*: ServiceBusProcessor default/unprovable knob (config-bound value) ⇒ Info with conditional message; `SkipLocalDiagnosticCheck` for CompilationEnd.
- *Negatives (~26)*: in-handler scope creation + resolution; IDbContextFactory capture + CreateDbContextAsync; in-lambda `new MyContext()`; `MaxConcurrentCalls = 1`; `MaxConcurrentSessions = 1`; `MaxDegreeOfParallelism = 1`; **`Timeout.InfiniteTimeSpan` TimeSpan-overload one-shot**; never-started Timer; `AutoReset = false`; `SynchronizingObject` set; PeriodicTimer loop; **SemaphoreSlim Wait/try/finally-Release**; **Interlocked.CompareExchange guard**; **timer re-arm**; lexical lock; MassTransit-shaped consumer with ctor-injected DbContext (full fixture, zero diagnostics); lone awaited Task.Run; whitelist captures (ILogger/IServiceScopeFactory/IOptions); Dispose-only reference; reassignment-before-use; sink package absent but same-named user type present; cross-type method group bails; nested-lambda boundary; handler-parameter-only usage.
- *Cross-rule (~3)*: DI021+DI019/DI003 compose at distinct spans on the canonical repro.

**~14 code-fix tests**: async lambda full before/after incl. **field + ctor param + assignment removal**; sync Timer callback (`using` + `CreateScope`); existing factory reused; name collision; expression-bodied conversion; field kept when referenced elsewhere; refusals (write, ref, escape) verified as no-code-action; method-group degraded fix; DI022 path via SkipLocalDiagnosticCheck; missing using added; **fixed output re-verified diagnostic-clean**.

**~8 HandlerCaptureWalker unit tests** in isolation. Framework-version sweep: key cases against `ReferenceAssembliesWithLatestDi` (Timers/Parallel come from TFM ref assemblies). Optional C#14 `?.+=` test pins LanguageVersion. Full suite with `TMPDIR=$(pwd)/.tmp-test/` fallback; `codex review --uncommitted` clean before PR; standing cadence push → PR → merge → tag (one tag at a time).

---

## 9. Effort estimate (hardening-iteration units)

- **v1 = 1.5–2 iterations** (revised up from the backbone's 1, per both feasibility reviews): iteration 1 = registry + catalog + HandlerCaptureWalker (greenfield, ~250 LOC) + DI021 rule + guard suppressions + ~60-test wall + DI022 config-gated Info tier; iteration 1.5–2 = the code fixer and its Codex passes (budget 6–12 there; severable to v1.1). Ships as 2.10.0.
- **v2 = 1 iteration**: scoped tier, RabbitMQ/PLINQ/Dataflow rows, IDbContextFactory action, editorconfig sinks.
- **v3 = 0.5–1 iteration**: DI023 fan-out (dominated by structural-gate negatives), static-field framework checks.
- Family total: **3–4 iterations**.

---

## 10. Open questions for the maintainer

1. **Fixer in v1 or v1.1?** Spec says ship together but severable — pre-authorize the analyzer-only 2.10.0 if the fixer's Codex passes exceed ~8, or hold the release?
2. **DI022 enabled-by-default at Info?** It will fire on every config-bound ServiceBusProcessor capture — high real-world volume by design. Default-on (recommended; it's the headline scenario) or default-off with README opt-in?
3. **DI004 helper extraction vs. pattern duplication**: HandlerCaptureWalker needs alias/resolution logic shaped like DI004's private methods. Extract them to Infrastructure (touches DI004's hardened test wall) or duplicate ~100 LOC into the walker (maintenance cost)? Spec assumes duplication for v1 safety; confirm.
4. **Disposable async-lock heuristic** (name-contains "Lock"/"Mutex") — acceptable crude suppression, or drop it and accept Nito.AsyncEx FPs with a documented `#pragma` story?
5. **`ProcessErrorAsync` in scope?** Error handlers rarely touch DbContext but are the same event shape — included in the table for free; veto if noise appears.
6. **One-hop delegation depth**: fixed at exactly one same-type hop. Comfortable, or should v1 ship zero hops and add it in v1.1 behind tests if recall complaints arrive?
7. **ANALYZER_HEALTH.md framing**: document the known under-reports (cross-method knob wiring, cross-type method groups) as v2 targets now, so hardening-loop passes don't re-litigate them as bugs?
