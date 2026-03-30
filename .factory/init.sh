#!/usr/bin/env bash
set -euo pipefail

dotnet restore "/Users/georgewall/RiderProjects/DependencyInjection.Lifetime.Analyzers/DependencyInjection.Lifetime.Analyzers.sln"
node --version >/dev/null
