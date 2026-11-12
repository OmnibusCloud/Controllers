#!/usr/bin/env bash
# Builds the modules the Verify runtime set cannot take ready-made, from pinned sources with
# a pinned toolchain, on Linux x64:
#
#   lua.wasm      the stock Lua interpreter (lua.org sources, lua-wasi.h in front of each file)
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
  local url="$1" pin="$2" file="$3" seen
  [ -f "$file" ] || curl -fsSL --retry 3 -o "$file" "$url"
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
fetch "$LUA_URL" "$LUA_SHA256" "$WORK/lua.tar.gz"

SDK="$WORK/wasi-sdk"
if [ ! -x "$SDK/bin/clang" ]; then
  mkdir -p "$SDK"
  tar -xzf "$WORK/wasi-sdk.tar.gz" -C "$SDK" --strip-components=1
fi
"$SDK/bin/clang" --version | head -1

LUA="$WORK/lua-$LUA_VERSION"
rm -rf "$LUA"
tar -xzf "$WORK/lua.tar.gz" -C "$WORK"

echo "== lua.wasm (Lua $LUA_VERSION)"
# Every source of the interpreter except the compiler's own main. Lua raises errors with
# longjmp; in WebAssembly that is the exception-handling proposal (-wasm-enable-sjlj, the
# standard encoding, libsetjmp). The signal and process-clock emulations are wasi-libc's
# own; the stack is 1 MiB because Lua's C recursion (200 nested calls) does not fit the
# linker's default 64 KiB.
(
  cd "$LUA/src"
  SOURCES="$(ls *.c | grep -v '^luac\.c$' | sort | tr '\n' ' ')"
  # shellcheck disable=SC2086
  "$SDK/bin/clang" --target=wasm32-wasip1 --sysroot="$SDK/share/wasi-sysroot" \
    -O2 -DNDEBUG \
    -D_WASI_EMULATED_SIGNAL -D_WASI_EMULATED_PROCESS_CLOCKS \
    -include "$HERE/lua-wasi.h" \
    -mllvm -wasm-enable-sjlj -mllvm -wasm-use-legacy-eh=false \
    -ffile-prefix-map="$WORK"=. -ffile-prefix-map="$HERE"=. \
    $SOURCES \
    -lwasi-emulated-signal -lwasi-emulated-process-clocks -lsetjmp -lm \
    -Wl,-z,stack-size=1048576 -Wl,--strip-debug \
    -o "$OUT/lua.wasm"
)

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
