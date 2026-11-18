#!/usr/bin/env bash
# The archives of the Verify controller's data assets, built from their pins and packed the same
# way every time:
#
#   verify-wasm.zip           the language runtimes, as verify-assets lays them out without the
#                             numpy runtime's folder
#   wasmtime-win-x64.zip      the native engine library of each platform, as natives.sh lays it
#   wasmtime-linux-x64.zip    out, with its licence, its notice and its third-party notices
#   wasmtime-osx-arm64.zip
#
# With --check each archive is held to the SHA-256 and the size the controller's project file
# pins for it, and an archive that is not the pinned one fails the run: what a node is sent is
# then what these pins build to, byte for byte. Without it the hashes are printed, for the
# project file. verify-python-numpy.zip is not made here: the numpy workflow builds it
# (toolchain/python-numpy) and holds it to its pin itself.
#
# An archive is packed by python-numpy/layers.py (asset): its entries in a fixed order, with
# fixed times and modes, compressed by zlib at level 9. The same files give the same bytes
# wherever this runs; that file says what the bytes depend on.
#
# Usage: archives.sh <out-dir> [<work-dir>] [--check]
# Needs dotnet, python3 (PYTHON names another), curl, unzip, tar with xz.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPOSITORY="$(cd "$HERE/../../.." && pwd)"
PROJECT="$REPOSITORY/AI/OutWit.Controller.AI.Verify/OutWit.Controller.AI.Verify.csproj"
TOOL="$REPOSITORY/AI/OutWit.Controller.AI.Verify.Assets/OutWit.Controller.AI.Verify.Assets.csproj"
PYTHON="${PYTHON:-python3}"

CHECK=0
POSITIONAL=()
for argument in "$@"; do
  case "$argument" in
    --check) CHECK=1 ;;
    *) POSITIONAL+=("$argument") ;;
  esac
done
[ "${#POSITIONAL[@]}" -ge 1 ] || { echo "usage: archives.sh <out-dir> [<work-dir>] [--check]"; exit 2; }

OUT="$(mkdir -p "${POSITIONAL[0]}" && cd "${POSITIONAL[0]}" && pwd)"
WORK="${POSITIONAL[1]:-$(mktemp -d)}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"

sha() {
  if command -v sha256sum > /dev/null 2>&1; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1
}

# pinned <asset id> <element>: what the project file states for a data asset.
pinned() {
  sed -n "/<ControllerDataAsset Include=\"$1\">/,/<\/ControllerDataAsset>/p" "$PROJECT" \
    | sed -n "s:.*<$2>\(.*\)</$2>.*:\1:p"
}

echo "== the language runtimes"
dotnet run --project "$TOOL" -c Release -- build "$WORK/wasm" --cache "$WORK/downloads" --work "$WORK/layers" --without-numpy --clean
"$PYTHON" -I "$HERE/python-numpy/layers.py" asset "$WORK/wasm" "$OUT/verify-wasm.zip"

echo "== the native engine libraries"
bash "$HERE/natives.sh" "$WORK/natives" "$WORK/downloads"
for rid in win-x64 linux-x64 osx-arm64; do
  "$PYTHON" -I "$HERE/python-numpy/layers.py" asset "$WORK/natives/$rid" "$OUT/wasmtime-$rid.zip"
done

echo "== the archives (SHA-256, bytes, name) and where the project file expects each"
status=0
for asset in verify-wasm wasmtime-win-x64 wasmtime-linux-x64 wasmtime-osx-arm64; do
  file="$OUT/$asset.zip"
  seen="$(sha "$file")"
  size="$(wc -c < "$file" | tr -d ' ')"
  echo "$seen  $size  $asset.zip  ->  $(pinned "$asset" Uri)"
  if [ "$CHECK" -eq 1 ]; then
    if [ "$seen" != "$(pinned "$asset" Sha256)" ] || [ "$size" != "$(pinned "$asset" Size)" ]; then
      echo "::error::$asset.zip is not the archive the project file pins ($(pinned "$asset" Sha256), $(pinned "$asset" Size) bytes)"
      status=1
    fi
  fi
done
if [ "$CHECK" -eq 1 ] && [ "$status" -eq 0 ]; then
  echo "every archive is the one the project file pins"
fi
exit "$status"
