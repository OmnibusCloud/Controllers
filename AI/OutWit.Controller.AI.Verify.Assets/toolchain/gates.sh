#!/usr/bin/env bash
# The toolchain job's gates for lua.wasm, after build.sh:
#
#   1. Lua's own test suite (portable and soft mode), every file on its own, under the pinned
#      wasmtime command line with the exception-handling feature OFF, as the sandbox runs it.
#      Three files fail for reasons of WASI, the same in the build with exceptions: no locale
#      data (literals), fopen("") opens the preopened root (attrib), no PATH and no temporary
#      files (files). Any other failure fails the gate.
#   2. How deep an error unwinds: the statistics build runs the deepest scenarios (deep.lua)
#      and the suite's deepest files; the high-water mark must stay under half the buffer.
#
# and for qjs.wasm:
#
#   3. QuickJS-NG's own tests (tests/*.js of its sources), exceptions off. A test marked
#      "negative" passes when it ends with the error type it names. Five fail for reasons of
#      WASI, the same with QuickJS's own release build: no Atomics without threads (bug1301,
#      bug1302), no `qjs` global outside its CLI options (test_builtin), no os functions
#      (test_std), no workers (test_worker).
#   4. Recursion: an endless one of every shape (plain, with locals, through map, through
#      toString) is a RangeError the program catches, never a trap; the program the release
#      build ended silently (200 levels with an array a frame) prints its result.
#
# Usage: gates.sh <out-dir> <work-dir> <wasmtime>
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(cd "$1" && pwd)"
WORK="$(cd "$2" && pwd)"
WASMTIME="$3"

# shellcheck disable=SC1091
. "$HERE/pins.env"

FILES="main gc db calls strings literals tpack attrib gengc locals constructs code big cstack nextvar pm utf8 api events vararg closure coroutine goto errors math sort bitwise verybig files heavy"
KNOWN="literals attrib files"
DEEP="index gsub tostring concat format sortcmp parse parsetable load call memory"
DEEP_FILES="calls cstack errors coroutine"
BUFFER=$((256 * 1024))

echo "== Lua's test suite"
TESTS="$WORK/lua-tests.tar.gz"
if [ ! -f "$TESTS" ]; then
  curl -fsSL --retry 3 --max-time 120 -o "$TESTS" "$LUA_TESTS_URL" || { echo "DOWNLOAD FAILED $LUA_TESTS_URL"; exit 1; }
fi
echo "$LUA_TESTS_SHA256  $TESTS" | sha256sum -c - || exit 1
SUITE="$WORK/lua-tests"
rm -rf "$SUITE"
mkdir -p "$SUITE"
tar -xzf "$TESTS" -C "$SUITE" --strip-components=1

FAILED=0
for file in $FILES; do
  run="$WORK/suite-$file"
  rm -rf "$run"
  cp -r "$SUITE" "$run"
  timeout 900 "$WASMTIME" run -W exceptions=n --dir "$run::/" "$OUT/lua.wasm" \
    -e "_soft=true _port=true _nomsg=true" "$file.lua" > "$WORK/suite-$file.log" 2>&1
  code=$?
  if [ "$code" -eq 0 ]; then
    echo "pass   $file"
  elif [[ " $KNOWN " == *" $file "* ]]; then
    echo "known  $file: $(grep -m1 -E 'assertion failed|error' "$WORK/suite-$file.log" | cut -c1-120)"
  else
    echo "FAIL   $file (exit $code): $(tail -3 "$WORK/suite-$file.log" | tr '\n' ' ' | cut -c1-300)"
    FAILED=1
  fi
  rm -rf "$run"
done
[ "$FAILED" -eq 0 ] || { echo "::error::lua.wasm fails Lua's own test suite"; exit 1; }

echo "== how deep an error unwinds (statistics build, buffer $BUFFER bytes)"
STATS="$WORK/gates/lua-stats.wasm"
[ -f "$STATS" ] || { echo "no statistics build at $STATS"; exit 1; }
HIGH=0
# The statistics build prints "high_water=N" at exit; the deepest of a run, or 0.
water_of() {
  local water
  water="$(grep -oE 'high_water=[0-9]+' "$1" | cut -d= -f2 | sort -n | tail -1)"
  echo "${water:-0}"
}
for scenario in $DEEP; do
  log="$WORK/deep-$scenario.log"
  timeout 300 "$WASMTIME" run -W exceptions=n --env LUA_ASYNCIFY_HWM=1 --dir "$HERE/gates::/" \
    "$STATS" deep.lua "$scenario" > "$log" 2>&1
  water="$(water_of "$log")"
  [ "$water" -gt "$HIGH" ] && HIGH="$water"
  echo "deep   $scenario: $(head -1 "$log" | cut -c1-80); high water $water"
