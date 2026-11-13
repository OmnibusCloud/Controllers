#!/usr/bin/env bash
# Builds the numpy runtime from pinned sources with a pinned toolchain, on Linux x64:
#
#   python-numpy.wasm   CPython 3.14.6 for wasm32-wasip1, built by its own WASI driver with the
#                       WASI SDK it supports (24), with numpy 2.5.4's extension modules linked in
#                       as builtins (wasi-libc has no dlopen)
#   numpy-2.5.4.zip     numpy's Python package as a layer: bytecode only, compiled by
#                       python-numpy.wasm itself (unchecked-hash .pyc), a stored zip with fixed
#                       times and order, as the asset tool builds the sympy layer
#   python314.zip       the standard library the same way, for the smoke runs (the catalog's own
#                       stdlib layer is the asset tool's; the two are the same Python version)
#   LICENSE-python.txt, LICENSE-numpy.txt (numpy's licence and those of the parts compiled in),
#   LICENSE-wasi-python.txt (the patches and recipes adapted from wasi-python)
#   build-info.txt      the link line, the builtin modules, the sizes and hashes
#
# numpy is configured for what the sandbox runs: no threads, no SIMD dispatch (wasm32 has no CPU
# features for numpy's dispatcher), no BLAS (numpy's own lapack_lite), C++ throws stop the guest
# (cxa_stubs.c), so the module has no exception-handling instruction. See the files next to this
# one for each adaptation.
#
# Usage: build.sh <out-dir> [<work-dir>]
# Prints the SHA-256 of everything it downloads and of everything it builds. The build is meant
# to be reproducible: the same pins give the same bytes.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="${2:-$(mktemp -d)}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"

# shellcheck disable=SC1091
. "$HERE/../pins.env"   # the wasmtime the sandbox runs on
# shellcheck disable=SC1091
. "$HERE/pins.env"

# The same bytes wherever and whenever the build runs: the date stamp sys.version shows is the
# CPython tarball's newest file, and nothing depends on the locale, the time zone or hashing.
export SOURCE_DATE_EPOCH="$CPYTHON_SOURCE_DATE_EPOCH"
export LC_ALL=C TZ=UTC PYTHONHASHSEED=0 PYTHONDONTWRITEBYTECODE=1
unset PYTHONPATH PYTHONHOME

UNPINNED=0
JOBS="$(nproc)"

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

# fail <log> <message>: the end of a log and one line naming what failed.
fail() {
  [ -f "$1" ] && tail -n 60 "$1"
  echo "ERROR: $2"
  exit 1
}

echo "== downloads"
fetch "$WASI_SDK24_URL" "$WASI_SDK24_SHA256" "$WORK/wasi-sdk-24.tar.gz"
fetch "$CPYTHON_URL" "$CPYTHON_SHA256" "$WORK/Python-$CPYTHON_VERSION.tar.xz"
fetch "$NUMPY_URL" "$NUMPY_SHA256" "$WORK/numpy-$NUMPY_VERSION.tar.gz"
fetch "$CYTHON_URL" "$CYTHON_SHA256" "$WORK/$(basename "$CYTHON_URL")"
fetch "$NINJA_URL" "$NINJA_SHA256" "$WORK/$(basename "$NINJA_URL")"
fetch "$WASMTIME_URL" "$WASMTIME_SHA256" "$WORK/wasmtime.tar.xz"

echo "== tools"
SDK="$WORK/wasi-sdk-24"
if [ ! -x "$SDK/bin/clang" ]; then
  mkdir -p "$SDK"
  tar -xzf "$WORK/wasi-sdk-24.tar.gz" -C "$SDK" --strip-components=1
fi
"$SDK/bin/clang" --version | head -1
WASMTIME_DIR="$WORK/wasmtime"
if [ ! -x "$WASMTIME_DIR/wasmtime" ]; then
  mkdir -p "$WASMTIME_DIR"
  tar -xJf "$WORK/wasmtime.tar.xz" -C "$WASMTIME_DIR" --strip-components=1
