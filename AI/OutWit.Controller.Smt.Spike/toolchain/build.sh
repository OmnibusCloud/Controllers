#!/usr/bin/env bash
# SM-0 spike: Z3 built for wasm32-wasip1, the module the SMT controller would run in the shared
# wasm sandbox host. Linux x64, pinned toolchain (pins.env), meant to be reproducible: the same
# pins give the same bytes from any folder.
#
#   z3.wasm          the z3 command line (src/shell), single-threaded, no GMP, C++ exceptions
#                    as wasm exceptions (the standard exnref proposal), 8 MiB shadow stack below
#                    the data, memory up to the wasm32 ceiling of 4 GiB
#   LICENSE-z3.txt   Z3's licence (MIT), as it is in the source
#   build-info.txt   versions, flags, imports, sizes
#   SHA256SUMS
#
# Usage: build.sh <out-dir> [<work-dir>]
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
  [ -f "$file" ] || curl -fsSL --retry 3 --max-time 600 -o "$file" "$url"
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
fetch "$Z3_SOURCE_URL" "$Z3_SOURCE_SHA256" "$WORK/z3-source.tar.gz"

SDK="$WORK/wasi-sdk"
if [ ! -x "$SDK/bin/clang" ]; then
  mkdir -p "$SDK"
  tar -xzf "$WORK/wasi-sdk.tar.gz" -C "$SDK" --strip-components=1
fi
"$SDK/bin/clang" --version | head -1
cmake --version | head -1
python3 --version

# The source under a fixed name, whatever the archive's top folder is called.
SRC="$WORK/z3"
rm -rf "$SRC"
mkdir -p "$SRC"
tar -xzf "$WORK/z3-source.tar.gz" -C "$SRC" --strip-components=1
echo "z3 source: $(head -1 "$SRC/scripts/VERSION.txt")"

echo "== shims"
# The scoped timer (threads, pthread_atfork) gives way to a timer that never fires; the
# original must be the one the shim was written against.
seen="$(sha256sum "$SRC/src/util/scoped_timer.cpp" | cut -d' ' -f1)"
[ "$seen" = "$Z3_SCOPED_TIMER_SHA256" ] || { echo "src/util/scoped_timer.cpp changed ($seen): review the shim"; exit 1; }
cp "$HERE/wasi/scoped_timer.cpp" "$SRC/src/util/scoped_timer.cpp"
echo "  src/util/scoped_timer.cpp  <- toolchain/wasi/scoped_timer.cpp"

# Flags.
#  -fwasm-exceptions -mllvm -wasm-use-legacy-eh=false : C++ exceptions as the standard wasm
#      exception-handling proposal (wasi-sdk's CppExceptions.md); the same at link time picks
#      the sysroot's libc++/libc++abi built with exceptions, -lunwind their unwinder.
#  _WASI_EMULATED_*: wasi-libc's emulations of signal() (the shell installs a Ctrl-C
#      handler), clock() (statistics) and getpid() (never reached; debug.cpp).
#  NO_Z3_DEBUGGER: an assertion never shells out to gdb (system() does not exist in WASI).
#  -ffile-prefix-map: __FILE__ in Z3's assertion messages names no folder of the build.
CFLAGS_COMMON="-fwasm-exceptions -mllvm -wasm-use-legacy-eh=false -D_WASI_EMULATED_SIGNAL -D_WASI_EMULATED_PROCESS_CLOCKS -D_WASI_EMULATED_GETPID -DNO_Z3_DEBUGGER -ffile-prefix-map=$WORK=."
#  stack: 8 MiB of shadow stack (what a program has on Linux), placed first, so that an
#      overflow runs below address 0 and traps instead of writing over the data.
#  memory: the wasm32 ceiling, stated by the module; the host's cap is lower.
LDFLAGS_COMMON="-fwasm-exceptions -Wl,-z,stack-size=8388608 -Wl,--stack-first -Wl,--max-memory=4294967296 -Wl,--strip-debug"
LIBS_COMMON="-lunwind -lwasi-emulated-signal -lwasi-emulated-process-clocks -lwasi-emulated-getpid"

BUILD="$WORK/build"
rm -rf "$BUILD"
mkdir -p "$BUILD"

