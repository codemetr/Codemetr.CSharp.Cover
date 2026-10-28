#!/usr/bin/env bash
# Coverage gate on this repository with the tool built from source (Release): the tests run under Coverlet,
# diff coverage is taken against the base given as $1 (default: HEAD = uncommitted changes only).
# The tool runs from a copy, because it rebuilds this solution and must not lock its own output.
set -euo pipefail
cd "$(dirname "$0")/.."
tool="$(mktemp -d)"
trap 'rm -rf "$tool"' EXIT
cp -R src/Codemetr.CSharp.Cover.Tool/bin/Release/net10.0/. "$tool/"
dotnet "$tool/Codemetr.CSharp.Cover.Tool.dll" check --base "${1:-HEAD}" --format text
