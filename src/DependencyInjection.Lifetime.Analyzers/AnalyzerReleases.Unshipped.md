; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI027 | DependencyInjection | Warning | Shorter-lived service subscribes to an observable on a longer-lived publisher and discards the IDisposable subscription token
DI028 | DependencyInjection | Warning | Shorter-lived service registers a callback on a longer-lived registration source and discards the returned registration
DI029 | DependencyInjection | Warning | HttpClient or a pooling handler constructed per invocation, or an HttpClient registered as a singleton or held in a static member
DI030 | DependencyInjection | Info | Singleton-owned or static collection grows with request-derived keys and is never evicted, or an IMemoryCache entry has neither expiration nor size
