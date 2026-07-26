; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI032 | DependencyInjection | Warning | Container-created service implements only IAsyncDisposable, so synchronous provider or scope disposal throws
DI033 | DependencyInjection | Info | Disposable instance registered as a pre-built singleton, which the container never disposes
