# How we found 471 DI lifetime findings across 14 public repositories

Most analyzer packages are validated on toy snippets. That is not enough for dependency injection:
the same registration can be correct in one host and a captive dependency in another, and a
lifetime bug often hides behind a factory, an open generic, or a custom scope wrapper.

We built a pinned corpus and a scanner so every rule change has to survive real code.

## The corpus

Fourteen public repositories are checked out at exact commit SHAs:

- ASP.NET Core apps such as eShopOnWeb, CleanArchitecture, and Ocelot
- framework libraries such as IdentityServer, OpenIddict, Polly, and MassTransit
- workers and service defaults from dotnet-podcasts and the Dapr SDK
- a clean DI library baseline (Scrutor) and an alternate-container bridge (Autofac)

The current baselines contain **471 findings**. That number is a regression signal, not a claim
that every finding is a production bug: informational rules such as DI007 are intentionally part of
the baseline, and partial composition roots are marked in the manifest.

## What the corpus changed

Real code exposed shapes that unit tests did not:

- MassTransit disposes scopes through the canonical
  `if (scope is IAsyncDisposable alias) await alias.DisposeAsync(); else scope.Dispose();` pattern.
  DI001 now recognizes it.
- MassTransit also stores a stateless `new EmptyServiceProvider()` in a static field. DI006 now skips
  custom null-object providers with no instance state while still reporting stateful providers.
- dotnet-podcasts registers `AddDbContext<PodcastDbContext>()`, but the context takes the non-generic
  `DbContextOptions` base type. DI015 now treats any registered `DbContextOptions<TContext>` as
  satisfying it.
- Elsa exposes a custom `ITenantScope.ServiceProvider` property. DI019 no longer treats an arbitrary
  wrapper property as the root provider.
- MassTransit returns a scope inside `new TenantScope(scope, …)`. DI001 now treats a scope wrapped in
  a directly returned object as caller-owned.

Each fix has positive and negative tests, and the corpus baselines moved only where the shape moved.

## Scan a repository yourself

```bash
dnx DependencyInjection.Lifetime.Analyzers.Scan -- MyApp.slnx
```

Or install the tool:

```bash
dotnet tool install -g DependencyInjection.Lifetime.Analyzers.Scan
di-scan MyApp.slnx --fail-on warning --sarif di.sarif
```

The scanner builds the target with the analyzers injected, without editing project files. It reports
`ruleId path:line:column`, the surrounding source, and links to the rule page. SARIF, Markdown, and
HTML are available for triage and CI.

## Baseline the existing debt

```bash
di-scan ./MyApp.slnx --update-baseline di.baseline
di-scan ./MyApp.slnx --fail-on warning --baseline di.baseline
```

Only new findings fail the second command. A SARIF baseline matches by fingerprint, so a finding that
moves down a file stays known.

## What we still consider unproven

Cross-project composition is the biggest remaining limitation. DI015 analyzes one compilation at a
time, so a library cannot see a registration that a downstream web project adds. The JT
CleanArchitecture corpus entry records those findings as baseline signal, not as proof that the app
is missing the registration.

We also keep DI003's transient captures as warnings even when a transient looks stateless: static
analysis cannot prove that a future edit will keep it stateless.

## The numbers

- 38 shipped rules
- 3,164 tests
- 14 pinned public repositories
- 471 baseline findings
- Mutation gates at 80% for the test-code suppressor (95.10%) and shared resolution helpers (94.92%)

The corpus, triage policy, and every baseline are in
[docs/scan-regression.md](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/blob/main/docs/scan-regression.md).
The scanner guide is at
[georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/scanner](https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/scanner/).
