#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
probe="$root/tests/e2e/probe"
cd "$root"
mkdir -p "$probe/src/csharp/Probe/Native" "$probe/src/csharp/Other/Native" "$probe/build"
dotnet run --project src/Kabic -- bindings --root "$probe"
dotnet run --project src/Kabic -- generate --root "$probe"
zig cc -shared -fPIC -I "$probe/src/c" -o "$probe/build/libke_probe.so" "$probe/src/native/probe.c"
dotnet run --project "$root/tests/e2e/surface/Surface.csproj"
