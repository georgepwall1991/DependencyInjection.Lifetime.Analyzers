<#
.SYNOPSIS
Checks the scanner's findings against a pinned corpus of public .NET repositories.

.DESCRIPTION
Clones or updates every repository in tests/scan-corpus/corpus.json at its pinned commit,
builds di-scan from source, scans the configured target, and compares the findings with the
committed baseline for that repository. The script exits non-zero when a finding appears,
disappears, or the scanner exit code changes.

The baselines are a regression signal, not a correctness oracle. A baseline diff means the
analyzer behavior moved; decide whether the change is intentional before updating it.

By default the repositories are cached under artifacts/scan-corpus/repos and the SARIF and
scan logs are written under artifacts/scan-corpus. Both are ignored by git.

.PARAMETER Name
Only run the named corpus entries, such as App-vNext_Polly. Names come from corpus.json.

.PARAMETER CachePath
Where to keep the repository clones. Point this at an existing checkout cache to avoid
downloading the corpus again.

.PARAMETER UpdateBaselines
Rewrite the baseline for each scanned repository instead of failing on drift.

.PARAMETER NoBuild
Reuse the scanner already in src/DependencyInjection.Lifetime.Analyzers.Scan/bin/Release instead
of building it first.

.EXAMPLE
pwsh ./scripts/Invoke-ScanRegression.ps1

.EXAMPLE
pwsh ./scripts/Invoke-ScanRegression.ps1 -Name App-vNext_Polly -Name ThreeMammals_Ocelot

.EXAMPLE
pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines

.EXAMPLE
pwsh ./scripts/Invoke-ScanRegression.ps1 -NoBuild -CachePath /path/to/existing/corpus
#>
[CmdletBinding()]
param(
    [string[]]$Name,
    [string]$CachePath,
    [switch]$UpdateBaselines,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$corpusRoot = Join-Path $repoRoot 'tests/scan-corpus'
$manifestPath = Join-Path $corpusRoot 'corpus.json'
$baselineRoot = Join-Path $corpusRoot 'baselines'
$artifactsRoot = Join-Path $repoRoot 'artifacts/scan-corpus'
$sarifRoot = Join-Path $artifactsRoot 'sarif'
$logRoot = Join-Path $artifactsRoot 'logs'

if (-not $CachePath) {
    $CachePath = Join-Path $artifactsRoot 'repos'
}

$CachePath = [IO.Path]::GetFullPath($CachePath)

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "The corpus manifest is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$allRepositories = @($manifest.repositories)
if ($allRepositories.Count -eq 0) {
    throw 'The corpus manifest has no repositories.'
}

foreach ($repository in $allRepositories) {
    foreach ($field in @('name', 'repository', 'url', 'commit', 'target')) {
        $property = $repository.PSObject.Properties[$field]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "A corpus entry is missing '$field'."
        }
    }

    if ([string]$repository.commit -notmatch '^[0-9a-f]{40}$') {
        throw "Corpus entry '$($repository.name)' does not pin a full lowercase commit SHA."
    }
}

$duplicateNames = @(
    $allRepositories |
        Group-Object -Property name |
        Where-Object Count -gt 1 |
        ForEach-Object Name
)
if ($duplicateNames.Count -gt 0) {
    throw "Duplicate corpus names: $($duplicateNames -join ', ')."
}

$selectedRepositories = $allRepositories
if ($Name) {
    $unknownNames = @($Name | Where-Object { $allRepositories.name -notcontains $_ })
    if ($unknownNames.Count -gt 0) {
        throw "Unknown corpus name(s): $($unknownNames -join ', '). Available names: $($allRepositories.name -join ', ')."
    }

    $selectedRepositories = @($allRepositories | Where-Object { $Name -contains $_.name })
}

foreach ($directory in @($CachePath, $artifactsRoot, $sarifRoot, $logRoot, $baselineRoot)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

$scannerProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers.Scan/DependencyInjection.Lifetime.Analyzers.Scan.csproj'
$scannerDll = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers.Scan/bin/Release/net8.0/DependencyInjection.Lifetime.Analyzers.Scan.dll'

if (-not $NoBuild) {
    Write-Host 'Building the scanner...'
    & dotnet build $scannerProject --configuration Release --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'The scanner build failed.'
    }
}

if (-not (Test-Path -LiteralPath $scannerDll)) {
    throw "The scanner was not found at $scannerDll. Run without -NoBuild, or build it first."
}

$script:ScannerDll = $scannerDll

