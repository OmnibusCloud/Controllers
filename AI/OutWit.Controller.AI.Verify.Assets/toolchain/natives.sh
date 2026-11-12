#!/usr/bin/env bash
# The native wasmtime libraries the Verify controller ships, one per platform, taken unchanged
# from the engine's own C API release archives (pinned in pins.env by the digests the release
# page states). Lays them out the way the controller's data assets carry them:
#
#   <out>/win-x64/wasmtime.dll          <out>/linux-x64/libwasmtime.so
#   <out>/osx-arm64/libwasmtime.dylib   and in each: LICENSE-wasmtime.txt, NOTICE.txt
#
# Usage: natives.sh <out-dir> [<download-dir>]
# The .NET binding (the Wasmtime package) stays at its own version; within the engine's
# long-term line a patch changes neither an output nor a unit of fuel.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
DOWNLOADS="${2:-$OUT/.downloads}"
mkdir -p "$DOWNLOADS"

# shellcheck disable=SC1091
. "$HERE/pins.env"

sha() {
  if command -v sha256sum > /dev/null 2>&1; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1
}

fetch() {
  local url="$1" pin="$2" file="$3" seen
  [ -f "$file" ] || curl -fsSL --retry 3 --max-time 600 -o "$file" "$url"
  seen="$(sha "$file")"
  if [ "$seen" != "$pin" ]; then
    echo "HASH MISMATCH $(basename "$file"): expected $pin, got $seen"
    rm -f "$file"
    exit 1
  fi
  echo "pinned   $seen  $(basename "$file")"
}

# rid | archive URL | archive pin | library inside the archive | library name in the asset
NATIVES="win-x64|$WASMTIME_NATIVE_WIN_X64_URL|$WASMTIME_NATIVE_WIN_X64_SHA256|lib/wasmtime.dll|wasmtime.dll
linux-x64|$WASMTIME_NATIVE_LINUX_X64_URL|$WASMTIME_NATIVE_LINUX_X64_SHA256|lib/libwasmtime.so|libwasmtime.so
osx-arm64|$WASMTIME_NATIVE_OSX_ARM64_URL|$WASMTIME_NATIVE_OSX_ARM64_SHA256|lib/libwasmtime.dylib|libwasmtime.dylib"

echo "== wasmtime $WASMTIME_NATIVE_VERSION, native libraries"
while IFS='|' read -r rid url pin inner name; do
  archive="$DOWNLOADS/$(basename "$url")"
  fetch "$url" "$pin" "$archive"

  top="$(basename "$url")"
  top="${top%.zip}"
  top="${top%.tar.xz}"

  dir="$OUT/$rid"
  rm -rf "$dir"
  mkdir -p "$dir"

  case "$archive" in
    *.zip)
      unzip -p "$archive" "$top/$inner" > "$dir/$name"
      unzip -p "$archive" "$top/LICENSE" > "$dir/LICENSE-wasmtime.txt"
      ;;
    *.tar.xz)
      # A relative name: GNU tar on Windows reads "C:" in a path as a remote host.
      (cd "$DOWNLOADS" && tar -xJf "$(basename "$archive")" -O "$top/$inner") > "$dir/$name"
      (cd "$DOWNLOADS" && tar -xJf "$(basename "$archive")" -O "$top/LICENSE") > "$dir/LICENSE-wasmtime.txt"
      ;;
  esac

  [ -s "$dir/$name" ] || { echo "EMPTY $rid/$name"; exit 1; }

  cat > "$dir/NOTICE.txt" <<EOF
Native wasmtime library for the OmnibusCloud Verify controller.

wasmtime $WASMTIME_NATIVE_VERSION (the long-term line 48), taken unchanged ($inner) from
$url
(SHA-256 of the archive $pin).
https://github.com/bytecodealliance/wasmtime, Apache-2.0 WITH LLVM-exception:
see LICENSE-wasmtime.txt. The .NET binding next to the controller is the Wasmtime
package (wasmtime-dotnet, Apache-2.0).
EOF

  echo "  $rid/$name  $(sha "$dir/$name")  $(wc -c < "$dir/$name" | tr -d ' ') bytes"
done <<< "$NATIVES"
