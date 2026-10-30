#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
probe="$root/tests/e2e/probe"
cd "$root"
dotnet run --project src/Kabic -- bindings --root "$probe"
dotnet run --project src/Kabic -- generate --root "$probe"
dotnet build "$probe/src/csharp/Probe/Probe.csproj"
