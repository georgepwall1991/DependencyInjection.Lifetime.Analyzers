param(
    [string]$PackagesDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packagesPath = Join-Path $repoRoot $PackagesDirectory
$analyzerProject = Join-Path $repoRoot 'src/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.csproj'

if (-not (Test-Path -LiteralPath $packagesPath)) {
    throw "The package directory was not found at $packagesPath. Run scripts/Validate-Package.ps1 first."
}

$version = (& dotnet msbuild $analyzerProject -getProperty:Version -nologo).Trim()
$packagePath = Join-Path $packagesPath "DependencyInjection.Lifetime.Analyzers.$version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "The analyzer package was not found at $packagePath. Run scripts/Validate-Package.ps1 first."
}

$workPath = Join-Path ([IO.Path]::GetTempPath()) "di-analyzer-consumption-$([Guid]::NewGuid().ToString('N'))"
$sarifPath = Join-Path $workPath 'diagnostics.sarif'

try {
    New-Item -ItemType Directory -Path $workPath -Force | Out-Null

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packagesPath" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $workPath 'NuGet.config') -Encoding UTF8

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="DependencyInjection.Lifetime.Analyzers" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $workPath 'App.csproj') -Encoding UTF8

    @"
using Microsoft.Extensions.DependencyInjection;

public interface IScopedService { }
public sealed class ScopedService : IScopedService { }
public sealed class CaptiveService
{
    public CaptiveService(IScopedService scoped) { }
}

public static class Program
{
    public static void Main()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScopedService, ScopedService>();
        services.AddSingleton<CaptiveService>();
    }
}
"@ | Set-Content -LiteralPath (Join-Path $workPath 'Program.cs') -Encoding UTF8

    Write-Host "Building a fresh project with the packed analyzer $version..."
    & dotnet build (Join-Path $workPath 'App.csproj') `
        --configuration Release `
        --nologo `
        --verbosity quiet `
        -p:ErrorLog=$sarifPath
    if ($LASTEXITCODE -ne 0) {
        throw "The consumer project failed to build with the packed analyzer (exit $LASTEXITCODE)."
    }

    if (-not (Test-Path -LiteralPath $sarifPath)) {
        throw "The packed analyzer did not produce a SARIF log at $sarifPath."
    }

    $sarif = Get-Content -LiteralPath $sarifPath -Raw | ConvertFrom-Json
    $rules = @(
        foreach ($run in @($sarif.runs)) {
            foreach ($result in @($run.results)) {
                [string]$result.ruleId
            }
        }
    )

    if ($rules -notcontains 'DI003') {
        throw "The packed analyzer did not report DI003. Reported: $($rules -join ', ')."
    }

    Write-Host "Analyzer package consumption passed: $version reported DI003."
}
finally {
    if (Test-Path -LiteralPath $workPath) {
        Remove-Item -LiteralPath $workPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
