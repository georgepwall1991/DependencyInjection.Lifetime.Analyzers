#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"
release_notes_file="${2:-}"
analyzer_version="$(dotnet msbuild src/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.csproj -getProperty:Version -nologo | tr -d '[:space:]')"
analyzer_package="$package_dir/DependencyInjection.Lifetime.Analyzers.$analyzer_version.nupkg"

if [[ ! -f "$analyzer_package" ]]; then
  echo "Missing analyzer package: $analyzer_package" >&2
  exit 1
fi

cmp README.md <(unzip -p "$analyzer_package" README.md)

# Product-flow visuals referenced by PackageReadmeFile must ship inside the package.
for asset in \
  assets/flow-ide-diagnostics.svg \
  assets/flow-before-after-fix.svg \
  assets/flow-ci-analyzer-loop.svg
do
  cmp "$asset" <(unzip -p "$analyzer_package" "$asset")
done

# Discoverability metadata: high-intent DI lifetime terms (NuGet search).
analyzer_nuspec="$(unzip -p "$analyzer_package" DependencyInjection.Lifetime.Analyzers.nuspec)"

for term in \
  "captive dependenc" \
  lifetime \
  "scope leak" \
  BuildServiceProvider \
  "Microsoft.Extensions.DependencyInjection" \
  singleton \
  scoped \
  transient \
  roslyn-analyzer
do
  printf '%s' "$analyzer_nuspec" | grep -Fiq "$term" || {
    echo "Analyzer nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

if [[ -n "$release_notes_file" ]]; then
  node tools/verify-package-release-notes.mjs "$analyzer_package" "$release_notes_file"
fi

echo "Verified package README, assets, and discoverability metadata for $analyzer_version."