function Invoke-Git {
    param(
        [string[]]$GitArguments,
        [string]$Description
    )

    $output = & git @GitArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (git exit code $LASTEXITCODE):`n$($output -join [Environment]::NewLine)"
    }
}

function Sync-Repository {
    param($Repository)

    $path = Join-Path $CachePath ([string]$Repository.name)
    if (-not (Test-Path -LiteralPath (Join-Path $path '.git'))) {
        if (Test-Path -LiteralPath $path) {
            throw "The cache path '$path' exists but is not a git clone. Delete it and rerun."
        }

        Write-Host "    cloning $($Repository.url)"
        Invoke-Git -GitArguments @('clone', '--quiet', '--filter=blob:none', '--no-checkout', [string]$Repository.url, $path) -Description "Cloning $($Repository.repository)"
    }

    & git -C $path cat-file -e "$($Repository.commit)^{commit}" 2>$null | Out-Null
    $hasCommit = $LASTEXITCODE -eq 0
    if (-not $hasCommit) {
        Write-Host "    fetching $($Repository.commit.Substring(0, 12))"
        & git -C $path fetch --quiet origin ([string]$Repository.commit) 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Invoke-Git -GitArguments @('-C', $path, 'fetch', '--quiet', 'origin') -Description "Fetching $($Repository.repository)"
        }
    }

    Invoke-Git -GitArguments @('-C', $path, 'checkout', '--quiet', '--force', '--detach', [string]$Repository.commit) -Description "Checking out $($Repository.repository) at $($Repository.commit)"

    # Newer git versions write repositoryformatversion 1 for partial clones. Older
    # Microsoft.Build.Tasks.Git versions (used by SourceLink in several pinned repos) refuse to
    # read that version, so the cache clone is normalized to the format they support.
    & git -C $path config core.repositoryformatversion 0 2>$null | Out-Null

    return $path
}

function Convert-ToRepoRelativePath {
    param(
        [string]$Uri,
        [string]$RepositoryPath
    )

    $path = $Uri -replace '\\', '/'
    if ($path.StartsWith('file://', [StringComparison]::OrdinalIgnoreCase)) {
        $path = ([Uri]$path).LocalPath
    }

    if ([IO.Path]::IsPathRooted($path)) {
        $path = [IO.Path]::GetRelativePath($RepositoryPath, $path)
    }

    $path = [Uri]::UnescapeDataString($path) -replace '\\', '/'
    while ($path.StartsWith('./', [StringComparison]::Ordinal)) {
        $path = $path.Substring(2)
    }

    return $path
}

function Get-Findings {
    param(
        [string]$SarifPath,
        [string]$RepositoryPath
    )

    if (-not (Test-Path -LiteralPath $SarifPath)) {
        throw "The scanner did not write a SARIF report at $SarifPath. See the scan log for the build failure."
    }

    $document = Get-Content -LiteralPath $SarifPath -Raw | ConvertFrom-Json
    $findings = [System.Collections.Generic.List[string]]::new()

    foreach ($run in @($document.runs)) {
        if ($null -eq $run) {
            continue
        }

        foreach ($result in @($run.results)) {
            if ($null -eq $result) {
                continue
            }

            $ruleId = [string]$result.ruleId
            $locations = @($result.locations)
            if ([string]::IsNullOrWhiteSpace($ruleId) -or $locations.Count -eq 0) {
                continue
            }

            $physicalLocation = $locations[0].physicalLocation
            if ($null -eq $physicalLocation) {
                continue
            }

            $uri = [string]$physicalLocation.artifactLocation.uri
            $line = [int]$physicalLocation.region.startLine
            $column = [int]$physicalLocation.region.startColumn
            $path = Convert-ToRepoRelativePath -Uri $uri -RepositoryPath $RepositoryPath
            $findings.Add("$ruleId $path`:$line`:$column")
        }
    }

    $sorted = $findings.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return $sorted
}

function Read-Baseline {
    param([string]$Path)

    $header = @{}
    $findings = [System.Collections.Generic.List[string]]::new()

    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }

        if ($trimmed.StartsWith('#')) {
            $match = [regex]::Match($trimmed, '^#\s*(?<key>[^:]+):\s*(?<value>.*)$')
            if ($match.Success) {
                $header[$match.Groups['key'].Value.Trim()] = $match.Groups['value'].Value.Trim()
            }

            continue
        }

        if ($trimmed -notmatch '^DI\d{3}[a-z]? .+:\d+:\d+$') {
            throw "Unexpected line in baseline '$Path': $trimmed"
        }

        $findings.Add($trimmed)
    }

    return [pscustomobject]@{
        Header = $header
        Findings = $findings.ToArray()
    }
}

function Write-Baseline {
    param(
        [string]$Path,
        $Repository,
        [int]$ExitCode,
        [string[]]$Findings
    )

    $sorted = [string[]]@($Findings | Where-Object { $_ })
    [Array]::Sort($sorted, [StringComparer]::Ordinal)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# DI scan regression baseline. Regenerate with scripts/Invoke-ScanRegression.ps1 -UpdateBaselines.')
    $lines.Add("# repository: $($Repository.repository)")
    $lines.Add("# commit: $($Repository.commit)")
    $lines.Add("# target: $($Repository.target)")
    $excludes = @($Repository.excludes | Where-Object { $_ })
    if ($excludes.Count -gt 0) {
        $lines.Add("# excludes: $($excludes -join ',')")
    }
    $lines.Add("# exit: $ExitCode")
    foreach ($finding in $sorted) {
        $lines.Add($finding)
    }

    [IO.File]::WriteAllText($Path, ($lines -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
}

function Invoke-Scanner {
    param(
        [string]$RepositoryPath,
        [string]$Target,
        [string]$Framework,
        [string[]]$Excludes,
        [string]$SarifPath,
        [string]$LogPath
    )

    $arguments = @(
        $script:ScannerDll,
        $Target,
        '--findings', 'all',
        '--top', '0',
        '--no-github',
        '--sarif', $SarifPath
    )

    if ($Framework) {
        $arguments += @('-f', $Framework)
    }

    foreach ($exclude in @($Excludes)) {
        if ($exclude) {
            $arguments += @('--exclude', [string]$exclude)
        }
    }

    Push-Location $RepositoryPath
    try {
        & dotnet @arguments > $LogPath 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    return $exitCode
}

function Show-LogTail {
    param(
        [string]$Path,
        [int]$Lines = 10
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Write-Host "    Last $Lines lines of ${Path}:"
    Get-Content -LiteralPath $Path -Tail $Lines | ForEach-Object { Write-Host "      $_" }
}

function Show-FindingDiff {
    param(
        [string[]]$Items,
        [string]$Marker,
        [string]$Color
    )

    if ($null -eq $Items -or $Items.Count -eq 0) {
        return
    }

    $limit = 20
    foreach ($item in @($Items | Select-Object -First $limit)) {
        Write-Host "    $Marker $item" -ForegroundColor $Color
    }

    if ($Items.Count -gt $limit) {
        Write-Host "    ... and $($Items.Count - $limit) more" -ForegroundColor $Color
    }
}

$knownBaselineNames = @($allRepositories | ForEach-Object { "$($_.name).txt" })
$failures = [System.Collections.Generic.List[string]]::new()
$staleBaselines = @(
    Get-ChildItem -LiteralPath $baselineRoot -Filter '*.txt' -File -ErrorAction SilentlyContinue |
        Where-Object { $knownBaselineNames -notcontains $_.Name }
)

if ($staleBaselines.Count -gt 0) {
    $staleNames = @($staleBaselines | ForEach-Object Name)
    if ($UpdateBaselines) {
        Write-Warning "Baseline files have no corpus entry and were left alone: $($staleNames -join ', ')."
    }
    else {
        $failures.Add("Baseline files have no corpus entry: $($staleNames -join ', ').")
    }
}

Write-Host "Scan regression corpus: $($selectedRepositories.Count) repositor$(if ($selectedRepositories.Count -eq 1) { 'y' } else { 'ies' })"
Write-Host "Cache: $CachePath"

$index = 0
$totalFindings = 0

foreach ($repository in $selectedRepositories) {
    $index++
    $baselinePath = Join-Path $baselineRoot "$($repository.name).txt"
    $expectedExitCode = if ($null -eq $repository.expectedExitCode) { 0 } else { [int]$repository.expectedExitCode }

    Write-Host ''
    Write-Host "[$index/$($selectedRepositories.Count)] $($repository.repository) @ $($repository.commit.Substring(0, 12))"
    if ($repository.notes) {
        Write-Host "    $($repository.notes)"
    }

    if (-not $UpdateBaselines -and -not (Test-Path -LiteralPath $baselinePath)) {
        $failures.Add("$($repository.name): the baseline is missing. Run with -UpdateBaselines.")
        continue
    }

    try {
        $repositoryPath = Sync-Repository -Repository $repository
        $targetPath = Join-Path $repositoryPath ([string]$repository.target)
        if (-not (Test-Path -LiteralPath $targetPath)) {
            throw "Target '$($repository.target)' was not found after checkout."
        }

        $sarifPath = Join-Path $sarifRoot "$($repository.name).sarif"
        $logPath = Join-Path $logRoot "$($repository.name).log"
        if (Test-Path -LiteralPath $sarifPath) {
            Remove-Item -LiteralPath $sarifPath -Force
        }

        $exitCode = Invoke-Scanner -RepositoryPath $repositoryPath -Target ([string]$repository.target) -Framework ([string]$repository.framework) -Excludes @($repository.excludes) -SarifPath $sarifPath -LogPath $logPath

        if ($exitCode -ne $expectedExitCode) {
            Write-Host "    Scanner exited $exitCode; the manifest expects $expectedExitCode." -ForegroundColor Red
            Show-LogTail -Path $logPath
            $failures.Add("$($repository.name): the manifest expects exit $expectedExitCode but the scanner exited $exitCode. Update expectedExitCode in corpus.json if the new result is stable.")
        }

        $currentFindings = @(Get-Findings -SarifPath $sarifPath -RepositoryPath $repositoryPath)

        if ($UpdateBaselines) {
            Write-Baseline -Path $baselinePath -Repository $repository -ExitCode $exitCode -Findings $currentFindings
            $totalFindings += $currentFindings.Count
            Write-Host "    wrote $($currentFindings.Count) finding(s) to tests/scan-corpus/baselines/$($repository.name).txt"
            continue
        }

        $baseline = Read-Baseline -Path $baselinePath

        if (-not $baseline.Header.ContainsKey('repository') -or [string]$baseline.Header['repository'] -ne [string]$repository.repository) {
            $failures.Add("$($repository.name): the baseline was generated for a different repository. Run with -UpdateBaselines.")
        }

        if (-not $baseline.Header.ContainsKey('commit') -or [string]$baseline.Header['commit'] -ne [string]$repository.commit) {
            $failures.Add("$($repository.name): the baseline was generated at $($baseline.Header['commit']); the manifest pins $($repository.commit). Run with -UpdateBaselines.")
        }

        if (-not $baseline.Header.ContainsKey('target') -or [string]$baseline.Header['target'] -ne [string]$repository.target) {
            $failures.Add("$($repository.name): the baseline targets '$($baseline.Header['target'])'; the manifest targets '$($repository.target)'. Run with -UpdateBaselines.")
        }

        if (-not $baseline.Header.ContainsKey('exit') -or [int]$baseline.Header['exit'] -ne $exitCode) {
            $failures.Add("$($repository.name): the baseline recorded scanner exit $($baseline.Header['exit']); the scanner exited $exitCode.")
        }

        $manifestExcludes = @($repository.excludes | Where-Object { $_ }) -join ','
        $baselineExcludes = if ($baseline.Header.ContainsKey('excludes')) { [string]$baseline.Header['excludes'] } else { '' }
        if ($baselineExcludes -ne $manifestExcludes) {
            $failures.Add("$($repository.name): the baseline excludes '$baselineExcludes'; the manifest excludes '$manifestExcludes'. Run with -UpdateBaselines.")
        }

        $baselineSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$baseline.Findings, [StringComparer]::Ordinal)
        $currentSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$currentFindings, [StringComparer]::Ordinal)
        $added = @($currentFindings | Where-Object { -not $baselineSet.Contains($_) })
        $removed = @($baseline.Findings | Where-Object { -not $currentSet.Contains($_) })

        if ($added.Count -eq 0 -and $removed.Count -eq 0) {
            $totalFindings += $currentFindings.Count
            Write-Host "    OK: $($currentFindings.Count) finding(s) match the baseline."
            continue
        }

        if ($added.Count -gt 0) {
            Write-Host "    $($added.Count) new finding(s) not in the baseline:" -ForegroundColor Red
            Show-FindingDiff -Items $added -Marker '+' -Color Red
            $failures.Add("$($repository.name): $($added.Count) new finding(s).")
        }

        if ($removed.Count -gt 0) {
            Write-Host "    $($removed.Count) baseline finding(s) no longer reported:" -ForegroundColor Yellow
            Show-FindingDiff -Items $removed -Marker '-' -Color Yellow
            $failures.Add("$($repository.name): $($removed.Count) finding(s) disappeared from the baseline.")
        }
    }
    catch {
        $failures.Add("$($repository.name): $($_.Exception.Message)")
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "Scan regression failed with $($failures.Count) problem$(if ($failures.Count -eq 1) { '' } else { 's' }):" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure"
    }

    Write-Host ''
    Write-Host 'If the drift is intentional, run scripts/Invoke-ScanRegression.ps1 -UpdateBaselines and review the baseline diff before committing.'
    exit 1
}

Write-Host ''
if ($UpdateBaselines) {
    Write-Host "Updated $($selectedRepositories.Count) baseline(s) with $totalFindings finding(s). Review: git diff tests/scan-corpus/baselines"
}
else {
    Write-Host "Scan regression passed: $($selectedRepositories.Count) repositor$(if ($selectedRepositories.Count -eq 1) { 'y' } else { 'ies' }), $totalFindings finding(s) match the baselines."
}
