#!/usr/bin/env bash
# Coverage gate on this repository with the tool built from source (Release): the tests run under Coverlet,
# diff coverage is taken against the base given as $1 (default: HEAD = uncommitted changes only).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet src/Codemetr.CSharp.Cover.Tool/bin/Release/net10.0/Codemetr.CSharp.Cover.Tool.dll check --base "${1:-HEAD}" --format text
