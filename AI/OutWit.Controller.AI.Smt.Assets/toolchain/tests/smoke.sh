#!/usr/bin/env bash
# The solver module under the pinned wasmtime command line, with the exception-handling
# proposal on and every other option as the sandbox's engine has it (AI/Shared/Wasm). Each
# case prints what Z3 answered and how it ended; a case fails when it ends with another exit
# code than expected or a text it must answer is missing.
#
# Usage: smoke.sh <wasmtime> <z3.wasm>
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1091
. "$HERE/../pins.env"

[ "$#" -eq 2 ] || { echo "usage: smoke.sh <wasmtime> <z3.wasm>"; exit 64; }
WASMTIME="$1"
MODULE="$2"
FAILED=0

# The engine's options in the sandbox, with exceptions on: fuel, a memory cap (a refused grow
# is no trap: the guest sees it fail), canonical NaNs, 8 MiB of stack, one 32-bit memory, no
# threads, no GC, no components, relaxed SIMD in its deterministic mode. An 8 MiB wasm stack
# needs a bigger main-thread stack under the command line than Linux's default.
OPTIONS=(-W exceptions=y -W fuel=100000000000 -W max-memory-size=1073741824 -W trap-on-grow-failure=n
         -W nan-canonicalization=y -W max-wasm-stack=8388608 -W threads=n -W shared-memory=n -W gc=n
         -W memory64=n -W multi-memory=n -W component-model=n -W relaxed-simd-deterministic=y)
ulimit -s 65536 2> /dev/null || true

# check <name> <exit code> <input> <text>... -- <z3 argument>...
# A text is looked for anywhere in the output; one that starts with = must be a whole line.
check() {
  local name="$1" expected="$2" input="$3"
  shift 3
  local texts=()
  while [ "$1" != "--" ]; do
    texts+=("$1")
    shift
  done
  shift
  local output code text
  output="$(printf '%s\n' "$input" | "$WASMTIME" run "${OPTIONS[@]}" "$MODULE" "$@" 2>&1)"
  code=$?
  echo "--- $name (exit $code)"
  echo "$output" | head -20
  if [ "$code" -ne "$expected" ]; then
    echo "FAILED: expected exit $expected"
    FAILED=1
  fi
  for text in "${texts[@]}"; do
    case "$text" in
      =*) grep -q -x -F -- "${text#=}" <<< "$output" ;;
      *) grep -q -F -- "$text" <<< "$output" ;;
    esac || { echo "FAILED: expected '$text'"; FAILED=1; }
  done
}

check version 0 "" "Z3 version $Z3_VERSION" -- -version
check sat-model 0 \
  '(set-option :produce-models true)(declare-const x Int)(assert (> x 41))(assert (< x 43))(check-sat)(get-model)' \
  "=sat" "(define-fun x () Int" "42)" -- -in -smt2
check unsat 0 '(declare-const p Bool)(assert (and p (not p)))(check-sat)' "=unsat" -- -in -smt2
# A parse error is a C++ exception inside Z3, caught by the command loop: the session goes on,
# and the process ends with 1 for the error it reported.
check error-then-continue 1 '(assert (> y 2))(echo "after-the-error")' \
  "unknown constant y" "=after-the-error" -- -in -smt2
# The resource limit stops the check by an exception too; the answer is unknown and the reason
# names the limit.
check rlimit-unknown 0 \
  '(set-option :rlimit 2000)(declare-const a (_ BitVec 64))(declare-const b (_ BitVec 64))(assert (= (bvmul a b) #x00000000ffffffc5))(assert (bvugt a #x0000000000000001))(assert (bvugt b #x0000000000000001))(check-sat)(get-info :reason-unknown)(get-info :all-statistics)' \
  "=unknown" '(:reason-unknown "max. resource limit exceeded")' -- -in -smt2
# Out of memory: Z3's own ceiling (-memory, in megabytes) ends the process with its memout
# code, 101. A 4096-bit multiplication bit-blasts into millions of clauses.
ONE="#x$(printf '0%.0s' $(seq 1 1023))1"
check memout 101 \
  "(declare-const a (_ BitVec 4096))(declare-const b (_ BitVec 4096))(assert (= (bvmul a b) (bvadd a $ONE)))(check-sat)" \
  '(error "out of memory")' -- -in -smt2 -memory:64

if [ "$FAILED" -ne 0 ]; then
  echo "== a smoke case failed"
  exit 1
fi
echo "== every smoke case answered as expected"
