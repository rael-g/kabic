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
zlib="$root/tests/e2e/zlib"
mkdir -p "$zlib/src/csharp/Zlib/Native"
dotnet run --project src/Kabic -- bindings --root "$zlib"
dotnet run --project src/Kabic -- generate --root "$zlib"
mkdir -p "$zlib/build"
cp -L "${ZLIB_SO:-/run/host/usr/lib/libz.so.1}" "$zlib/build/libz.so"
dotnet run --project "$root/tests/e2e/zlibrun/ZlibRun.csproj"
sqlite="$root/tests/e2e/sqlite"
mkdir -p "$sqlite/src/csharp/Sqlite/Native" "$sqlite/build"
dotnet run --project src/Kabic -- bindings --root "$sqlite"
dotnet run --project src/Kabic -- generate --root "$sqlite"
cp -L "${SQLITE_SO:-/usr/lib/x86_64-linux-gnu/libsqlite3.so.0}" "$sqlite/build/libsqlite3.so"
dotnet run --project "$root/tests/e2e/sqliterun/SqliteRun.csproj"
vulkan="$root/tests/e2e/vulkan"
mkdir -p "$vulkan/src/csharp/Vulkan/Native" "$vulkan/build"
dotnet run --project src/Kabic -- bindings --root "$vulkan"
dotnet run --project src/Kabic -- generate --root "$vulkan"
cp -L "${VULKAN_SO:-/usr/lib/x86_64-linux-gnu/libvulkan.so.1}" "$vulkan/build/libvulkan.so"
dotnet run --project "$root/tests/e2e/vulkanrun/VulkanRun.csproj"
for fixture in "$probe" "$zlib" "$sqlite" "$vulkan"; do
  dotnet run --project src/Kabic -- write zig --root "$fixture"
  (cd "$fixture/src/zig/abi" && zig build test --global-cache-dir "$fixture/build/zig-global")
done
LD_LIBRARY_PATH="$probe/build" zig test --global-cache-dir "$probe/build/zig-global" --dep abi -Mroot=tests/e2e/zigrun/probe.zig \
  -Mabi="$probe/src/zig/abi/probe/probe.zig" -L "$probe/build" -lke_probe -lc
LD_LIBRARY_PATH="$sqlite/build" zig test --global-cache-dir "$sqlite/build/zig-global" --dep abi -Mroot=tests/e2e/zigrun/sqlite.zig \
  -Mabi="$sqlite/src/zig/abi/sqlite/sqlite.zig" -L "$sqlite/build" -lsqlite3 -lc
zig test --global-cache-dir "$vulkan/build/zig-global" --dep abi -Mroot=tests/e2e/zigrun/vulkan.zig \
  -Mabi="$vulkan/src/zig/abi/vulkan/vulkan.zig" -lc -fno-emit-bin
