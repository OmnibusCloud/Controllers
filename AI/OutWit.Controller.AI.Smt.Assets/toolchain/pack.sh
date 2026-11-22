#!/usr/bin/env bash
# Packs the data asset of the Smt controller's solver module from what build.sh wrote:
#
#   z3-<version>.wasm            the module and Z3's licence, as build.sh wrote them
#   LICENSE-z3.txt
#   THIRD-PARTY-NOTICES-z3.txt   what is linked into the module besides Z3, and the terms of
#                                each part (notices.py, from the pinned licence texts)
#   smt-z3-wasm.zip              the data asset: the three files above under wasm/, stored,
#                                with fixed times and modes, so the same files give the same zip
#   SHA256SUMS                   the SHA-256 of the four
#   build-info.txt               build.sh's record, beside them (not in the asset)
#
# Usage: pack.sh <module-dir> <out-dir> [<download-dir>]
#
# <module-dir> is build.sh's <out-dir>. Needs python3 and, for the licence texts, the network;
# it takes seconds, so the long build can be kept and packed again.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
[ "$#" -ge 2 ] || { echo "usage: pack.sh <module-dir> <out-dir> [<download-dir>]"; exit 64; }
FROM="$(cd "$1" && pwd)"
OUT="$(mkdir -p "$2" && cd "$2" && pwd)"
DOWNLOADS="${3:-$(mktemp -d)}"
mkdir -p "$DOWNLOADS"

# shellcheck disable=SC1091
. "$HERE/pins.env"

MODULE="z3-$Z3_VERSION.wasm"
NOTICES="THIRD-PARTY-NOTICES-z3.txt"
ARCHIVE="smt-z3-wasm.zip"

for name in "$MODULE" LICENSE-z3.txt build-info.txt; do
  [ -f "$FROM/$name" ] || { echo "no $name in $FROM (build.sh writes it)"; exit 1; }
  [ "$FROM" = "$OUT" ] || cp "$FROM/$name" "$OUT/$name"
done

echo "== $NOTICES"
python3 -I "$HERE/notices.py" "$HERE/pins.env" "$OUT/$NOTICES" "$DOWNLOADS"

echo "== $ARCHIVE"
python3 -I - "$OUT" "$ARCHIVE" "$MODULE" LICENSE-z3.txt "$NOTICES" <<'EOF'
import os
import sys
import zipfile

out, target = sys.argv[1], sys.argv[2]
path = os.path.join(out, target)
if os.path.exists(path):
    os.remove(path)
with zipfile.ZipFile(path, "w", zipfile.ZIP_STORED) as archive:
    for name in sys.argv[3:]:
        info = zipfile.ZipInfo("wasm/" + name, date_time=(1980, 1, 1, 0, 0, 0))
        info.create_system = 3
        info.external_attr = 0o644 << 16
        info.compress_type = zipfile.ZIP_STORED
        with open(os.path.join(out, name), "rb") as handle:
            archive.writestr(info, handle.read())
with zipfile.ZipFile(path) as archive:
    for info in archive.infolist():
        print("  %s  %d bytes" % (info.filename, info.file_size))
EOF

echo "== packed"
( cd "$OUT" && sha256sum "$MODULE" LICENSE-z3.txt "$NOTICES" "$ARCHIVE" | tee SHA256SUMS && ls -l "$MODULE" LICENSE-z3.txt "$NOTICES" "$ARCHIVE" | awk '{ print $5, $9 }' )

SEEN="$(sha256sum "$OUT/$ARCHIVE" | cut -d' ' -f1)"
SIZE="$(wc -c < "$OUT/$ARCHIVE" | tr -d ' ')"
if [ "$SEEN" = "$SMT_Z3_WASM_SHA256" ] && [ "$SIZE" = "$SMT_Z3_WASM_SIZE" ]; then
  echo "the archive is the one pins.env pins"
else
  echo "NOT the archive pins.env pins (${SMT_Z3_WASM_SHA256:-no pin}, ${SMT_Z3_WASM_SIZE:-no size} bytes)"
fi
