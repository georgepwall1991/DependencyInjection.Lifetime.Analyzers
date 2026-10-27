param(
    [string]$ConfigFile = 'stryker-config.json'
)

$ErrorActionPreference = 'Stop'

# The mutation break threshold is deliberately not a parameter. The repository's gate is 80%,
# and lowering it to make a change pass is not allowed.
$Break = 80

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$manifestPath = Join-Path $repoRoot '.config/dotnet-tools.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    dotnet new tool-manifest --force | Out-Null
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest -notmatch 'dotnet-stryker') {
    dotnet tool install dotnet-stryker | Out-Null
}

dotnet tool restore | Out-Null
dotnet stryker --config-file $ConfigFile --break-at $Break
exit $LASTEXITCODE
