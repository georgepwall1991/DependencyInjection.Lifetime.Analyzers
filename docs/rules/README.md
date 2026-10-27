# Rule reference

One page per shipped rule. Every page explains the production failure, the bad shape, the safe
shape, how the analyzer proves the finding, how to suppress it, and where to read more.

The rule pages are generated from the repository README and the analyzer descriptors by
`node tools/generate-rule-assets.mjs`. Change the README or the descriptor, then regenerate;
`--check` fails CI when a page is stale.

| ID | Title | Default severity | Code fix |
| --- | --- | --- | --- |
| [DI001](DI001.md) | Service Scope Not Disposed | Warning | Yes |
| [DI002](DI002.md) | Scoped Service Escapes Scope | Warning | Yes |
| [DI003](DI003.md) | Captive Dependency | Warning | Yes |
| [DI004](DI004.md) | Service Used After Scope Disposed | Warning | Yes |
| [DI005](DI005.md) | Use `CreateAsyncScope` in Async Methods | Warning | Yes |
| [DI006](DI006.md) | Static `IServiceProvider` Cache | Warning | Yes |
| [DI007](DI007.md) | Service Locator Anti-Pattern | Info | No |
| [DI008](DI008.md) | Disposable Transient Service | Warning | Yes |
| [DI009](DI009.md) | Open Generic Captive Dependency | Warning | Yes |
| [DI010](DI010.md) | Constructor Over-Injection | Info | No |
| [DI011](DI011.md) | `IServiceProvider` Injection | Info | No |
| [DI012](DI012.md) | Conditional Registration Misuse | Info | Yes |
| [DI012b](DI012b.md) | Duplicate service registration | Info | No |
| [DI013](DI013.md) | Implementation Type Mismatch | Error | Yes |
| [DI014](DI014.md) | Root Service Provider Not Disposed | Warning | Yes |
| [DI015](DI015.md) | Unresolvable Dependency | Warning | Yes |
| [DI016](DI016.md) | BuildServiceProvider Misuse | Warning | No |
| [DI017](DI017.md) | Circular Dependency | Warning | No |
| [DI018](DI018.md) | Non-Instantiable Implementation Type | Warning | No |
| [DI019](DI019.md) | Scoped Service Resolved From Root Provider | Warning | Yes |
| [DI020](DI020.md) | Middleware Captures Scoped Service In Constructor | Warning | No |
| [DI021](DI021.md) | Non-Thread-Safe Service Shared Across Concurrent Handler Invocations | Warning | Yes |
| [DI022](DI022.md) | Service Instance Reused Across Handler Invocations | Info | No |
| [DI023](DI023.md) | Fire-and-Forget Background Work Captures a Scope | Warning | No |
| [DI024](DI024.md) | Hosted Service Creates Scope Outside Execution Loop | Warning | No |
| [DI025](DI025.md) | Event Subscription On Longer-Lived Publisher Without Unsubscribe | Warning | Yes |
| [DI026](DI026.md) | Event Subscription On Scoped Publisher Without Unsubscribe | Info | No |
| [DI027](DI027.md) | Rx Subscription On Longer-Lived Observable Without Dispose | Warning | No |
| [DI028](DI028.md) | Discarded Callback Registration On A Longer-Lived Source | Warning | No |
| [DI029](DI029.md) | HttpClient Lifetime Misuse | Warning | No |
| [DI030](DI030.md) | Unbounded Singleton Or Static Cache | Info | No |
| [DI031](DI031.md) | Shared Implementation Registered Under Several Service Types | Info | No |
| [DI032](DI032.md) | Service Implements Only IAsyncDisposable | Warning | No |
| [DI033](DI033.md) | Container Will Not Dispose a Pre-Built Instance | Info | No |
| [DI034](DI034.md) | HttpContext Used in Fire-and-Forget Background Work | Warning | No |
| [DI035](DI035.md) | Non-Thread-Safe Service Shared Across a Fan-Out | Warning | No |
| [DI036](DI036.md) | Registration Added After The Provider Was Built | Warning | No |
| [DI037](DI037.md) | Un-awaited Task Escapes The Scope That Created It | Warning | No |

## Profiles

The same generator writes the starter `.editorconfig` profiles under `profiles/`:

| Profile | Use it when |
| --- | --- |
| `default.editorconfig` | You want each rule at its shipped default severity. |
| `brownfield-adoption.editorconfig` | You are adding the package to a large existing codebase and want composition failures first. |
| `strict-ci.editorconfig` | Every high-confidence rule should fail CI; informational rules stay warnings. |
| `library-author.editorconfig` | You ship a library and want activation and composition failures to be errors. |

## Priority failure modes

The shipped IDs are the stable contract. The short names below are the six highest-value failure
modes used by the scanner corpus and the showcase; they map onto one or more shipped rules.

| Priority | Production failure | Shipped rule |
| --- | --- | --- |
| DIA001 | A singleton captures a scoped or transient service | `DI003`; `DI009` for open generics |
| DIA002 | `BuildServiceProvider()` outside the composition root | `DI016` |
| DIA003 | A scoped service is resolved from the root provider | `DI019` |
| DIA004 | The same service is registered with conflicting lifetimes | `DI012b` (duplicate registration; the lifetime consequence is `DI003`/`DI031`) |
| DIA005 | An open generic registration has an unsafe lifetime for its dependencies | `DI009` |
| DIA006 | `IServiceProvider` is stored in a singleton or static field | `DI006` for static members; `DI011` for injected providers |
