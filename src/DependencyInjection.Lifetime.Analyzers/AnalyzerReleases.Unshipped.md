; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI025 | DependencyInjection | Warning | Shorter-lived service subscribes to an event on a longer-lived publisher (singleton dependency or static event) without a matching unsubscription
DI026 | DependencyInjection | Info | Transient service subscribes to an event on a scoped publisher without a matching unsubscription (scope-bounded tier of DI025)
