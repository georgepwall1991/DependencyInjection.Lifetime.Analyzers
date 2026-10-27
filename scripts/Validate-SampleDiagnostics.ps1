param(
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'tests/DependencyInjection.Lifetime.Analyzers.Tests/DependencyInjection.Lifetime.Analyzers.Tests.csproj'

if (-not (Test-Path -LiteralPath $testProject)) {
    throw "The test project was not found at $testProject."
}

$arguments = @(
    'test',
    $testProject,
    '--configuration', $Configuration,
    '--filter', 'FullyQualifiedName~SampleDiagnosticsVerifierTests',
    '--verbosity', 'minimal'
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Sample diagnostics validation failed.'
}

'sample diagnostics validation ok'
