#!/usr/bin/env bash
# Builds the solver module of the Smt controller: Z3 (the release pins.env pins) for
# wasm32-wasip1 with the pinned wasi-sdk, on Linux x64:
#
#   z3-<version>.wasm  the z3 command line (src/shell): single-threaded, no GMP, C++ exceptions
#                      as WebAssembly exceptions (the standard proposal, exnref), 8 MiB of
#                      shadow stack below the data, memory up to the wasm32 ceiling of 4 GiB
#   LICENSE-z3.txt     Z3's licence (MIT), as it is in the source archive
#   build-info.txt     what the module was built from and with
#
# Usage: build.sh <out-dir> [<work-dir>]
#        build.sh --inputs   prints one SHA-256 over what the module is made from (this script,
#                            the two source changes, the pins it reads) and builds nothing: the
#                            name a build's output can be kept under
#
# The build is reproducible: the same pins give the same bytes, wherever the checkout or the
# work folder is. The compiler, the linker and the libraries are the pinned SDK's; cmake, make
# and python3 (Z3's build generates sources with it) are the build machine's. The work folder
# is mapped out of every file name the compiler writes, the section that names the compiler's
# own build is removed, and the module is checked for the real folders afterwards.
#
# A build takes twenty to thirty minutes on four cores. pack.sh makes the data asset from what
# this script writes.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"

# shellcheck disable=SC1091
. "$HERE/pins.env"

if [ "${1:-}" = "--inputs" ]; then
  {
    cat "$HERE/build.sh" "$HERE/patch.py" "$HERE/wasi/scoped_timer.cpp"
    for name in Z3_VERSION Z3_COMMIT Z3_SOURCE_URL Z3_SOURCE_SHA256 Z3_LICENSE_SHA256 Z3_SCOPED_TIMER_SHA256 Z3_HWF_SHA256 \
                WASI_SDK_VERSION WASI_SDK_URL WASI_SDK_SHA256 WASI_LIBC_COMMIT LLVM_COMMIT; do
      printf '%s=%s\n' "$name" "${!name}"
    done
  } | sha256sum | cut -d' ' -f1
  exit 0
fi
[ "$#" -ge 1 ] || { echo "usage: build.sh <out-dir> [<work-dir>] | build.sh --inputs"; exit 64; }

OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="${2:-$(mktemp -d)}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
MODULE="z3-$Z3_VERSION.wasm"

UNPINNED=0

fetch() {
  local url="$1" pin="$2" file="$3" seen
  if [ ! -f "$file" ]; then
    curl -fsSL --retry 3 --max-time 600 -o "$file" "$url" || { echo "DOWNLOAD FAILED $url"; exit 1; }
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
fetch "$Z3_SOURCE_URL" "$Z3_SOURCE_SHA256" "$WORK/z3-source.tar.gz"
# A download without a pin is hashed and named above, and that is all that is done with it:
# nothing is unpacked or run before every download has matched its pin.
if [ "$UNPINNED" -ne 0 ]; then
  echo "some downloads are not pinned (see UNPINNED above): nothing was unpacked or run"
  exit 3
fi
echo "every download matched its pin"

echo "== wasi-sdk $WASI_SDK_VERSION"
SDK="$WORK/wasi-sdk"
if [ ! -x "$SDK/bin/clang" ]; then
  rm -rf "$SDK"
  mkdir -p "$SDK"
  tar -xzf "$WORK/wasi-sdk.tar.gz" -C "$SDK" --strip-components=1
fi
"$SDK/bin/clang" --version | head -1
cmake --version | head -1
python3 --version
# The notices that travel with the module take the licence texts of wasi-libc and of LLVM's
# libraries at the commits pins.env names: they must be the commits this SDK was built from.
for line in "wasi-libc: ${WASI_LIBC_COMMIT:0:12}" "llvm: ${LLVM_COMMIT:0:12}"; do
  if ! grep -q -x -F "$line" "$SDK/VERSION"; then
    echo "the SDK's VERSION file has no line '$line': pins.env names another commit"
    cat "$SDK/VERSION"
    exit 1
  fi
  echo "the SDK's VERSION file: $line"
done

echo "== z3 $Z3_VERSION"
# The source under a fixed name, whatever the archive's top folder is called: the name is in
# the module (the file names of Z3's checks start with z3/src/).
SRC="$WORK/z3"
rm -rf "$SRC"
mkdir -p "$SRC"
tar -xzf "$WORK/z3-source.tar.gz" -C "$SRC" --strip-components=1
case "$(head -1 "$SRC/scripts/VERSION.txt")" in
  "$Z3_VERSION".*) echo "z3 source: $(head -1 "$SRC/scripts/VERSION.txt")" ;;
  *) echo "the source archive is Z3 $(head -1 "$SRC/scripts/VERSION.txt"), pins.env says $Z3_VERSION"; exit 1 ;;
esac

