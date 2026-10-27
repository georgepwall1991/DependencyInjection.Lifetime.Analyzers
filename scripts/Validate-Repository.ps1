$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-RepositoryPath([string]$relativePath) {
    return Join-Path $repoRoot $relativePath
}

function Get-Text([string]$relativePath) {
    return Get-Content -LiteralPath (Get-RepositoryPath $relativePath) -Raw
}

function Assert-Contains([string]$relativePath, [string]$pattern, [string]$message) {
    $text = Get-Text $relativePath
    if ($text -notmatch $pattern) {
        throw $message
    }
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

Write-Host 'Checking generated rule docs and profiles...'
& node (Get-RepositoryPath 'tools/generate-rule-assets.mjs') --check
if ($LASTEXITCODE -ne 0) {
    throw 'Generated rule assets are stale. Run node tools/generate-rule-assets.mjs and review the diff.'
}

$diagnosticIdsText = Get-Text 'src/DependencyInjection.Lifetime.Analyzers/DiagnosticIds.cs'
$diagnosticIds = [regex]::Matches(
    $diagnosticIdsText,
    'public const string \w+ = "(DI\d{3}[a-z]?)"'
) |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

if ($diagnosticIds.Count -eq 0) {
    throw 'No diagnostic IDs were found in DiagnosticIds.cs.'
}

$catalogIds = [regex]::Matches(
    (Get-Text 'src/DependencyInjection.Lifetime.Analyzers.Scan/RuleCatalog.cs'),
    'Rule\(\s*"(DI\d{3}[a-z]?)"'
) |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

$missingFromCatalog = @($diagnosticIds | Where-Object { $catalogIds -notcontains $_ })
$extraInCatalog = @($catalogIds | Where-Object { $diagnosticIds -notcontains $_ })
if ($missingFromCatalog.Count -gt 0) {
    throw "RuleCatalog.cs is missing: $($missingFromCatalog -join ', ')."
}
if ($extraInCatalog.Count -gt 0) {
    throw "RuleCatalog.cs has unknown rules: $($extraInCatalog -join ', ')."
}

$profilePaths = @(
    'profiles/default.editorconfig',
    'profiles/brownfield-adoption.editorconfig',
    'profiles/strict-ci.editorconfig',
    'profiles/library-author.editorconfig'
)

$releaseNotes = (Get-Text 'src/DependencyInjection.Lifetime.Analyzers/AnalyzerReleases.Unshipped.md') +
    (Get-Text 'src/DependencyInjection.Lifetime.Analyzers/AnalyzerReleases.Shipped.md')
$ruleDocsIndex = Get-Text 'docs/rules/README.md'

foreach ($diagnosticId in $diagnosticIds) {
    $docPath = "docs/rules/$diagnosticId.md"
    Assert-True (Test-Path -LiteralPath (Get-RepositoryPath $docPath)) "Missing rule documentation page: $docPath."

    $ruleDoc = Get-Text $docPath
    foreach ($section in @(
        '## Why it matters',
        '## Bad',
        '## Better',
        '## Detection',
        '## Code fix',
        '## Suppression',
        '## References'
    )) {
        Assert-True ($ruleDoc.Contains($section, [StringComparison]::Ordinal)) "$docPath is missing '$section'."
    }

    Assert-Contains 'README.md' "\b$diagnosticId\b" "README.md does not mention $diagnosticId."
    Assert-True ($ruleDocsIndex -match "\]\($diagnosticId\.md\)") "docs/rules/README.md does not link $diagnosticId."

    if ($releaseNotes -notmatch "(?m)^$diagnosticId\s+\|") {
        throw "AnalyzerReleases.Unshipped.md/Shipped.md do not list $diagnosticId."
    }

    foreach ($profilePath in $profilePaths) {
        Assert-Contains $profilePath `
            "dotnet_diagnostic\.$diagnosticId\.severity\s*=" `
            "$profilePath does not configure $diagnosticId."
    }

    $testMatches = Get-ChildItem -Path (Get-RepositoryPath 'tests') -Recurse -Filter '*.cs' |
        Select-String -Pattern "\b$diagnosticId\b" -List
    if (-not $testMatches) {
        throw "No test file mentions $diagnosticId."
    }
}

Write-Host 'Checking package composition...'
$packageProject = Get-Text 'src/DependencyInjection.Lifetime.Analyzers.Package/DependencyInjection.Lifetime.Analyzers.Package.csproj'
foreach ($required in @(
    'profiles\*.editorconfig',
    'docs\rules\*.md',
    'DependencyInjection.Lifetime.Analyzers.dll',
    'README.md'
)) {
    Assert-True ($packageProject.Contains($required, [StringComparison]::Ordinal)) "The package project does not include '$required'."
}

$analyzerProject = Get-Text 'src/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.csproj'
$versionMatch = [regex]::Match($analyzerProject, '<Version>([^<]+)</Version>')
Assert-True $versionMatch.Success 'The analyzer project has no <Version> property.'
$version = $versionMatch.Groups[1].Value

foreach ($projectPath in @(
    'src/DependencyInjection.Lifetime.Analyzers.Package/DependencyInjection.Lifetime.Analyzers.Package.csproj',
    'src/DependencyInjection.Lifetime.Analyzers.Scan/DependencyInjection.Lifetime.Analyzers.Scan.csproj'
)) {
    $text = Get-Text $projectPath
    Assert-True ($text -match '_DiAnalyzerProject') "$projectPath does not derive its version from the analyzer project."
    Assert-True ($text -notmatch '<Version>[0-9]') "$projectPath has a second literal version."
}

Write-Host 'Checking the pinned scan corpus...'
$corpus = Get-Content -LiteralPath (Get-RepositoryPath 'tests/scan-corpus/corpus.json') -Raw | ConvertFrom-Json
$repositories = @($corpus.repositories)
Assert-True ($repositories.Count -ge 8 -and $repositories.Count -le 15) "The corpus must pin 8 to 15 repositories; it has $($repositories.Count)."

$names = @($repositories | ForEach-Object name)
$duplicates = @($names | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
Assert-True ($duplicates.Count -eq 0) "Duplicate corpus names: $($duplicates -join ', ')."

$baselineRoot = Get-RepositoryPath 'tests/scan-corpus/baselines'
foreach ($repository in $repositories) {
    Assert-True ([string]$repository.commit -match '^[0-9a-f]{40}$') "Corpus entry '$($repository.name)' does not pin a full lowercase commit SHA."
    Assert-True ([string]$repository.target -notmatch '^[/\\]') "Corpus entry '$($repository.name)' has an absolute target."

    $baselinePath = Join-Path $baselineRoot "$($repository.name).txt"
    Assert-True (Test-Path -LiteralPath $baselinePath) "Missing baseline for '$($repository.name)'."

    $header = @{}
    foreach ($line in [IO.File]::ReadAllLines($baselinePath)) {
        $match = [regex]::Match($line, '^#\s*(?<key>[^:]+):\s*(?<value>.*)$')
        if ($match.Success) {
            $header[$match.Groups['key'].Value.Trim()] = $match.Groups['value'].Value.Trim()
        }
    }

    Assert-True ($header['repository'] -eq [string]$repository.repository) "Baseline '$($repository.name)' was generated for a different repository."
    Assert-True ($header['commit'] -eq [string]$repository.commit) "Baseline '$($repository.name)' was generated at a different commit."
    Assert-True ($header['target'] -eq [string]$repository.target) "Baseline '$($repository.name)' was generated for a different target."

    $manifestExcludes = @($repository.excludes | Where-Object { $_ }) -join ','
    $baselineExcludes = if ($header.ContainsKey('excludes')) { [string]$header['excludes'] } else { '' }
    Assert-True ($baselineExcludes -eq $manifestExcludes) "Baseline '$($repository.name)' excludes '$baselineExcludes'; the manifest excludes '$manifestExcludes'."
}

$knownBaselineNames = @($repositories | ForEach-Object { "$($_.name).txt" })
$staleBaselines = @(
    Get-ChildItem -LiteralPath $baselineRoot -Filter '*.txt' -File |
        Where-Object { $knownBaselineNames -notcontains $_.Name }
)
Assert-True ($staleBaselines.Count -eq 0) "Baseline files have no corpus entry: $($staleBaselines.Name -join ', ')."

Write-Host 'Checking scanner documentation...'
Assert-Contains 'src/DependencyInjection.Lifetime.Analyzers.Scan/README.md' 'di-scan' 'The scanner README does not document the di-scan command.'
Assert-Contains 'docs/scanning.md' 'Invoke-ScanRegression\.ps1' 'docs/scanning.md does not document the scan regression harness.'
Assert-Contains 'docs/scan-regression.md' 'tests/scan-corpus/corpus\.json' 'docs/scan-regression.md does not document the corpus manifest.'

Write-Host "Repository validation passed: $($diagnosticIds.Count) rules, $($repositories.Count) pinned repositories."
