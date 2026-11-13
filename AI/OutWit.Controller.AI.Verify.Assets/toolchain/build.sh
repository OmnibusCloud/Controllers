#!/usr/bin/env bash
# Builds the modules the Verify runtime set cannot take ready-made, from pinned sources with
# a pinned toolchain, on Linux x64:
#
#   lua.wasm      the stock Lua interpreter (lua.org sources unchanged, lua-wasi.h and
#                 lua-asyncify.h in front of each file, errors by an asyncify unwind), with
#                 LICENSE-lua.txt beside it
#   qjs.wasm      QuickJS-NG (its sources, quickjs-wasi.patch, the C stack below the data),
#                 with LICENSE-quickjs-ng.txt beside it
#   sum.wasm      the C fixture of the raw-WASI runtime tests
#   words.wasm    the Rust fixture of the raw-WASI runtime tests
#
# Usage: build.sh <out-dir> [<work-dir>]
# Prints the SHA-256 of everything it downloads and of everything it builds. The build is
# meant to be reproducible: the same pins give the same bytes.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="${2:-$(mktemp -d)}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"

# shellcheck disable=SC1091
. "$HERE/pins.env"

UNPINNED=0

fetch() {
  local url="$1" pin="$2" file="$3" mirror="${4:-}" seen
  if [ ! -f "$file" ]; then
    if ! curl -fsSL --retry 2 --max-time 300 -o "$file" "$url"; then
      [ -n "$mirror" ] || { echo "DOWNLOAD FAILED $url"; exit 1; }
      echo "no answer from $url, taking the mirror"
      curl -fsSL --retry 3 --max-time 300 -o "$file" "$mirror"
    fi
  fi
  seen="$(sha256sum "$file" | cut -d' ' -f1)"
  if [ -z "$pin" ]; then
    echo "UNPINNED $seen  $(basename "$file")"
    UNPINNED=1
  elif [ "$seen" != "$pin" ]; then
    echo "HASH MISMATCH $(basename "$file"): expected $pin, got $seen"
    exit 1
  else
    echo "pinned   $seen  $(basename "$file")"
  fi
}

echo "== downloads"
fetch "$WASI_SDK_URL" "$WASI_SDK_SHA256" "$WORK/wasi-sdk.tar.gz"
fetch "$LUA_URL" "$LUA_SHA256" "$WORK/lua.tar.gz" "$LUA_MIRROR_URL"
fetch "$BINARYEN_URL" "$BINARYEN_SHA256" "$WORK/binaryen.tar.gz"
fetch "$QUICKJS_URL" "$QUICKJS_SHA256" "$WORK/quickjs.tar.gz"

SDK="$WORK/wasi-sdk"
if [ ! -x "$SDK/bin/clang" ]; then
  mkdir -p "$SDK"
  tar -xzf "$WORK/wasi-sdk.tar.gz" -C "$SDK" --strip-components=1
fi
"$SDK/bin/clang" --version | head -1

BINARYEN="$WORK/binaryen"
if [ ! -x "$BINARYEN/bin/wasm-opt" ]; then
  mkdir -p "$BINARYEN"
  tar -xzf "$WORK/binaryen.tar.gz" -C "$BINARYEN" --strip-components=1
fi
"$BINARYEN/bin/wasm-opt" --version

LUA="$WORK/lua-$LUA_VERSION"
rm -rf "$LUA"
tar -xzf "$WORK/lua.tar.gz" -C "$WORK"

echo "== lua.wasm (Lua $LUA_VERSION)"
# Every source of the interpreter except the compiler's own main, Lua's files unchanged.
# Lua raises an error by a jump to the nearest protected call (LUAI_THROW / LUAI_TRY in
# ldo.c). lua-asyncify.h makes that jump an asyncify unwind that stops at the protected call
# and is never rewound, so the module needs no exception-handling feature and runs in the
# sandbox as it is. It relies on LUAI_TRY being used once, around "(*f)(L, ud);": checked here.
# The signal and process-clock emulations are wasi-libc's own; the stack is 1 MiB because
# Lua's C recursion (200 nested calls) does not fit the linker's default 64 KiB.
grep -q '^    (\*f)(L, ud);$' "$LUA/src/ldo.c" || { echo "ldo.c: the LUAI_TRY block is not the expected one"; exit 1; }
[ "$(grep -c 'LUAI_TRY(L, ' "$LUA/src/ldo.c")" = 1 ] || { echo "ldo.c: LUAI_TRY is not used exactly once"; exit 1; }

# lua_raw <output> [extra clang options]: the interpreter linked with its function names,
# which wasm-opt needs for the remove-list (it strips them afterwards). luaD_throw is declared
# not to return, and it does return now: into the unwind, which leaves every frame above it at
# once. clang's warning about that is expected and silenced (-Wno-invalid-noreturn).
lua_raw() {
  local output="$1"; shift
  (
    cd "$LUA/src"
    SOURCES="$(ls *.c | grep -v '^luac\.c$' | sort | tr '\n' ' ')"
    "$SDK/bin/clang" --target=wasm32-wasip1 -c "$HERE/lua-asyncify-sp.S" -o "$WORK/lua-asyncify-sp.o"
    # shellcheck disable=SC2086
    "$SDK/bin/clang" --target=wasm32-wasip1 --sysroot="$SDK/share/wasi-sysroot" \
      -O2 -DNDEBUG -Wno-invalid-noreturn \
      -D_WASI_EMULATED_SIGNAL -D_WASI_EMULATED_PROCESS_CLOCKS \
      -include "$HERE/lua-wasi.h" -include "$HERE/lua-asyncify.h" "$@" \
      -ffile-prefix-map="$WORK"=. -ffile-prefix-map="$HERE"=. \
      $SOURCES "$HERE/lua-asyncify.c" "$WORK/lua-asyncify-sp.o" \
      -lwasi-emulated-signal -lwasi-emulated-process-clocks -lm \
      -Wl,-z,stack-size=1048576 \
      -o "$output"
  )
}

