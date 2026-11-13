#!/usr/bin/env bash
# Runs what build.sh built the way the sandbox runs a Python task: the pinned wasmtime with the
# exception-handling feature off, the layers under a root mapped as "/", the sandbox's
# environment (PYTHONHOME=/, PYTHONHASHSEED=0, no bytecode written, unbuffered) and its argv
# (-P -B -c <code>). Each probe runs twice; the two outputs must be the same bytes.
#
# Usage: probe.sh <out-dir> <wasmtime>
# Wall time and peak RSS are the host process's (the module's compile included); the sandbox's
# fuel and linear-memory numbers come from the .NET probe, not from here.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(cd "$1" && pwd)"
WASMTIME="$2"
NUMPY_ZIP="$(cd "$OUT" && ls numpy-*.zip)"

echo "== the module"
python3 -I "$HERE/module_facts.py" "$OUT/python-numpy.wasm" || exit 1
# Validation with the feature off: an exception-handling instruction anywhere fails here.
"$WASMTIME" compile -W exceptions=n -o "$OUT/../python-numpy.cwasm" "$OUT/python-numpy.wasm" || { echo "::error::the module does not validate with exceptions off"; exit 1; }
echo "validates and compiles with exceptions off: $(wc -c < "$OUT/../python-numpy.cwasm") bytes of native code"

ROOT="$(mktemp -d)"
mkdir -p "$ROOT/lib"
cp "$OUT/python314.zip" "$OUT/$NUMPY_ZIP" "$ROOT/lib/"

RUN=("$WASMTIME" run -W exceptions=n --dir "$ROOT::/"
  --env PYTHONHOME=/ --env PYTHONHASHSEED=0 --env PYTHONDONTWRITEBYTECODE=1 --env PYTHONUNBUFFERED=1
  --env "PYTHONPATH=/lib/$NUMPY_ZIP" "$OUT/python-numpy.wasm" -P -B -c)

status=0
for probe in hello import program limits throw; do
  code="$(cat "$HERE/probes/$probe.py")"
  echo "== $probe"
  for attempt in 1 2; do
    start=$(date +%s%N)
    /usr/bin/time -f '%M' -o "$ROOT/rss" "${RUN[@]}" "$code" > "$ROOT/out$attempt" 2> "$ROOT/err$attempt"
    exit_code=$?
    wall=$(( ($(date +%s%N) - start) / 1000000 ))
    echo "[run $attempt: exit $exit_code, ${wall} ms wall, $(tail -n 1 "$ROOT/rss") KiB peak RSS]"
  done
  cat "$ROOT/out1"
  [ -s "$ROOT/err1" ] && { echo "-- stderr"; tail -n 20 "$ROOT/err1"; }
  if cmp -s "$ROOT/out1" "$ROOT/out2" && cmp -s "$ROOT/err1" "$ROOT/err2"; then
    echo "[the two runs printed the same bytes]"
  else
    echo "::warning::$probe printed different bytes in two runs"
    diff "$ROOT/out1" "$ROOT/out2" | head -n 10
  fi
done
rm -rf "$ROOT"
exit $status
