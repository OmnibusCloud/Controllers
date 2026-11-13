#!/usr/bin/env bash
# Runs what build.sh (and snapshot.sh) built the way the sandbox runs a Python task: the pinned
# wasmtime with the exception-handling feature off, the layers under a root mapped as "/", the
# sandbox's environment (PYTHONHOME=/, PYTHONHASHSEED=0, no bytecode written, unbuffered) and its
# argv (-P -B -c <code>). Each probe runs twice; the two outputs must be the same bytes.
#
# Usage: probe.sh <out-dir> <wasmtime>
# Each module is compiled once (timed) and run precompiled, so the wall time and the host's peak
# RSS of a run are the run's. The sandbox's fuel and linear-memory numbers come from the .NET
# probe, not from here.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(cd "$1" && pwd)"
WASMTIME="$2"
NUMPY_ZIP="$(cd "$OUT" && ls numpy-*.zip)"
TMP="$(mktemp -d)"
ROOT="$TMP/root"
mkdir -p "$ROOT/lib"
cp "$OUT/python314.zip" "$OUT/$NUMPY_ZIP" "$ROOT/lib/"

status=0
for module in python-numpy.wasm python-numpy-snapshot.wasm; do
  [ -f "$OUT/$module" ] || continue
  echo "===== $module"
  python3 -I "$HERE/module_facts.py" "$OUT/$module" || status=1
  # Validation with the feature off: an exception-handling instruction anywhere fails here.
  start=$(date +%s%N)
  if ! "$WASMTIME" compile -W exceptions=n -o "$TMP/module.cwasm" "$OUT/$module"; then
    echo "::error::$module does not validate with exceptions off"
    status=1
    continue
  fi
  echo "validates and compiles with exceptions off in $(( ($(date +%s%N) - start) / 1000000 )) ms: $(wc -c < "$TMP/module.cwasm") bytes of native code"
  RUN=("$WASMTIME" run --allow-precompiled -W exceptions=n --dir "$ROOT::/"
    --env PYTHONHOME=/ --env PYTHONHASHSEED=0 --env PYTHONDONTWRITEBYTECODE=1 --env PYTHONUNBUFFERED=1
    --env "PYTHONPATH=/lib/$NUMPY_ZIP" "$TMP/module.cwasm" -P -B -c)
  for probe in hello import program limits throw; do
    code="$(cat "$HERE/probes/$probe.py")"
    echo "== $probe ($module)"
    for attempt in 1 2; do
      start=$(date +%s%N)
      /usr/bin/time -f '%M' -o "$TMP/rss" "${RUN[@]}" "$code" > "$TMP/out$attempt" 2> "$TMP/err$attempt"
      exit_code=$?
      wall=$(( ($(date +%s%N) - start) / 1000000 ))
      echo "[run $attempt: exit $exit_code, ${wall} ms wall, $(tail -n 1 "$TMP/rss") KiB peak RSS]"
    done
    cat "$TMP/out1"
    [ -s "$TMP/err1" ] && { echo "-- stderr"; tail -n 20 "$TMP/err1"; }
    if cmp -s "$TMP/out1" "$TMP/out2" && cmp -s "$TMP/err1" "$TMP/err2"; then
      echo "[the two runs printed the same bytes]"
    else
      echo "::warning::$probe printed different bytes in two runs of $module"
      diff "$TMP/out1" "$TMP/out2" | head -n 10
    fi
    cp "$TMP/out1" "$TMP/$module.$probe.out"
  done
done
# The snapshot must answer as the plain module does (unseeded random numbers aside).
if [ -f "$TMP/python-numpy-snapshot.wasm.program.out" ]; then
  for probe in hello import program; do
    if cmp -s "$TMP/python-numpy.wasm.$probe.out" "$TMP/python-numpy-snapshot.wasm.$probe.out"; then
      echo "snapshot and plain module printed the same for $probe"
    else
      echo "::warning::the snapshot printed something else than the plain module for $probe"
      diff "$TMP/python-numpy.wasm.$probe.out" "$TMP/python-numpy-snapshot.wasm.$probe.out" | head -n 10
    fi
  done
fi
rm -rf "$TMP"
exit $status
