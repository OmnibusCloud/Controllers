#!/usr/bin/env bash
# Builds the PDF extractor of the Corpus controller from pdftext/ (pdf_oxide 0.3.78 and the
# crates its Cargo.lock pins) for wasm32-wasip1 with the Rust release pins.env pins, on
# Linux x64:
#
#   pdftext.wasm             the module, as rustc writes it (no wasm-opt)
#   LICENSE-pdftext.txt      the licences of everything linked into it (licenses.py)
#   corpus-pdftext-wasm.zip  the data asset: wasm/pdftext.wasm and wasm/LICENSE-pdftext.txt,
#                            stored with fixed times, so the same module gives the same zip
#   SHA256SUMS               the SHA-256 of the three
#
# Usage: build.sh <out-dir> [<work-dir>]
#
# The build is reproducible: the same pins and sources give the same bytes, wherever the
# checkout or the work folder is and whatever the build machine's own Rust and Cargo setup.
# The toolchain comes from the pinned archives, not from rustup; Cargo runs with no
# environment but the build's own (env -i), from / with a fresh CARGO_HOME (so it reads no
# configuration file but its own), with --locked; every absolute path rustc could write into
# the module (the crate, the registry, the toolchain, the target folder) is mapped to a fixed
# one, and the module is checked for the real ones afterwards.
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
fetch "$RUSTC_URL" "$RUSTC_SHA256" "$WORK/rustc.tar.xz"
fetch "$CARGO_URL" "$CARGO_SHA256" "$WORK/cargo.tar.xz"
fetch "$RUST_STD_HOST_URL" "$RUST_STD_HOST_SHA256" "$WORK/rust-std-host.tar.xz"
fetch "$RUST_STD_TARGET_URL" "$RUST_STD_TARGET_SHA256" "$WORK/rust-std-target.tar.xz"

echo "== Rust $RUST_VERSION"
# Each archive holds <package>/components and a folder per component, the layout its own
# install.sh copies into a prefix; this does the same copy. The release's COPYRIGHT and
# LICENSE files go beside it for the licence notice.
RUST="$WORK/rust-$RUST_VERSION"
if [ ! -x "$RUST/bin/cargo" ]; then
  rm -rf "$RUST" "$WORK/rust-unpack"
  mkdir -p "$RUST" "$WORK/rust-unpack"
  for archive in rustc cargo rust-std-host rust-std-target; do
    tar -xJf "$WORK/$archive.tar.xz" -C "$WORK/rust-unpack"
  done
  for package in "$WORK"/rust-unpack/*/; do
    while read -r component; do
      cp -R "$package$component/." "$RUST/"
    done < "$package/components"
  done
  mkdir -p "$RUST/licenses"
  cp "$WORK"/rust-unpack/rustc-*/COPYRIGHT "$WORK"/rust-unpack/rustc-*/LICENSE-* "$RUST/licenses/"
  rm -rf "$WORK/rust-unpack"
fi
"$RUST/bin/rustc" -vV
"$RUST/bin/cargo" -V

# The crate as committed, copied into the work folder: the build reads nothing else of the
# checkout.
SRC="$WORK/src/pdftext"
rm -rf "$SRC"
mkdir -p "$SRC"
cp "$HERE/pdftext/Cargo.toml" "$HERE/pdftext/Cargo.lock" "$SRC/"
cp -R "$HERE/pdftext/src" "$SRC/"

CARGO_HOME_DIR="$WORK/cargo-home"
TARGET_DIR="$WORK/target"
mkdir -p "$CARGO_HOME_DIR" "$WORK/home"
SEP=$'\x1f'
FLAGS="--remap-path-prefix=$SRC=/pdftext${SEP}--remap-path-prefix=$CARGO_HOME_DIR=/cargo"
FLAGS="$FLAGS${SEP}--remap-path-prefix=$RUST=/rust${SEP}--remap-path-prefix=$TARGET_DIR=/target"

cargo_run() {
  (
    cd /
    env -i \
      PATH="$RUST/bin:/usr/bin:/bin" \
      HOME="$WORK/home" \
      CARGO_HOME="$CARGO_HOME_DIR" \
      CARGO_TARGET_DIR="$TARGET_DIR" \
      CARGO_ENCODED_RUSTFLAGS="$FLAGS" \
      CARGO_TERM_COLOR=never \
      RUSTC="$RUST/bin/rustc" \
      "$RUST/bin/cargo" "$@"
  )
}

echo "== pdftext.wasm (pdf_oxide $PDF_OXIDE_VERSION, $PDFTEXT_TARGET)"
cargo_run fetch --locked --manifest-path "$SRC/Cargo.toml"
cargo_run metadata --format-version 1 --locked --offline --manifest-path "$SRC/Cargo.toml" --filter-platform "$PDFTEXT_TARGET" > "$WORK/metadata.json"
cargo_run build --release --locked --offline --manifest-path "$SRC/Cargo.toml" --target "$PDFTEXT_TARGET" \
  --message-format=json-render-diagnostics > "$WORK/build-messages.jsonl"
cp "$TARGET_DIR/$PDFTEXT_TARGET/release/pdftext.wasm" "$OUT/pdftext.wasm"

# Nothing of the build machine may be in the module: none of the real folders the build used.
for folder in "$WORK" "$HERE" "$CARGO_HOME_DIR" "$RUST"; do
  if grep -q -a -F "$folder" "$OUT/pdftext.wasm"; then
    echo "the module holds a path of the build machine: $folder"
    exit 1
  fi
done
echo "no path of the build machine in the module"

echo "== LICENSE-pdftext.txt"
python3 -I "$HERE/licenses.py" "$WORK/metadata.json" "$WORK/build-messages.jsonl" "$PDFTEXT_TARGET" "$RUST/licenses" "$OUT/LICENSE-pdftext.txt"

echo "== corpus-pdftext-wasm.zip"
python3 -I - "$OUT" <<'EOF'
import os
import sys
import zipfile

out = sys.argv[1]
with zipfile.ZipFile(os.path.join(out, "corpus-pdftext-wasm.zip"), "w", zipfile.ZIP_STORED) as archive:
    for name, source in (("wasm/pdftext.wasm", "pdftext.wasm"), ("wasm/LICENSE-pdftext.txt", "LICENSE-pdftext.txt")):
        info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
        info.create_system = 3
        info.external_attr = 0o644 << 16
        info.compress_type = zipfile.ZIP_STORED
        with open(os.path.join(out, source), "rb") as handle:
            archive.writestr(info, handle.read())
EOF

echo "== built"
( cd "$OUT" && sha256sum pdftext.wasm LICENSE-pdftext.txt corpus-pdftext-wasm.zip | tee SHA256SUMS && ls -l pdftext.wasm LICENSE-pdftext.txt corpus-pdftext-wasm.zip | awk '{ print $5, $9 }' )

echo "== pins"
if [ "$UNPINNED" -ne 0 ]; then
  echo "some downloads are not pinned (see UNPINNED above)"
  exit 3
fi
echo "every download matched its pin"
