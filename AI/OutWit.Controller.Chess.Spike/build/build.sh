#!/usr/bin/env bash
# Builds Stockfish from the pinned upstream tag with one recipe per RID and records what was
# built: SHA-256 and size of the binary, the time the build took and the toolchain.
# Nothing here is committed or uploaded: the source is cloned into the work folder and the
# binary stays in the out folder of the runner.
#
# usage: build.sh <recipe> <work-dir> <out-dir>
#   recipe: linux-x64 | win-x64 | linux-arm64 | win-arm64 | osx-fat | osx-x64
#   env:    SDE_DIR  the x64 recipes run their PGO benchmark of every ISA slice under Intel SDE
#                    (-future), as upstream's release workflow does
#           JOBS     parallel make jobs (default 4)
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
# shellcheck disable=SC1091
. "$here/../pins.env"

recipe=$1
work=$2
out=$3
jobs=${JOBS:-4}
mkdir -p "$work" "$out"

sha256() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1" | cut -d' ' -f1
  else shasum -a 256 < "$1" | cut -d' ' -f1; fi
}

# 1. Source: the pinned tag, verified against the pinned commit.
rm -rf "$work/stockfish"
git clone --quiet --depth 1 --branch "$SF_TAG" "$SF_REPO" "$work/stockfish"
head=$(git -C "$work/stockfish" rev-parse HEAD)
if [ "$head" != "$SF_COMMIT" ]; then
  echo "::error::tag $SF_TAG resolves to $head, pinned $SF_COMMIT" >&2
  exit 1
fi
src="$work/stockfish/src"

# 2. Network: fetched by name and verified by its full SHA-256. Upstream's net.sh compares only
#    the first 12 hex digits (the file name); with the file in place it skips its own download.
net="$src/$SF_NET"
for url in "https://tests.stockfishchess.org/api/nn/$SF_NET" \
           "https://github.com/official-stockfish/networks/raw/master/$SF_NET"; do
  if curl -fsSL --retry 3 --max-time 900 -o "$net" "$url" && [ "$(sha256 "$net")" = "$SF_NET_SHA256" ]; then
    echo "network $SF_NET from $url, SHA-256 verified"
    break
  fi
  rm -f "$net"
done
if [ ! -f "$net" ]; then
  echo "::error::network $SF_NET could not be fetched and verified" >&2
  exit 1
fi

# 3. Build, one recipe per RID (upstream's universal recipes of the tag).
case "$recipe" in
  linux-x64|win-x64)
    sde="$SDE_DIR/sde"
    if [ "$recipe" = win-x64 ]; then sde="$(cygpath -u "$SDE_DIR")/sde.exe"; fi
    exe=stockfish; [ "$recipe" = win-x64 ] && exe=stockfish.exe
    cmd=(make -j"$jobs" -C "$src" ARCH=x86-64-universal profile-build "RUN_PREFIX=$sde -future --")
    ;;
  linux-arm64)
    exe=stockfish
    cmd=(make -j"$jobs" -C "$src" ARCH=arm64-universal profile-build)
    ;;
  win-arm64)
    exe=stockfish.exe
    cmd=(make -j"$jobs" -C "$src" ARCH=arm64-universal profile-build COMP=clang COMPCXX=clang++)
    ;;
  osx-fat)
    # x86-64 slice: x86-64-universal without PGO; arm64 slice: apple-silicon with PGO; one net
    exe=stockfish
    cmd=(make -j"$jobs" -C "$src" macos-lipo COMP=clang)
    ;;
  osx-x64)
    # The x86-64 slice recipe of macos-lipo, built natively on an Intel Mac and embedding its
    # own copy of the net (macos-lipo shares the arm64 slice's copy instead, so upstream never
    # compiles the #embed path with Apple clang). The Apple clang of macos-15-intel types the
    # #embed bytes as signed and rejects the narrowing into unsigned char; the warning flag
    # only lets the bytes convert, it changes no code.
    exe=stockfish
    cmd=(make -j"$jobs" -C "$src" ARCH=x86-64-universal build COMP=clang
         EXTRACXXFLAGS=-Wno-c++11-narrowing)
    ;;
  *)
    echo "unknown recipe $recipe" >&2
    exit 2
    ;;
esac

echo "build: ${cmd[*]}"
t0=$(date +%s)
"${cmd[@]}" > "$work/build.log" 2>&1 || { tail -n 80 "$work/build.log"; exit 1; }
t1=$(date +%s)
tail -n 12 "$work/build.log"

case "$recipe" in
  osx-*) ;;
  *) strip "$src/$exe" ;;
esac

cp "$src/$exe" "$out/$exe"
size=$(wc -c < "$out/$exe" | tr -d ' ')
hash=$(sha256 "$out/$exe")
cxx=gcc; case "$recipe" in win-arm64|osx-*) cxx=clang ;; esac
toolchain=$( ("$cxx" --version 2>/dev/null || true) | sed -n 1p | tr -d '"\\')

cat > "$out/build.json" <<EOF
{"recipe": "$recipe", "tag": "$SF_TAG", "commit": "$SF_COMMIT", "net": "$SF_NET",
 "net_sha256": "$SF_NET_SHA256", "binary": "$exe", "sha256": "$hash", "size": $size,
 "build_seconds": $((t1 - t0)), "toolchain": "$toolchain", "command": "${cmd[*]}"}
EOF
cat "$out/build.json"
