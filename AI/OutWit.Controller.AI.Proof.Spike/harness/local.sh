#!/bin/bash
# PF-0 spike: the workflow's platform job on a machine that is not a hosted runner.
#
#   local.sh <folder> <step> [<step> ...]
#   local.sh <folder> confirm <exports folder> <label>
#
# Steps, in the order of the workflow:
#   setup     the machine, the Lean release, the measuring prelude, the REPL, the core smoke,
#             Mathlib's environment, the start-up of one process
#   fresh     the golden set, one process per item
#   export    stage one of the split: check, export, judge on this machine
#   pool      the golden set through pooled processes
#   one       one pooled process in a fixed order
#   small     the golden set against the smaller environment
#   replay    a slice of the environment through `leanchecker`, one process per module
#   degree    the memory of several pooled processes
#   confirm   stage two of the split on exports that travelled from another machine
#
# Everything the job brings, downloads, unpacks or writes stays under <folder>: the work folder
# (<folder>/work), the results (<folder>/out), the logs, and every cache a tool would otherwise
# put under the home folder (XDG_CACHE_HOME, LAKE_CACHE_DIR, MATHLIB_CACHE_DIR, ELAN_HOME, TMPDIR;
# Python writes no bytecode). HOME is left as it is; git reads no user or system configuration
# and asks no credential helper. Nothing is installed: the release is run from the folder by its
# full path. No time budget is set: every step takes all its items.
#
# The steps of one call run one after another; <folder>/RUNNING holds the process id while they
# do, <folder>/steps.tsv says how each ended, and <folder>/DONE-<step> or FAILED-<step> is left
# behind for a caller that polls.
#
# Settings by environment: PF0_RID (default: by `uname`), PF0_JOBS (processes at a time, 4),
# PF0_PROCESSES (the memory step's most, 6), PF0_MODULES (50), PF0_PYTHON (python3),
# PF0_ITEMS (<folder>/items/items.jsonl), PF0_NICE (10), PF0_BUILD_THREADS (the threads `lake`
# may use while it builds, PF0_JOBS).

set -u

ROOT="${1:?the folder}"
shift
mkdir -p "$ROOT"
ROOT="$(cd "$ROOT" && pwd)"
SPIKE="$(cd "$(dirname "$0")/.." && pwd)"
PY="${PF0_PYTHON:-python3}"
JOBS="${PF0_JOBS:-4}"
PROCESSES="${PF0_PROCESSES:-6}"
MODULES="${PF0_MODULES:-50}"
ITEMS="${PF0_ITEMS:-$ROOT/items/items.jsonl}"
MAX_HEARTBEATS=400000

if [ -z "${PF0_RID:-}" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) PF0_RID=osx-arm64 ;;
    Darwin-x86_64) PF0_RID=osx-x64 ;;
    Linux-x86_64) PF0_RID=linux-x64 ;;
    Linux-aarch64) PF0_RID=linux-arm64 ;;
    *) echo "set PF0_RID"; exit 2 ;;
  esac
fi
RID="$PF0_RID"
OUT="$ROOT/out/results-$RID"
LOGS="$ROOT/logs"
mkdir -p "$OUT" "$LOGS" "$ROOT/work" "$ROOT/cache" "$ROOT/tmp" "$ROOT/elan"

export PF0_WORK="$ROOT/work"
export XDG_CACHE_HOME="$ROOT/cache"
export LAKE_CACHE_DIR="$ROOT/cache/lake"
export MATHLIB_CACHE_DIR="$ROOT/work/mathlib-cache"
export ELAN_HOME="$ROOT/elan"
export TMPDIR="$ROOT/tmp"
export PYTHONDONTWRITEBYTECODE=1
export PYTHONPYCACHEPREFIX="$ROOT/pycache"
export PYTHONUTF8=1
export CURL_HOME="$ROOT"
export GIT_CONFIG_GLOBAL=/dev/null
export GIT_CONFIG_NOSYSTEM=1
export GIT_TERMINAL_PROMPT=0
export GIT_CONFIG_COUNT=1
export GIT_CONFIG_KEY_0=credential.helper
export GIT_CONFIG_VALUE_0=
unset GITHUB_ENV GITHUB_STEP_SUMMARY

renice "${PF0_NICE:-10}" -p $$ >/dev/null 2>&1 || true
echo $$ > "$ROOT/RUNNING"
trap 'rm -f "$ROOT/RUNNING"' EXIT

field() {  # field <record.json> <key>
  "$PY" -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))[sys.argv[2]])' "$1" "$2"
}

run() {  # run <name> <command...>: one command of a step, its output in a log of its own
  local name="$1"
  shift
  local began
  began=$(date +%s)
  echo "=== $name: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  "$@" > "$LOGS/$name.log" 2>&1
  local code=$?
  printf '%s\t%s\t%s\t%s\n' "$name" "$code" "$(( $(date +%s) - began ))" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$ROOT/steps.tsv"
  echo "    exit $code after $(( $(date +%s) - began )) s"
  return $code
}

tools() {  # where the release and the REPL are, once the setup has run
  LEAN_ROOT="$(field "$OUT/toolchain.json" root)" || return 1
  PF0_REPL=""
  if [ -f "$OUT/repl.json" ]; then PF0_REPL="$(field "$OUT/repl.json" binary 2>/dev/null)"; fi
  COMMON=(--lean-path "$PF0_WORK/prelude" --lean-path-file "$PF0_WORK/leanpath.json" --items "$ITEMS" --max-heartbeats "$MAX_HEARTBEATS" --mixed)
}