done
for file in $DEEP_FILES; do
  run="$WORK/deep-suite-$file"
  log="$WORK/deep-suite-$file.log"
  rm -rf "$run"
  cp -r "$SUITE" "$run"
  timeout 900 "$WASMTIME" run -W exceptions=n --env LUA_ASYNCIFY_HWM=1 --dir "$run::/" \
    "$STATS" -e "_soft=true _port=true _nomsg=true" "$file.lua" > "$log" 2>&1
  water="$(water_of "$log")"
  [ "$water" -gt "$HIGH" ] && HIGH="$water"
  echo "suite  $file: high water $water"
  rm -rf "$run"
done
echo "deepest unwind: $HIGH bytes of $BUFFER"
[ "$HIGH" -gt 0 ] || { echo "::error::the statistics build reported nothing"; exit 1; }
[ "$HIGH" -lt $((BUFFER / 2)) ] || { echo "::error::the deepest unwind takes half the buffer or more"; exit 1; }

echo "== QuickJS-NG's own tests"
QJS_TESTS="$WORK/quickjs-$QUICKJS_VERSION/tests"
[ -d "$QJS_TESTS" ] || { echo "no QuickJS sources at $QJS_TESTS"; exit 1; }
QJS_SKIP=" assert.js empty.js fixture_cyclic_import.js fixture_string_exports.js microbench.js test_worker_module.js "
QJS_KNOWN=" bug1301.js bug1302.js test_builtin.js test_std.js test_worker.js "
FAILED=0
for path in "$QJS_TESTS"/*.js; do
  name="$(basename "$path")"
  [[ "$QJS_SKIP" == *" $name "* ]] && continue
  log="$WORK/qjs-$name.log"
  timeout 300 "$WASMTIME" run -W exceptions=n --dir "$QJS_TESTS::/" "$OUT/qjs.wasm" "$name" > "$log" 2>&1
  code=$?
  negative="$(sed -n '/negative:/,/---\*\//p' "$path" | grep -oE 'type: *[A-Za-z]+' | head -1 | sed 's/type: *//')"
  if [ -n "$negative" ]; then
    if [ "$code" -ne 0 ] && grep -q "^$negative" "$log"; then echo "pass   $name (ends with $negative, as it must)"; continue; fi
  elif [ "$code" -eq 0 ]; then
    echo "pass   $name"; continue
  fi
  if [[ "$QJS_KNOWN" == *" $name "* ]]; then
    echo "known  $name: $(grep -v '^\s*at ' "$log" | grep -v '^\s*$' | head -1 | cut -c1-100)"
  else
    echo "FAIL   $name (exit $code): $(grep -v '^\s*$' "$log" | tail -2 | tr '\n' ' ' | cut -c1-200)"
    FAILED=1
  fi
done
[ "$FAILED" -eq 0 ] || { echo "::error::qjs.wasm fails QuickJS's own tests"; exit 1; }

echo "== QuickJS: recursion"
for shape in \
  'function f(n){ return 1 + f(n + 1); }' \
  'function f(n){ const a = [n, n + 1, n + 2]; return a[0] + f(n + 1); }' \
  'function f(n){ return [n].map(x => f(x + 1))[0]; }' \
  'function f(n){ return String({ toString(){ return "x" + f(n + 1); } }); }'; do
  out="$("$WASMTIME" run -W exceptions=n "$OUT/qjs.wasm" -e "$shape try { f(0); console.log('returned'); } catch (e) { console.log('caught ' + e.name); }" 2>&1)"
  code=$?
  echo "  $(echo "$shape" | cut -c1-60): exit $code, $out" | cut -c1-140
  [ "$code" -eq 0 ] && [ "$out" = "caught RangeError" ] || { echo "::error::an endless recursion is not a RangeError the program catches"; exit 1; }
done
out="$("$WASMTIME" run -W exceptions=n "$OUT/qjs.wasm" -e 'function f(n){ const a = [n, n + 1, n + 2]; return n === 0 ? 0 : a[0] + f(n - 1); } console.log(f(200)); console.log("after");' 2>&1)"
echo "  200 levels with an array a frame: $(echo "$out" | tr '\n' ' ')"
[ "$out" = "$(printf '20100\nafter')" ] || { echo "::error::a recursion of 200 levels does not print its result"; exit 1; }
echo "gates passed"