echo "== configure"
cmake -S "$SRC" -B "$BUILD" -G "Unix Makefiles" \
  -DCMAKE_TOOLCHAIN_FILE="$SDK/share/cmake/wasi-sdk-p1.cmake" \
  -DWASI_SDK_PREFIX="$SDK" \
  -DCMAKE_SYSROOT="$SDK/share/wasi-sysroot" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_C_FLAGS="$CFLAGS_COMMON" \
  -DCMAKE_CXX_FLAGS="$CFLAGS_COMMON" \
  -DCMAKE_EXE_LINKER_FLAGS="$LDFLAGS_COMMON" \
  -DCMAKE_CXX_STANDARD_LIBRARIES="$LIBS_COMMON" \
  -DCMAKE_C_STANDARD_LIBRARIES="$LIBS_COMMON" \
  -DPython3_EXECUTABLE="$(command -v python3)" \
  -DBUILD_SHARED_LIBS=OFF \
  -DZ3_SINGLE_THREADED=ON \
  -DZ3_POLLING_TIMER=OFF \
  -DZ3_USE_LIB_GMP=OFF \
  -DZ3_BUILD_EXECUTABLE=ON \
  -DZ3_BUILD_TEST_EXECUTABLES=OFF \
  -DZ3_ENABLE_EXAMPLE_TARGETS=OFF \
  -DZ3_BUILD_PYTHON_BINDINGS=OFF \
  -DZ3_BUILD_DOTNET_BINDINGS=OFF \
  -DZ3_BUILD_JAVA_BINDINGS=OFF \
  -DZ3_BUILD_DOCUMENTATION=OFF \
  -DZ3_INCLUDE_GIT_HASH=OFF \
  -DZ3_INCLUDE_GIT_DESCRIBE=OFF \
  -DZ3_LINK_TIME_OPTIMIZATION=OFF \
  -DZ3_ENABLE_CFI=OFF \
  -DZ3_ENABLE_TRACING_FOR_NON_DEBUG=OFF

echo "== build ($(nproc) jobs)"
# -k: on a failure, every translation unit that fails is reported in one run.
if ! make -C "$BUILD" -k -j"$(nproc)" shell > "$WORK/make.log" 2>&1; then
  grep -n -E "error:|undefined symbol|Error [0-9]" "$WORK/make.log" | head -200
  echo "== the build failed (the last lines of make.log follow)"
  tail -50 "$WORK/make.log"
  exit 1
fi
grep -c -E "warning:" "$WORK/make.log" | sed 's/^/warnings: /' || true

LINKED="$BUILD/z3.wasm"
[ -f "$LINKED" ] || LINKED="$BUILD/z3"
[ -f "$LINKED" ] || { echo "no z3 module in $BUILD"; ls -la "$BUILD"; exit 1; }

# The producers section names the compiler's own build; without it the module is the same
# bytes wherever it is built (Verify's toolchain does the same).
"$SDK/bin/llvm-objcopy" --remove-section=producers "$LINKED" "$OUT/z3.wasm"

# No folder of this build may be in the module.
for folder in "$WORK" "$HERE"; do
  if grep -q -a -F "$folder" "$OUT/z3.wasm"; then
    echo "the module names a folder of the build: $folder"
    exit 1
  fi
done

cp "$SRC/LICENSE.txt" "$OUT/LICENSE-z3.txt"

echo "== the module"
"$SDK/bin/llvm-objcopy" --remove-section=name "$OUT/z3.wasm" "$WORK/z3-noname.wasm"
{
  echo "z3 $Z3_VERSION ($Z3_COMMIT), wasm32-wasip1"
  echo "wasi-sdk $WASI_SDK_VERSION: $("$SDK/bin/clang" --version | head -1)"
  echo "cflags: $CFLAGS_COMMON"
  echo "ldflags: $LDFLAGS_COMMON $LIBS_COMMON"
  echo "cmake: -DZ3_SINGLE_THREADED=ON -DZ3_POLLING_TIMER=OFF -DZ3_USE_LIB_GMP=OFF -DBUILD_SHARED_LIBS=OFF Release"
  echo "shim: src/util/scoped_timer.cpp (no threads; a timer never fires)"
  echo "size: $(wc -c < "$OUT/z3.wasm") bytes; without the name section $(wc -c < "$WORK/z3-noname.wasm") bytes"
  echo "sha256: $(sha256sum "$OUT/z3.wasm" | cut -d' ' -f1)"
  echo
  python3 -I "$HERE/imports.py" "$OUT/z3.wasm"
} | tee "$OUT/build-info.txt"

( cd "$OUT" && sha256sum z3.wasm LICENSE-z3.txt > SHA256SUMS && cat SHA256SUMS )

echo "== pins"
if [ "$UNPINNED" -ne 0 ]; then
  echo "some downloads are not pinned (see UNPINNED above)"
  exit 3
fi
echo "every download matched its pin"