# Asyncify instruments what can reach the unwind (WASI's imports never do) except the
# protected call itself; the asyncify_* exports it adds are removed (the host calls _start).
ASYNCIFY=(--asyncify --pass-arg=asyncify-ignore-imports --pass-arg=asyncify-removelist@lua_asyncify_call
          -O2 --remove-exports --pass-arg=remove-exports@asyncify_* --remove-unused-module-elements
          --strip-debug --strip-producers)

lua_raw "$WORK/lua.raw.wasm"
"$BINARYEN/bin/wasm-opt" "$WORK/lua.raw.wasm" "${ASYNCIFY[@]}" -o "$OUT/lua.wasm"

# The licence the module ships with: the notice at the end of lua.h, as it is.
sed -n '/^\* Copyright (C) 1994/,/SOFTWARE OR THE USE OR OTHER DEALINGS/p' "$LUA/src/lua.h" | sed 's/^\* \{0,1\}//' > "$OUT/LICENSE-lua.txt"
[ "$(wc -l < "$OUT/LICENSE-lua.txt")" -ge 15 ] || { echo "lua.h: the licence notice was not found"; exit 1; }

# The gate's build (gates.sh), not shipped: the same module counting how deep its unwinds go.
mkdir -p "$WORK/gates"
lua_raw "$WORK/gates/lua-stats.raw.wasm" -DLUA_ASYNCIFY_STATS
"$BINARYEN/bin/wasm-opt" "$WORK/gates/lua-stats.raw.wasm" "${ASYNCIFY[@]}" -o "$WORK/gates/lua-stats.wasm"

echo "== qjs.wasm (QuickJS-NG $QUICKJS_VERSION)"
# The command line and the engine, QuickJS-NG's files with one patch (quickjs-wasi.patch: the
# stack limit back on, at 128 KiB). The C stack is 1 MiB below the data (--stack-first), so an
# overflow traps at address 0 instead of writing over the data. With 128 KiB a recursion too
# deep is JavaScript's RangeError before the engine's own native stack (512 KiB) runs out, for
# plain recursion (about 300 levels) and through native callbacks (map: about 100); measured on
# x86-64 and checked on every platform by the Verify tests.
QJS="$WORK/quickjs-$QUICKJS_VERSION"
rm -rf "$QJS"
tar -xzf "$WORK/quickjs.tar.gz" -C "$WORK"
( cd "$QJS" && patch -p1 --no-backup-if-mismatch -s < "$HERE/quickjs-wasi.patch" )
(
  cd "$QJS"
  "$SDK/bin/clang" --target=wasm32-wasip1 --sysroot="$SDK/share/wasi-sysroot" \
    -O2 -D_GNU_SOURCE -D_WASI_EMULATED_PROCESS_CLOCKS -D_WASI_EMULATED_SIGNAL \
    -DJS_WASI_STACK_SIZE=131072 -Wno-unknown-pragmas \
    -ffile-prefix-map="$WORK"=. \
    dtoa.c libregexp.c libunicode.c quickjs.c quickjs-libc.c qjs.c gen/repl.c gen/standalone.c \
    -lwasi-emulated-process-clocks -lwasi-emulated-signal -lm \
    -Wl,--stack-first -Wl,-z,stack-size=1048576 -Wl,--strip-debug \
    -o "$WORK/qjs.raw.wasm"
)
# The producers section names the compiler's own build ("23.1.0-wasi-sdk" here, "23.1.0-rc3"
# in wasi-sdk's Windows build); without it the module is the same bytes wherever it is built.
"$SDK/bin/llvm-objcopy" --remove-section=producers "$WORK/qjs.raw.wasm" "$OUT/qjs.wasm"
cp "$QJS/LICENSE" "$OUT/LICENSE-quickjs-ng.txt"

echo "== sum.wasm (C)"
"$SDK/bin/clang" --target=wasm32-wasip1 --sysroot="$SDK/share/wasi-sysroot" \
  -O2 -ffile-prefix-map="$HERE"=. \
  "$HERE/fixtures/sum.c" -Wl,--strip-debug -o "$OUT/sum.wasm"

echo "== words.wasm (Rust $RUST_VERSION)"
if command -v rustup > /dev/null 2>&1; then
  rustup toolchain install "$RUST_VERSION" --profile minimal --target wasm32-wasip1 > /dev/null 2>&1
  rustup run "$RUST_VERSION" rustc --version
  rustup run "$RUST_VERSION" rustc --target wasm32-wasip1 --edition 2021 \
    -C opt-level=s -C strip=symbols -C codegen-units=1 -C panic=abort \
    --remap-path-prefix "$HERE"=. \
    "$HERE/fixtures/words.rs" -o "$OUT/words.wasm"
else
  echo "rustup not found: words.wasm skipped"
fi

echo "== built"
( cd "$OUT" && sha256sum *.wasm && ls -l *.wasm | awk '{ print $5, $9 }' )

echo "== pins"
if [ "$UNPINNED" -ne 0 ]; then
  echo "some downloads are not pinned (see UNPINNED above)"
  exit 3
fi
echo "every download matched its pin"
