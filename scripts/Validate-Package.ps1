param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputPath = Join-Path $repoRoot $OutputDirectory
$analyzerProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.csproj'
$packageProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers.Package/DependencyInjection.Lifetime.Analyzers.Package.csproj'
$scannerProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers.Scan/DependencyInjection.Lifetime.Analyzers.Scan.csproj'

if (-not (Test-Path -LiteralPath $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

$version = (& dotnet msbuild $analyzerProject -getProperty:Version -nologo).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read the analyzer version.'
}

Write-Host "Building and packing version $version..."
& dotnet build $analyzerProject --configuration $Configuration --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'The analyzer build failed.' }

& dotnet pack $packageProject --configuration $Configuration --output $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'The analyzer package failed to pack.' }

& dotnet pack $scannerProject --configuration $Configuration --output $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'The scanner package failed to pack.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageEntries([string]$path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PackageContains([string[]]$entries, [string]$entry, [string]$packageName) {
    if ($entries -notcontains $entry) {
        throw "$packageName is missing '$entry'."
    }
}

$analyzerPackage = Join-Path $outputPath "DependencyInjection.Lifetime.Analyzers.$version.nupkg"
$scannerPackage = Join-Path $outputPath "DependencyInjection.Lifetime.Analyzers.Scan.$version.nupkg"

if (-not (Test-Path -LiteralPath $analyzerPackage)) {
    throw "The analyzer package was not produced at $analyzerPackage."
}
if (-not (Test-Path -LiteralPath $scannerPackage)) {
    throw "The scanner package was not produced at $scannerPackage."
}

$analyzerEntries = Get-PackageEntries $analyzerPackage
foreach ($entry in @(
    'analyzers/dotnet/cs/DependencyInjection.Lifetime.Analyzers.dll',
    'contentFiles/any/any/profiles/default.editorconfig',
    'contentFiles/any/any/profiles/brownfield-adoption.editorconfig',
    'contentFiles/any/any/profiles/strict-ci.editorconfig',
    'contentFiles/any/any/profiles/library-author.editorconfig',
    'docs/rules/DI003.md',
    'docs/rules/README.md',
    'PACKAGE_README.md',
    'icon.png'
)) {
    Assert-PackageContains $analyzerEntries $entry 'The analyzer package'
}

$scannerEntries = Get-PackageEntries $scannerPackage
foreach ($entry in @(
    'tools/net8.0/any/DependencyInjection.Lifetime.Analyzers.Scan.dll',
    'tools/net8.0/any/analyzer/DependencyInjection.Lifetime.Analyzers.dll',
    'tools/net8.0/any/DotnetToolSettings.xml',
    'README.md',
    'icon.png'
)) {
    Assert-PackageContains $scannerEntries $entry 'The scanner package'
}

$settings = [System.IO.Compression.ZipFile]::OpenRead($scannerPackage)
try {
    $entry = $settings.GetEntry('tools/net8.0/any/DotnetToolSettings.xml')
    $reader = [IO.StreamReader]::new($entry.Open())
    try {
        $xml = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $settings.Dispose()
}

if ($xml -notmatch '<Command Name="di-scan"') {
    throw 'The scanner package does not expose the di-scan command.'
}

Write-Host "Package validation passed: analyzer $version, scanner $version."
