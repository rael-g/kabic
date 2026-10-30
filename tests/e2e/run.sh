#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
probe="$root/tests/e2e/probe"
cd "$root"
mkdir -p "$probe/src/csharp/Probe/Native" "$probe/src/csharp/Other/Native"
dotnet run --project src/Kabic -- bindings --root "$probe"
dotnet run --project src/Kabic -- generate --root "$probe"
dotnet run --project "$root/tests/e2e/surface/Surface.csproj"