fi
WASMTIME="$WASMTIME_DIR/wasmtime"
"$WASMTIME" --version
# Cython and ninja from their wheels, unpacked (no installer): Cython is pure Python, run by the
# build host's Python; ninja is one binary.
TOOLS="$WORK/tools"
rm -rf "$TOOLS"
mkdir -p "$TOOLS/bin"
python3 -I -m zipfile -e "$WORK/$(basename "$CYTHON_URL")" "$TOOLS/cython-pkg"
python3 -I -m zipfile -e "$WORK/$(basename "$NINJA_URL")" "$TOOLS/ninja-pkg"
chmod +x "$TOOLS/ninja-pkg/ninja-1.13.2.data/scripts/ninja"
ln -s "$TOOLS/ninja-pkg/ninja-1.13.2.data/scripts/ninja" "$TOOLS/bin/ninja"
printf '#!/bin/sh\nexec python3 -s "%s/cython-pkg/cython.py" "$@"\n' "$TOOLS" > "$TOOLS/cython"
chmod +x "$TOOLS/cython"
export PATH="$TOOLS/bin:$WASMTIME_DIR:$PATH"
"$TOOLS/cython" --version
ninja --version
python3 --version

echo "== CPython $CPYTHON_VERSION (wasm32-wasip1)"
CP="$WORK/Python-$CPYTHON_VERSION"
W="$CP/cross-build/wasm32-wasip1"
rm -rf "$CP"
tar -xJf "$WORK/Python-$CPYTHON_VERSION.tar.xz" -C "$WORK"
mkdir -p "$WORK/logs/cpython"
# CPython's own driver: a native build-Python, then the WASI interpreter under cross-build/.
# --disable-test-modules: the _test* and xx* modules are not part of the product.
# The string hash stays the one configure picks in a cross build (32-bit FNV), the hash of the
# catalog's python-3.14.6 (brettcannon's build): with the sandbox's PYTHONHASHSEED=0 a program
# that prints a set of strings prints it in the same order on both runtimes.
(
  cd "$CP"
  WASI_SDK_PATH="$SDK" python3 Tools/wasm/wasi build --quiet --logdir "$WORK/logs/cpython" -- \
    --disable-test-modules
) > "$WORK/logs/cpython/driver.log" 2>&1 || { cat "$WORK/logs/cpython/driver.log"; for log in "$WORK"/logs/cpython/*.log; do echo "--- $log"; tail -n 30 "$log"; done; exit 1; }
[ -f "$W/python.wasm" ] || fail "$WORK/logs/cpython/driver.log" "no python.wasm in $W"
BUILD_PYTHON="$(find "$CP/cross-build" -mindepth 2 -maxdepth 2 -name python -type f ! -path '*/wasm32-wasip1/*' | head -n 1)"
[ -x "$BUILD_PYTHON" ] || fail /dev/null "no build-Python under $CP/cross-build"
echo "build-Python: $("$BUILD_PYTHON" --version) at ${BUILD_PYTHON#"$WORK"/}"
echo "python.wasm without numpy: $(wc -c < "$W/python.wasm") bytes"

echo "== numpy $NUMPY_VERSION (static archives)"
NP="$WORK/numpy-$NUMPY_VERSION"
NB="$WORK/numpy-build"
rm -rf "$NP" "$NB" "$WORK/numpy-install"
tar -xzf "$WORK/numpy-$NUMPY_VERSION.tar.gz" -C "$WORK"
for patch in "$HERE"/patches/*.patch; do
  ( cd "$NP" && patch -p1 --no-backup-if-mismatch -s < "$patch" ) || fail /dev/null "$(basename "$patch") did not apply"
  echo "patched: $(basename "$patch")"
done
sed -e "s#@HERE@#$HERE#g" -e "s#@SDK@#$SDK#g" -e "s#@TOOLS@#$TOOLS#g" -e "s#@WORK@#$WORK#g" \
  "$HERE/wasi-numpy.cross.in" > "$WORK/wasi-numpy.cross"
export WASI_SDK_PATH="$SDK" PYNP_BUILD_PYTHON="$BUILD_PYTHON" PYNP_WASI_BUILD="$W" PYNP_CPYTHON="$CP"
EXT_SUFFIX="$("$HERE/python-wasi-cross" -c 'import sysconfig; print(sysconfig.get_config_var("EXT_SUFFIX"))')"
echo "the target Python's extension suffix: $EXT_SUFFIX"
mkdir -p "$WORK/logs/numpy"
MESON=(python3 "$NP/vendored-meson/meson/meson.py")
(
  cd "$NP"
  "${MESON[@]}" setup "$NB" --cross-file "$WORK/wasi-numpy.cross" --buildtype=release \
    -Dallow-noblas=true -Dblas=none -Dlapack=none \
    -Ddisable-optimization=true -Ddisable-threading=true \
    -Ddisable-highway=true -Ddisable-intel-sort=true -Ddisable-svml=true
) > "$WORK/logs/numpy/setup.log" 2>&1 || fail "$WORK/logs/numpy/setup.log" "meson setup failed"
grep -E "Long double format|Disabling CPU feature|BLAS|LAPACK|Message:" "$WORK/logs/numpy/setup.log" | head -n 20 || true
ninja -C "$NB" -j "$JOBS" > "$WORK/logs/numpy/ninja.log" 2>&1 || fail "$WORK/logs/numpy/ninja.log" "numpy's build failed"
echo "numpy: $(find "$NB" -name "*$EXT_SUFFIX" | wc -l) extension archives"
"${MESON[@]}" install -C "$NB" --destdir "$WORK/numpy-install" --tags python-runtime --quiet \
  > "$WORK/logs/numpy/install.log" 2>&1 || fail "$WORK/logs/numpy/install.log" "meson install failed"
SITE="$(find "$WORK/numpy-install" -type f -name __init__.py -path '*site-packages/numpy/__init__.py' | head -n 1)"
[ -n "$SITE" ] || fail /dev/null "no installed numpy package under $WORK/numpy-install"
SITE="$(dirname "$SITE")"
grep -q 'NPY_SIZEOF_LONGDOUBLE 16' "$NB/numpy/_core/include/numpy/_numpyconfig.h" \
  || fail /dev/null "numpy's generated _numpyconfig.h does not describe wasm32's 16-byte long double"

echo "== link (numpy's modules as builtins)"
L="$WORK/link"
rm -rf "$L"
mkdir -p "$L"
python3 -I "$HERE/numpy_builtins.py" "$NB" "$NUMPY_VERSION" "$EXT_SUFFIX" "$L"
# makesetup reads sources under Modules/ (it mangles absolute paths) and the build directory's
# Setup.local. No `=` may appear in a module line: makesetup reads such a line as a variable and
# drops the module without a word, so config.c is checked below.
cp "$HERE/wasinumpy.c" "$HERE/cxa_stubs.c" "$L/npyinittab.h" "$CP/Modules/"
{
  echo "# numpy's extension modules as builtins (toolchain/python-numpy/build.sh)."
  echo "*static*"
  echo "_wasinumpy wasinumpy.c cxa_stubs.c $(cat "$L/link.txt") -lc++ -lc++abi -lc-printscan-long-double"
} > "$W/Modules/Setup.local"
( cd "$W" && make -j "$JOBS" all ) > "$L/make.log" 2>&1 || fail "$L/make.log" "the relink failed"
grep -q 'PyInit__wasinumpy' "$W/Modules/config.c" || fail /dev/null "Setup.local's line was dropped (Modules/config.c)"
# A clash only a static link creates is a trap at run time; wasm-ld's warning is the detector.
if grep -E 'signature mismatch|duplicate symbol|undefined symbol' "$L/make.log"; then
  fail /dev/null "the static link has clashing or missing symbols ($L/make.log)"
fi

echo "== python-numpy.wasm"
# Debug sections stripped (the module keeps its name section, so a trap names the function);
# the producers section names the compiler's own build, which differs between hosts.
"$SDK/bin/llvm-strip" --strip-debug -o "$L/python.stripped.wasm" "$W/python.wasm"
"$SDK/bin/llvm-objcopy" --remove-section=producers "$L/python.stripped.wasm" "$OUT/python-numpy.wasm"

echo "== licences"
cp "$CP/LICENSE" "$OUT/LICENSE-python.txt"
{
  cat "$NP/LICENSE.txt"
  # The licences of the parts numpy compiles in for this build (highway, x86-simd-sort and
  # SVML are switched off and not in the module).
  for part in numpy/fft/pocketfft/LICENSE.md numpy/linalg/lapack_lite/LICENSE.txt numpy/ma/LICENSE \
    numpy/random/LICENSE.md numpy/random/src/distributions/LICENSE.md numpy/random/src/mt19937/LICENSE.md \
    numpy/random/src/pcg64/LICENSE.md numpy/random/src/philox/LICENSE.md numpy/random/src/sfc64/LICENSE.md \
    numpy/random/src/splitmix64/LICENSE.md numpy/_core/include/numpy/libdivide/LICENSE.txt; do
    [ -f "$NP/$part" ] || fail /dev/null "numpy has no $part"
    printf '\n----\n\nName: %s\n\n' "$part"
    cat "$NP/$part"
  done
} > "$OUT/LICENSE-numpy.txt"
cp "$HERE/LICENSE-wasi-python.txt" "$OUT/LICENSE-wasi-python.txt"

echo "== layers (bytecode compiled by python-numpy.wasm)"
STD="$WORK/layer-stdlib"
NPL="$WORK/layer-numpy"
rm -rf "$STD" "$NPL"
python3 -I "$HERE/layers.py" stage-stdlib "$CP/Lib" "$W/build/lib.wasi-wasm32-3.14" "$STD/lib/python3.14"
python3 -I "$HERE/layers.py" stage-package "$SITE" "$NPL/numpy"
# compileall inside the interpreter itself, as the asset tool does: legacy .pyc next to each .py,
# unchecked-hash, file names rewritten to where the zip puts them.
"$WASMTIME" run -W exceptions=n --dir "$STD::/" --env PYTHONHOME=/ --env PYTHONHASHSEED=0 \
  "$OUT/python-numpy.wasm" -B -m compileall -b -q -f --invalidation-mode unchecked-hash \
  -s /lib/python3.14 -p /lib/python314.zip /lib/python3.14 > "$WORK/logs/compile-stdlib.log" 2>&1 \
  || fail "$WORK/logs/compile-stdlib.log" "compileall of the standard library failed"
"$WASMTIME" run -W exceptions=n --dir "$STD::/" --dir "$NPL::/src" --env PYTHONHOME=/ --env PYTHONHASHSEED=0 \
  "$OUT/python-numpy.wasm" -B -m compileall -b -q -f --invalidation-mode unchecked-hash \
  -s /src -p "/lib/numpy-$NUMPY_VERSION.zip" /src > "$WORK/logs/compile-numpy.log" 2>&1 \
  || fail "$WORK/logs/compile-numpy.log" "compileall of numpy failed"
python3 -I "$HERE/layers.py" zip "$STD/lib/python3.14" "$OUT/python314.zip"
python3 -I "$HERE/layers.py" zip "$NPL" "$OUT/numpy-$NUMPY_VERSION.zip"

echo "== built"
{
  echo "CPython $CPYTHON_VERSION, numpy $NUMPY_VERSION, wasi-sdk 24, SOURCE_DATE_EPOCH $SOURCE_DATE_EPOCH"
  echo
  echo "Setup.local:"
  sed "s#$WORK#<work>#g" "$W/Modules/Setup.local"
  echo
  echo "python.wasm before numpy and after, unstripped: see the log; stripped: $(wc -c < "$OUT/python-numpy.wasm") bytes"
  echo
  echo "numpy's module archives:"
  find "$NB" -name "*$EXT_SUFFIX" -printf '%s %P\n' | sort -k 2
  echo
  echo "builtins:"
  grep -o '"numpy[^"]*"' "$L/npyinittab.h" | tr -d '"'
} > "$OUT/build-info.txt"
cat "$OUT/build-info.txt"
( cd "$OUT" && sha256sum python-numpy.wasm ./*.zip LICENSE-*.txt && ls -l python-numpy.wasm ./*.zip | awk '{ print $5, $9 }' )

echo "== pins"
if [ "$UNPINNED" -ne 0 ]; then
  echo "some downloads are not pinned (see UNPINNED above)"
  exit 3
fi
echo "every download matched its pin"
