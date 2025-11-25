### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI010 | DependencyInjection | Info | Constructor has too many dependencies
DI011 | DependencyInjection | Warning | Avoid injecting IServiceProvider/IServiceScopeFactory
DI012 | DependencyInjection | Info | TryAdd registration will be ignored because service already registered
DI012b | DependencyInjection | Info | Service registered multiple times; later registration overrides earlier
