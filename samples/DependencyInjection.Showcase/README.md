# DependencyInjection.Showcase

A compact set of bad and safe dependency-injection shapes. The full sample app under
[`samples/SampleApp`](../SampleApp) has examples for every shipped rule; this project is the short
tour for the six highest-value failure modes.

| Mission ID | Shipped rule | Failure it prevents | Safe shape |
| --- | --- | --- | --- |
| DIA001 | `DI003` | Singleton captures a scoped or transient service | Make the consumer scoped, or inject `IServiceScopeFactory` and create a scope per operation |
| DIA002 | `DI016` | `BuildServiceProvider()` during registration snapshots a half-built container | Build the provider once at the composition root, after all registrations |
| DIA003 | `DI019` | Scoped service resolved from the root provider | Resolve scoped services from a created scope |
| DIA004 | `DI012b` | The same service is registered with conflicting lifetimes; the last registration wins | Register the service once, or use `TryAdd`/`Replace` to express intent |
| DIA005 | `DI009` | Open generic singleton captures a scoped dependency | Make the open generic scoped/transient, or resolve the dependency from a scope |
| DIA006 | `DI006` | `IServiceProvider` stored in a static member | Inject the provider per use, or store `IServiceScopeFactory` when scope creation is the responsibility |

Build the project to see the warning diagnostics:

```bash
dotnet build samples/DependencyInjection.Showcase/DependencyInjection.Showcase.csproj -c Release
```

`DI012b` is an `Info` diagnostic, so MSBuild does not print it by default. Run the scanner to see
every rule, including Info:

```bash
dnx DependencyInjection.Lifetime.Analyzers.Scan -- samples/DependencyInjection.Showcase/DependencyInjection.Showcase.csproj --rules DI012b
```

The analyzer reports at the registration or resolution site. The file comments name the rule and the
failure; the rule pages under [`docs/rules`](../../docs/rules/README.md) explain the detection
boundary and the code fix, where one exists.

The showcase is intentionally not part of the sample-diagnostics contract. `SampleApp` owns that
contract because it exercises every rule and every safe pattern.
