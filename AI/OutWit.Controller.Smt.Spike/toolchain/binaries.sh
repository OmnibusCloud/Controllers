#!/usr/bin/env bash
# SM-0 spike: the binaries a run job needs for one runtime identifier, each pinned in pins.env.
#
#   binaries.sh natives <rid> <harness-bin>   the wasmtime 48.0.8 C API library, placed where
#                                             the shared host's resolver looks for it
#                                             (<harness-bin>/runtimes/<rid>/native/)
#   binaries.sh z3 <rid> <out-dir>            the native Z3 release (the baseline); prints the
#                                             path of the z3 executable on its last line
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1091
. "$HERE/pins.env"

KIND="$1"
RID="$2"
OUT="$(mkdir -p "$3" && cd "$3" && pwd)"
DOWNLOADS="${RUNNER_TEMP:-/tmp}/smt-sm0-downloads"
mkdir -p "$DOWNLOADS"

sha() {
  if command -v sha256sum > /dev/null 2>&1; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1
}

fetch() {
  local url="$1" pin="$2" file="$3" seen
  [ -f "$file" ] || curl -fsSL --retry 3 --max-time 900 -o "$file" "$url"
  seen="$(sha "$file")"
  if [ "$seen" != "$pin" ]; then
    echo "HASH MISMATCH $(basename "$file"): expected $pin, got $seen" >&2
    rm -f "$file"
    exit 1
  fi
  echo "pinned   $seen  $(basename "$file")" >&2
}

key="$(echo "$RID" | tr 'a-z-' 'A-Z_')"

case "$KIND" in
  natives)
    url_var="WASMTIME_NATIVE_${key}_URL"
    pin_var="WASMTIME_NATIVE_${key}_SHA256"
    url="${!url_var}"
    archive="$DOWNLOADS/$(basename "$url")"
    fetch "$url" "${!pin_var}" "$archive"
    top="$(basename "$url")"; top="${top%.zip}"; top="${top%.tar.xz}"
    case "$RID" in
      win-*) inner="lib/wasmtime.dll"; name="wasmtime.dll" ;;
      osx-*) inner="lib/libwasmtime.dylib"; name="libwasmtime.dylib" ;;
      *) inner="lib/libwasmtime.so"; name="libwasmtime.so" ;;
    esac
    dir="$OUT/runtimes/$RID/native"
    mkdir -p "$dir"
    case "$archive" in
      *.zip) unzip -p "$archive" "$top/$inner" > "$dir/$name" ;;
      *) (cd "$DOWNLOADS" && tar -xJf "$(basename "$archive")" -O "$top/$inner") > "$dir/$name" ;;
    esac
    [ -s "$dir/$name" ] || { echo "EMPTY $dir/$name" >&2; exit 1; }
    echo "wasmtime $WASMTIME_VERSION native: $dir/$name $(sha "$dir/$name")" >&2
    ;;
  z3)
    url_var="Z3_NATIVE_${key}_URL"
    pin_var="Z3_NATIVE_${key}_SHA256"
    url="${!url_var}"
    archive="$DOWNLOADS/$(basename "$url")"
    fetch "$url" "${!pin_var}" "$archive"
    (cd "$OUT" && unzip -q -o "$archive")
    z3="$(find "$OUT" -type f \( -name z3 -o -name z3.exe \) -path '*/bin/*' | head -1)"
    [ -n "$z3" ] || { echo "no z3 executable in $(basename "$archive")" >&2; exit 1; }
    chmod +x "$z3" || true
    "$z3" -version >&2
    echo "$z3"
    ;;
  *)
    echo "usage: binaries.sh natives|z3 <rid> <dir>" >&2
    exit 64
    ;;
esac