setup() {
  # While things are fetched, built and unpacked, Lean's and leantar's thread pools are held to
  # the same number of processors the checks will use.
  export LEAN_NUM_THREADS="${PF0_BUILD_THREADS:-$JOBS}"
  export RAYON_NUM_THREADS="$LEAN_NUM_THREADS"
  run machine "$PY" "$SPIKE/harness/machine.py" "$OUT/machine.json" || return 1
  run toolchain "$PY" "$SPIKE/harness/toolchain.py" "$RID" "$PF0_WORK/toolchain" "$OUT/toolchain.json" || return 1
  LEAN_ROOT="$(field "$OUT/toolchain.json" root)" || return 1
  run prelude "$PY" "$SPIKE/harness/smoke.py" prelude --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" || return 1
  run repl "$PY" "$SPIKE/harness/tools.py" repl --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" || return 1
  PF0_REPL="$(field "$OUT/repl.json" binary)" || return 1
  run mathlib "$PY" "$SPIKE/harness/mathlib.py" --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" || return 1
  # The measured steps run with Lean's own number of threads, as on the hosted runners.
  unset LEAN_NUM_THREADS RAYON_NUM_THREADS
  run smoke-core "$PY" "$SPIKE/harness/smoke.py" run --set core --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" --repl "$PF0_REPL"
  run startup "$PY" "$SPIKE/harness/startup.py" --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" || return 1
}

fresh() {
  tools || return 1
  run golden-fresh "$PY" "$SPIKE/harness/check.py" --lean-root "$LEAN_ROOT" "${COMMON[@]}" --import Mathlib --out "$OUT/golden-fresh.jsonl" --work "$PF0_WORK/golden" --jobs "$JOBS" --hard-cap 300
}

export_() {
  tools || return 1
  run split-choose "$PY" "$SPIKE/harness/split.py" choose --items "$ITEMS" --ids "$OUT/split-ids.txt" --accepted 40 --controls 4 || return 1
  run split-export "$PY" "$SPIKE/harness/split.py" export --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --items "$ITEMS" --ids "$OUT/split-ids.txt" --out "$OUT" --exports "$ROOT/out/exports-$RID" --max-heartbeats "$MAX_HEARTBEATS" --paranoid 0
}

pool() {
  tools || return 1
  run golden-pool "$PY" "$SPIKE/harness/pool.py" --repl "$PF0_REPL" --bin "$LEAN_ROOT/bin" "${COMMON[@]}" --import Mathlib --out "$OUT/golden-pool.jsonl" --work "$PF0_WORK/golden-pool" --workers "$JOBS"
}

one() {
  tools || return 1
  run golden-pool-one "$PY" "$SPIKE/harness/pool.py" --repl "$PF0_REPL" --bin "$LEAN_ROOT/bin" "${COMMON[@]}" --import Mathlib --out "$OUT/golden-pool-one.jsonl" --work "$PF0_WORK/golden-pool-one" --workers 1 --limit 150
}

small() {
  tools || return 1
  run golden-small "$PY" "$SPIKE/harness/check.py" --lean-root "$LEAN_ROOT" "${COMMON[@]}" --small-profile --out "$OUT/golden-small.jsonl" --work "$PF0_WORK/golden-small" --jobs "$JOBS" --hard-cap 300
}

replay() {
  tools || return 1
  # Several modules in one `leanchecker` process crash it (the hosted Linux and Windows runners
  # showed that): one process per module here, and the crash is not asked for again.
  run replay "$PY" "$SPIKE/harness/replay.py" --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --out "$OUT" --limit "$MODULES"
}

degree() {
  tools || return 1
  run degree "$PY" "$SPIKE/harness/degree.py" --repl "$PF0_REPL" --bin "$LEAN_ROOT/bin" --lean-path-file "$PF0_WORK/leanpath.json" --lean-path "$PF0_WORK/prelude" --import Mathlib --work "$PF0_WORK/degree" --out "$OUT/degree.json" --max "$PROCESSES" --items "$SPIKE/golden/smoke-mathlib.jsonl"
}

confirm() {  # confirm <exports folder> <label>
  tools || return 1
  local label="$2"
  run "confirm-$label" "$PY" "$SPIKE/harness/split.py" confirm --lean-root "$LEAN_ROOT" --work "$PF0_WORK" --items "$ITEMS" --exports "$1" --out "$ROOT/out/confirm-$label" --label "$label" --max-heartbeats "$MAX_HEARTBEATS" --sandbox
}

finish() {  # finish <step> <exit>
  rm -f "$ROOT/DONE-$1" "$ROOT/FAILED-$1"
  if [ "$2" = "0" ]; then touch "$ROOT/DONE-$1"; else touch "$ROOT/FAILED-$1"; fi
}

if [ "${1:-}" = "confirm" ]; then
  confirm "${2:?the exports folder}" "${3:?a label}"
  code=$?
  finish confirm "$code"
  exit "$code"
fi

worst=0
for step in "$@"; do
  case "$step" in
    setup) setup ;;
    fresh) fresh ;;
    export) export_ ;;
    pool) pool ;;
    one) one ;;
    small) small ;;
    replay) replay ;;
    degree) degree ;;
    *) echo "unknown step: $step"; exit 2 ;;
  esac
  code=$?
  finish "$step" "$code"
  if [ "$code" != "0" ]; then worst=$code; fi
  if [ "$step" = "setup" ] && [ "$code" != "0" ]; then break; fi
done
exit "$worst"
