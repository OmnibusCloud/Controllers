#!/usr/bin/env bash
# smt-live for one machine without .NET: a self-contained folder of the tool for one platform,
# with the controller's pinned data assets beside it (the solver module and that platform's
# native engine library, resolved from the controller's manifest: see build/SmtLiveAssets.targets),
# checked, and packed into a zip. It runs on Windows (Git Bash), Linux and macOS, and builds for
# any of the three platforms from any of them.
#
#   bash publish-local.sh <rid> <folder>      rid: win-x64, linux-x64 or osx-arm64
#
# The folder is replaced; the zip is <folder>.zip beside it. The checks: the module and the
# native library are the bytes the controller's source pins (the constants the controller holds
# them to at run time), and the folder holds no other wasmtime library. The apphost of an
# osx-arm64 build made on another system carries no signature: sign it ad hoc on the Mac
# (codesign --force --sign - smt-live) and never re-sign the native library, whose SHA-256 is
# pinned.
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: bash publish-local.sh <win-x64|linux-x64|osx-arm64> <folder>" >&2
  exit 2
fi

rid=$1
out=$2
case "$rid" in
  win-x64)   lib=wasmtime.dll ;     app=smt-live.exe ;;
  linux-x64) lib=libwasmtime.so ;   app=smt-live ;;
  osx-arm64) lib=libwasmtime.dylib ; app=smt-live ;;
  *) echo "the platform must be win-x64, linux-x64 or osx-arm64, not $rid" >&2; exit 2 ;;
esac

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
if command -v python3 > /dev/null && python3 -c "" 2> /dev/null; then py=python3; else py=python; fi

sha() { if command -v sha256sum > /dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

# The pins, from the controller's source: the profile's module and the natives guard's table.
module_pin=$(sed -n 's/.*MODULE_SHA256 = "\([0-9a-f]\{64\}\)".*/\1/p' "$root/AI/OutWit.Controller.AI.Smt/Profiles/SmtProfile.cs")
module_file=$(sed -n 's/.*MODULE_FILE = "\([^"]*\)".*/\1/p' "$root/AI/OutWit.Controller.AI.Smt/Profiles/SmtProfile.cs")
natives_pin=$(sed -n "s/.*\[\"$rid\"\] = \"\([0-9a-f]\{64\}\)\".*/\1/p" "$root/AI/Shared/Wasm/WasmHostNatives.cs")
[ -n "$module_pin" ] && [ -n "$module_file" ] && [ -n "$natives_pin" ] || { echo "the pins were not found in the controller's source" >&2; exit 1; }

rm -rf "$out" "$out.zip"
dotnet publish "$here/OutWit.Controller.AI.Smt.Live.csproj" -c Release -r "$rid" --self-contained -o "$out"

module="$out/wasm/$module_file"
natives="$out/runtimes/$rid/native/$lib"
[ -f "$out/$app" ] || { echo "no $app in $out" >&2; exit 1; }
[ -f "$module" ] || { echo "no solver module at $module" >&2; exit 1; }
[ -f "$natives" ] || { echo "no native engine library at $natives" >&2; exit 1; }
[ "$(sha "$module")" = "$module_pin" ] || { echo "the module hashes to $(sha "$module"), not the pinned $module_pin" >&2; exit 1; }
[ "$(sha "$natives")" = "$natives_pin" ] || { echo "the native library hashes to $(sha "$natives"), not the pinned $natives_pin" >&2; exit 1; }

others=$(find "$out" -iname '*wasmtime*' \( -name '*.dll' -o -name '*.so' -o -name '*.dylib' \) ! -iname 'Wasmtime.Dotnet.dll' | grep -v -F "runtimes/$rid/native/$lib" || true)
[ -z "$others" ] || { echo "the folder holds another wasmtime library: $others" >&2; exit 1; }

# The zip keeps the apphost executable for unzip on Linux and macOS.
"$py" -I - "$out" "$out.zip" "$app" <<'PY'
import os, sys, zipfile
folder, archive, app = sys.argv[1], sys.argv[2], sys.argv[3]
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zip:
    for base, _, files in os.walk(folder):
        for name in sorted(files):
            path = os.path.join(base, name)
            entry = zipfile.ZipInfo.from_file(path, os.path.relpath(path, folder).replace(os.sep, "/"))
            entry.create_system = 3
            entry.external_attr = ((0o755 if name == app else 0o644) | 0o100000) << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            with open(path, "rb") as source:
                zip.writestr(entry, source.read())
PY

echo "smt-live for $rid: $out"
echo "  module  $(sha "$module")  wasm/$module_file (the profile's pin)"
echo "  natives $(sha "$natives")  runtimes/$rid/native/$lib (the pin of $rid)"
echo "  zip     $(sha "$out.zip")  $out.zip"
