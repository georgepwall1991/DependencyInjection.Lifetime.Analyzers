; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI027 | DependencyInjection | Warning | Shorter-lived service subscribes to an observable on a longer-lived publisher and discards the IDisposable subscription token
