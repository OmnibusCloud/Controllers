#!/usr/bin/env bash
# The numpy runtime's start-up snapshot, after build.sh in the same work folder:
#
#   python-numpy-reactor.wasm    the same interpreter linked as a WASI reactor with
#                                pynp_snapshot.c's entry points instead of CPython's main (it
#                                runs as the plain module does: the sandbox calls _start)
#   python-numpy-snapshot.wasm   that module after the pinned wasmtime's wizer ran
#                                wizer-initialize: the interpreter up, numpy imported
#
# The reactor's link is CPython's own link of python.wasm, read with make -n, with
# Programs/python.o replaced by pynp_snapshot.o, -mexec-model=reactor, and --wrap for the four
# WASI calls whose answers the snapshot would otherwise capture (pynp_snapshot.c).
#
# Usage: snapshot.sh <out-dir> <work-dir>
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(cd "$1" && pwd)"
WORK="$(cd "$2" && pwd)"

# shellcheck disable=SC1091
. "$HERE/pins.env"
export SOURCE_DATE_EPOCH="$CPYTHON_SOURCE_DATE_EPOCH" LC_ALL=C TZ=UTC
SDK="$WORK/wasi-sdk-24"
WASMTIME="$WORK/wasmtime/wasmtime"
CP="$WORK/Python-$CPYTHON_VERSION"
W="$CP/cross-build/wasm32-wasip1"
S="$WORK/snapshot"
rm -rf "$S"
mkdir -p "$S"

fail() {
  [ -f "$1" ] && tail -n 40 "$1"
  echo "ERROR: $2"
  exit 1
}

echo "== the reactor"
# make -n with python.c taken as changed prints CPython's compile of its main and its link.
( cd "$W" && make -n -W ../../Programs/python.c python.wasm ) > "$S/make-n.txt" 2> "$S/make-n.err" || fail "$S/make-n.err" "make -n failed"
compile="$(grep -F -- '-o Programs/python.o' "$S/make-n.txt" | tail -n 1)"
link="$(grep -F -- '-o python.wasm Programs/python.o' "$S/make-n.txt" | tail -n 1)"
[ -n "$compile" ] && [ -n "$link" ] || fail "$S/make-n.txt" "make -n printed no compile or link of python.wasm"
compile="${compile/..\/..\/Programs\/python.c/$HERE/pynp_snapshot.c}"
compile="${compile/-o Programs\/python.o/-DPYNP_SOURCE_DATE_EPOCH=${SOURCE_DATE_EPOCH}ull -ffile-prefix-map=$HERE=/toolchain -o $S/pynp_snapshot.o}"
WRAP="-Wl,--wrap=__wasi_clock_time_get -Wl,--wrap=__wasi_random_get -Wl,--wrap=__wasi_path_filestat_get -Wl,--wrap=__wasi_fd_filestat_get"
mkdir -p "$S/link"
# Linked under the plain module's name: wasm-ld writes it into the name section.
link="${link/-o python.wasm Programs\/python.o/-mexec-model=reactor $WRAP -o $S/link/python.wasm $S/pynp_snapshot.o}"
printf '%s\n%s\n' "$compile" "$link" | sed "s#$WORK#<work>#g; s#$HERE#<toolchain>#g" > "$S/commands.txt"
( cd "$W" && eval "$compile" ) > "$S/compile.log" 2>&1 || fail "$S/compile.log" "pynp_snapshot.c did not compile"
( cd "$W" && eval "$link" ) > "$S/link.log" 2>&1 || fail "$S/link.log" "the reactor's link failed"
if grep -E 'signature mismatch|duplicate symbol|undefined symbol' "$S/link.log"; then
  fail /dev/null "the reactor's link has clashing or missing symbols"
fi
"$SDK/bin/llvm-strip" --strip-debug -o "$S/python.stripped.wasm" "$S/link/python.wasm"
"$SDK/bin/llvm-objcopy" --remove-section=producers "$S/python.stripped.wasm" "$OUT/python-numpy-reactor.wasm"

echo "== the snapshot (wasmtime $("$WASMTIME" --version | cut -d' ' -f2) wizer)"
# The root wizer sees is the runtime's root: the two layers under /lib, with fixed times. The
# environment is the sandbox's.
ROOT="$S/root"
mkdir -p "$ROOT/lib"
NUMPY_ZIP="$(cd "$OUT" && ls numpy-*.zip)"
cp "$OUT/python314.zip" "$OUT/$NUMPY_ZIP" "$ROOT/lib/"
touch -d "@$SOURCE_DATE_EPOCH" "$ROOT/lib"/* "$ROOT/lib" "$ROOT"
start=$(date +%s%N)
"$WASMTIME" wizer -W exceptions=n --init-func wizer-initialize --dir "$ROOT::/" \
  --env PYTHONHOME=/ --env PYTHONHASHSEED=0 --env PYTHONDONTWRITEBYTECODE=1 --env PYTHONUNBUFFERED=1 \
  --env "PYTHONPATH=/lib/$NUMPY_ZIP" \
  -o "$S/python-snapshot.wasm" "$OUT/python-numpy-reactor.wasm" > "$S/wizer.log" 2>&1 || fail "$S/wizer.log" "wizer failed"
echo "wizer: $(( ($(date +%s%N) - start) / 1000000 )) ms"
cp "$S/python-snapshot.wasm" "$OUT/python-numpy-snapshot.wasm"
( cd "$OUT" && sha256sum python-numpy-reactor.wasm python-numpy-snapshot.wasm && ls -l python-numpy-reactor.wasm python-numpy-snapshot.wasm | awk '{ print $5, $9 }' )
