; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI038 | DependencyInjection | Warning | Consumer disposes a container-owned service: a constructor-injected singleton/scoped dependency, or a resolved singleton