echo "== source changes"
# A file the build does not take as it is must be the one its change was written against.
held() {
  local seen
  seen="$(sha256sum "$SRC/$1" | cut -d' ' -f1)"
  if [ "$seen" != "$2" ]; then
    echo "$1 is not the pinned file ($seen, pinned $2): read the change before moving the pin"
    exit 1
  fi
}
held LICENSE.txt "$Z3_LICENSE_SHA256"
# The scoped timer (a thread, pthread_atfork) gives way to a timer that never fires.
held src/util/scoped_timer.cpp "$Z3_SCOPED_TIMER_SHA256"
cp "$HERE/wasi/scoped_timer.cpp" "$SRC/src/util/scoped_timer.cpp"
echo "  src/util/scoped_timer.cpp: replaced by wasi/scoped_timer.cpp"
# The hardware floats set no rounding mode; patch.py holds the file to its pin itself.
python3 -I "$HERE/patch.py" "$SRC" "$Z3_HWF_SHA256"

# Flags.
#  -fwasm-exceptions -mllvm -wasm-use-legacy-eh=false : C++ exceptions as the standard
#      WebAssembly exception-handling proposal (wasi-sdk's CppExceptions.md); the same at link
#      time picks the sysroot's libc++ and libc++abi built with exceptions, -lunwind their
#      unwinder.
#  _WASI_EMULATED_*: wasi-libc's emulations of signal() (the shell installs a Ctrl-C handler),
#      clock() (statistics) and getpid() (never reached; debug.cpp).
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
# On a failure the build runs once more ignoring errors (-i), so that every translation unit
# that fails, in every library, is reported in one run.
if ! make -C "$BUILD" -k -j"$(nproc)" shell > "$WORK/make.log" 2>&1; then
  make -C "$BUILD" -i -j"$(nproc)" shell > "$WORK/make-all.log" 2>&1 || true
  echo "== the build failed; every error of a run that ignores errors:"
  grep -E "error:|undefined symbol|wasm-ld:" "$WORK/make-all.log" | sed "s|$WORK/||g" | sort | uniq -c | sort -rn | head -150
  echo "== the last lines of the first run"
  tail -30 "$WORK/make.log"
  exit 1
fi
grep -c -E "warning:" "$WORK/make.log" | sed 's/^/warnings: /' || true

LINKED="$BUILD/z3.wasm"
[ -f "$LINKED" ] || LINKED="$BUILD/z3"
[ -f "$LINKED" ] || { echo "no z3 module in $BUILD"; ls -la "$BUILD"; exit 1; }

# The producers section names the compiler's own build; the module ships without it (the
# Verify toolchain does the same with its modules).
"$SDK/bin/llvm-objcopy" --remove-section=producers "$LINKED" "$OUT/$MODULE"

# Nothing of the build machine may be in the module: none of the real folders the build used.
for folder in "$WORK" "$HERE"; do
  if grep -q -a -F "$folder" "$OUT/$MODULE"; then
    echo "the module holds a path of the build machine: $folder"
    exit 1
  fi
done
echo "no path of the build machine in the module"

cp "$SRC/LICENSE.txt" "$OUT/LICENSE-z3.txt"

echo "== the module"
# The record names no folder either: the same lines from every build.
"$SDK/bin/llvm-objcopy" --remove-section=name "$OUT/$MODULE" "$WORK/z3-noname.wasm"
SEEN="$(sha256sum "$OUT/$MODULE" | cut -d' ' -f1)"
SIZE="$(wc -c < "$OUT/$MODULE" | tr -d ' ')"
{
  echo "z3 $Z3_VERSION ($Z3_COMMIT), wasm32-wasip1"
  echo "source: $Z3_SOURCE_URL (SHA-256 $Z3_SOURCE_SHA256)"
  echo "wasi-sdk $WASI_SDK_VERSION: $("$SDK/bin/clang" --version | head -1)"
  echo "cflags: ${CFLAGS_COMMON//"$WORK"/<work>}"
  echo "ldflags: $LDFLAGS_COMMON $LIBS_COMMON"
  echo "cmake: Release, -DBUILD_SHARED_LIBS=OFF -DZ3_SINGLE_THREADED=ON -DZ3_POLLING_TIMER=OFF -DZ3_USE_LIB_GMP=OFF -DZ3_LINK_TIME_OPTIMIZATION=OFF"
  echo "source changes: src/util/scoped_timer.cpp replaced (no threads: a timer never fires); src/util/hwf.cpp edited (round to nearest only)"
  echo "size: $SIZE bytes; without the name section $(wc -c < "$WORK/z3-noname.wasm" | tr -d ' ') bytes"
  echo "sha256: $SEEN"
} | tee "$OUT/build-info.txt"

echo "== built"
( cd "$OUT" && sha256sum "$MODULE" LICENSE-z3.txt )
if [ "$SEEN" = "$Z3_MODULE_SHA256" ] && [ "$SIZE" = "$Z3_MODULE_SIZE" ]; then
  echo "the module is the one pins.env pins"
else
  echo "NOT the module pins.env pins ($Z3_MODULE_SHA256, $Z3_MODULE_SIZE bytes)"
fi
