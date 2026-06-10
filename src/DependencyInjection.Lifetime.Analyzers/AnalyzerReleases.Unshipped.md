; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI021 | DependencyInjection | Warning | Non-thread-safe service shared across concurrent handler invocations
DI022 | DependencyInjection | Info | Service instance reused across handler invocations of a concurrency-configurable sink
DI024 | DependencyInjection | Warning | Hosted service creates a scope or resolves a scoped service outside its long-running execution loop
