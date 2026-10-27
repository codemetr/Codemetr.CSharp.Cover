#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore --configfile .config/codemetr.nuget.config
dotnet tool run codemetr-csharp -- check src --format text
