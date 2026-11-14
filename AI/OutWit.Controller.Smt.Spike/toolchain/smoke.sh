#!/usr/bin/env bash
# SM-0 spike: the Z3 module under the pinned wasmtime command line, with the exception-handling
# proposal on and every other option as the sandbox's engine has it. Each case prints what Z3
# answered; the case fails when the expected words are missing.
#
# Usage: smoke.sh <wasmtime> <z3.wasm>
set -uo pipefail

WASMTIME="$1"
MODULE="$2"
FAILED=0

# The sandbox's engine options (WasmHostEngineConfig) with exceptions on; a refused grow is
# no trap (the binding cannot ask for one). An 8 MiB wasm stack needs a bigger main-thread
# stack under the command line than Linux's default.
OPTIONS=(-W exceptions=y -W fuel=100000000000 -W max-memory-size=1073741824 -W trap-on-grow-failure=n
         -W nan-canonicalization=y -W max-wasm-stack=8388608 -W threads=n -W shared-memory=n -W gc=n
         -W memory64=n -W multi-memory=n -W component-model=n -W relaxed-simd-deterministic=y)
ulimit -s 65536 2> /dev/null || true

case_() {
  local name="$1" expect="$2" input="$3"
  shift 3
  local output code
  output="$(printf '%s\n' "$input" | "$WASMTIME" run "${OPTIONS[@]}" "$MODULE" "$@" 2>&1)"
  code=$?
  echo "--- $name (exit $code)"
  echo "$output" | head -20
  if ! grep -q -F -- "$expect" <<< "$output"; then
    echo "FAILED: expected '$expect'"
    FAILED=1
  fi
}

case_ version "Z3 version 5.4.1" "" -version
case_ sat-model "(define-fun x () Int" \
  '(set-option :produce-models true)(declare-const x Int)(assert (> x 41))(assert (< x 43))(check-sat)(get-model)' -in -smt2
case_ unsat "unsat" '(declare-const p Bool)(assert (and p (not p)))(check-sat)' -in -smt2
# A parse error is a C++ exception inside Z3, caught by the command loop: the session goes on.
case_ error-then-continue "after-the-error" \
  '(assert (> y 2))(echo "after-the-error")' -in -smt2
# The resource limit stops the check by an exception too; the reason names the limit.
case_ rlimit-unknown "unknown" \
  '(set-option :rlimit 2000)(declare-const a (_ BitVec 64))(declare-const b (_ BitVec 64))(assert (= (bvmul a b) #x00000000ffffffc5))(assert (bvugt a #x0000000000000001))(assert (bvugt b #x0000000000000001))(check-sat)(get-info :reason-unknown)(get-info :all-statistics)' -in -smt2
# Out of memory: Z3's own ceiling (memory_max_size) ends the process with its memout code. A
# 4096-bit multiplication bit-blasts into millions of clauses.
ONE="#x$(printf '0%.0s' $(seq 1 1023))1"
case_ memout "out of memory"   "(declare-const a (_ BitVec 4096))(declare-const b (_ BitVec 4096))(assert (= (bvmul a b) (bvadd a $ONE)))(check-sat)" -in -smt2 -memory:64

if [ "$FAILED" -ne 0 ]; then
  echo "== a smoke case failed"
  exit 1
fi
echo "== every smoke case answered as expected"
