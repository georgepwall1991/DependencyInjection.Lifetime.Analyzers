param(
    [string]$PackagesDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packagesPath = Join-Path $repoRoot $PackagesDirectory
$analyzerProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.csproj'
$showcaseProject = Join-Path $repoRoot 'samples/DependencyInjection.Showcase/DependencyInjection.Showcase.csproj'

if (-not (Test-Path -LiteralPath $packagesPath)) {
    throw "The package directory was not found at $packagesPath. Run scripts/Validate-Package.ps1 first."
}

$version = (& dotnet msbuild $analyzerProject -getProperty:Version -nologo).Trim()
$packagePath = Join-Path $packagesPath "DependencyInjection.Lifetime.Analyzers.Scan.$version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "The scanner package was not found at $packagePath. Run scripts/Validate-Package.ps1 first."
}

$toolPath = Join-Path ([IO.Path]::GetTempPath()) "di-scan-tool-$([Guid]::NewGuid().ToString('N'))"
$sarifPath = Join-Path ([IO.Path]::GetTempPath()) "di-scan-consumption-$([Guid]::NewGuid().ToString('N')).sarif"

try {
    Write-Host "Installing the packed scanner into $toolPath..."
    & dotnet tool install `
        --add-source $packagesPath `
        --tool-path $toolPath `
        DependencyInjection.Lifetime.Analyzers.Scan `
        --version $version
    if ($LASTEXITCODE -ne 0) {
        throw 'The packed scanner tool failed to install.'
    }

    $scannerName = if ($env:OS -eq 'Windows_NT') { 'di-scan.exe' } else { 'di-scan' }
    $scanner = Join-Path $toolPath $scannerName
    if (-not (Test-Path -LiteralPath $scanner)) {
        throw "The installed scanner command was not found at $scanner."
    }

    $installedVersion = (& $scanner --version).Trim()
    if ($installedVersion -ne "di-scan $version") {
        throw "Expected 'di-scan $version', got '$installedVersion'."
    }

    Write-Host 'Scanning the showcase with the installed tool...'
    & $scanner $showcaseProject `
        --rules DI003,DI006,DI009,DI012b,DI016,DI019 `
        --findings all `
        --no-github `
        --sarif $sarifPath
    if ($LASTEXITCODE -ne 0) {
        throw "The installed scanner exited $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $sarifPath)) {
        throw "The installed scanner did not write $sarifPath."
    }

    $sarif = Get-Content -LiteralPath $sarifPath -Raw | ConvertFrom-Json
    $reportedRules = @(
        foreach ($run in @($sarif.runs)) {
            foreach ($result in @($run.results)) {
                [string]$result.ruleId
            }
        }
    ) | Sort-Object -Unique

    $expectedRules = @('DI003', 'DI006', 'DI009', 'DI012b', 'DI016', 'DI019')
    $missingRules = @($expectedRules | Where-Object { $reportedRules -notcontains $_ })
    if ($missingRules.Count -gt 0) {
        throw "The installed scanner did not report: $($missingRules -join ', '). Reported: $($reportedRules -join ', ')."
    }

    Write-Host "Scanner consumption validation passed: $version reported $($expectedRules -join ', ')."
}
finally {
    if (Test-Path -LiteralPath $toolPath) {
        Remove-Item -LiteralPath $toolPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $sarifPath) {
        Remove-Item -LiteralPath $sarifPath -Force -ErrorAction SilentlyContinue
    }
}
