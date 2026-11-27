#!/bin/bash
# PF-1 spike: the reader's re-check of a Proof certificate (spec section 6.6), from nothing but
# the published tools and the certificate's inputs. A certificate prints these steps with its own
# values; this script runs them.
#
#   recheck.sh <certificate.env> <empty work folder>
#
# certificate.env holds (shell assignments):
#   REVISION_LEAN       the Lean version of the declared Mathlib revision (for the challenge)
#   KERNEL_LEAN         the Lean version whose kernels judged (the controller's pinned toolchain)
#   ASSET_<v>_<os>      the release asset of version <v> (dots as _) for this machine, and
#   SHA256_<v>_<os>     its SHA-256 (os: linux, linux_aarch64, darwin_aarch64, windows)
#   MATHLIB_REV         the Mathlib commit
#   CHALLENGE_FILE      the challenge's text (as the certificate prints it), CHALLENGE_SHA256 its digest
#   CHALLENGE_MODULE    the module name the challenge is elaborated as
#   CHALLENGE_EXPORT_SHA256  the digest of the challenge's export
#   THEOREMS, AXIOMS    comma-separated, as in comparator.json
#   EXPORT_FILE         the project's export (.ndjson or .ndjson.zst), EXPORT_SHA256 the digest of the .ndjson
#
# Needs: bash, curl, git, tar (or unzip), sha256sum (or shasum); to unpack zstd where tar cannot,
# zstd, a Python with compression.zstd, or Node. Exit 0 when the full verdict is "accepted" on this machine.

set -euo pipefail
CERT="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
WORK="$2"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
# shellcheck disable=SC1090
. "$CERT"
CERT_DIR="$(dirname "$CERT")"
LOG="$WORK/recheck.log"
step() { echo "== $(date -u +%H:%M:%S) $*" | tee -a "$LOG"; }
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
sha() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) OSK=linux ;;
  Linux-aarch64) OSK=linux_aarch64 ;;
  Darwin-arm64) OSK=darwin_aarch64 ;;
  MINGW*|MSYS*|CYGWIN*) OSK=windows ;;
  *) echo "unsupported machine"; exit 2 ;;
esac
SANDBOX="--inadvisably-no-sandbox"
if [ "$OSK" = linux ] || [ "$OSK" = linux_aarch64 ]; then
  if command -v bwrap >/dev/null && bwrap --ro-bind / / --unshare-all true 2>/dev/null; then SANDBOX=""; fi
fi

fetch_lean() {  # version -> prints the toolchain root
  local v="$1" key asset digest dest
  key="$(echo "$v" | tr '.-' '__')"
  asset="$(eval echo "\${ASSET_${key}_${OSK}}")"
  digest="$(eval echo "\${SHA256_${key}_${OSK}}")"
  dest="$WORK/lean-$v"
  if [ ! -d "$dest" ]; then
    curl -fsSL --retry 3 -o "$WORK/$asset" "https://github.com/leanprover/lean4/releases/download/v$v/$asset"
    [ "$(sha "$WORK/$asset")" = "$digest" ] || { echo "toolchain $asset: digest differs" >&2; exit 3; }
    mkdir -p "$dest"
    case "$asset" in
      *.zip) (cd "$dest" && unzip -q "$WORK/$asset") ;;
      *.tar.zst) tar --zstd -xf "$WORK/$asset" -C "$dest" 2>/dev/null || python3 -c "import sys, tarfile; tarfile.open(sys.argv[1], 'r:zst').extractall(sys.argv[2], filter='fully_trusted')" "$WORK/$asset" "$dest" ;;
    esac
    rm -f "$WORK/$asset"
  fi
  echo "$dest/$(ls "$dest" | head -1)"
}

step "1. toolchains: $REVISION_LEAN (the challenge) and $KERNEL_LEAN (the kernels), digests checked"
REV_ROOT="$(fetch_lean "$REVISION_LEAN")"
KER_ROOT="$(fetch_lean "$KERNEL_LEAN")"

step "2. the export, its digest checked"
SOLUTION="$WORK/solution.ndjson"
case "$EXPORT_FILE" in
  *.zst) zstd -dc "$CERT_DIR/$EXPORT_FILE" > "$SOLUTION" 2>/dev/null          || python3 -c "import sys, shutil; from compression import zstd; shutil.copyfileobj(zstd.open(sys.argv[1]), open(sys.argv[2], 'wb'), 1 << 22)" "$CERT_DIR/$EXPORT_FILE" "$SOLUTION" 2>/dev/null          || node -e "const fs=require('fs'),z=require('zlib');fs.createReadStream(process.argv[1]).pipe(z.createZstdDecompress()).pipe(fs.createWriteStream(process.argv[2]))" "$(native "$CERT_DIR/$EXPORT_FILE")" "$(native "$SOLUTION")" ;;
  *) cp "$CERT_DIR/$EXPORT_FILE" "$SOLUTION" ;;
esac
[ "$(sha "$SOLUTION")" = "$EXPORT_SHA256" ] || { echo "export: digest differs" >&2; exit 3; }

step "3. Mathlib $MATHLIB_REV, its official cache, the challenge elaborated and exported"
[ "$(sha "$CERT_DIR/$CHALLENGE_FILE")" = "$CHALLENGE_SHA256" ] || { echo "challenge text: digest differs" >&2; exit 3; }
ML="$WORK/mathlib4"
if [ ! -d "$ML/.git" ]; then
  git init -q "$ML"
  git -C "$ML" fetch -q --depth 1 https://github.com/leanprover-community/mathlib4 "$MATHLIB_REV"
  git -C "$ML" checkout -q FETCH_HEAD
fi
export PATH="$REV_ROOT/bin:$PATH"
(cd "$ML" && MATHLIB_CACHE_DIR="$WORK/mathlib-cache" lake exe cache get)
SRC="$WORK/challenge"
mkdir -p "$SRC/$(dirname "$(echo "$CHALLENGE_MODULE" | tr . /)")"
REL="$(echo "$CHALLENGE_MODULE" | tr . /)"
cp "$CERT_DIR/$CHALLENGE_FILE" "$SRC/$REL.lean"
LP="$(cd "$ML" && lake env printenv LEAN_PATH)"
SEP=":"; SRCP="$SRC"
if [ "$OSK" = windows ]; then SEP=";"; SRCP="$(cygpath -w "$SRC")"; fi
(cd "$SRC" && LEAN_PATH="$LP$SEP$SRCP" lean -o "$REL.olean" "$REL.lean")
NAMES="Quot Quot.mk Quot.lift Quot.ind $(echo "$THEOREMS" | tr , ' ') $(echo "$AXIOMS" | tr , ' ') Nat.add Nat.sub Nat.mul Nat.pow Nat.gcd Nat.div Nat.mod Nat.beq Nat.ble Nat.land Nat.lor Nat.xor Nat.shiftLeft Nat.shiftRight String.ofList Char.ofNat List eagerReduce Nat String String.mk Char optParam autoParam semiOutParam outParam"
# shellcheck disable=SC2086
(cd "$SRC" && LEAN_PATH="$LP$SEP$SRCP" leanexport "$CHALLENGE_MODULE" -- $NAMES > "$WORK/challenge.ndjson")
GOT="$(sha "$WORK/challenge.ndjson")"
echo "challenge export $GOT (certificate: $CHALLENGE_EXPORT_SHA256)" | tee -a "$LOG"
[ "$GOT" = "$CHALLENGE_EXPORT_SHA256" ] || { echo "challenge export: digest differs" >&2; exit 4; }

step "4. lake comparator of $KERNEL_LEAN on the two exports ${SANDBOX:-(inside bubblewrap)}"
python3 -c "import json, sys; json.dump({'challenge_module': 'Challenge', 'solution_module': 'Solution', 'theorem_names': sys.argv[1].split(','), 'permitted_axioms': sys.argv[2].split(',')}, open(sys.argv[3], 'w'))" "$THEOREMS" "$AXIOMS" "$WORK/comparator.json" 2>/dev/null \
  || printf '{"challenge_module":"Challenge","solution_module":"Solution","theorem_names":["%s"],"permitted_axioms":["%s"]}\n' "$(echo "$THEOREMS" | sed 's/,/","/g')" "$(echo "$AXIOMS" | sed 's/,/","/g')" > "$WORK/comparator.json"
export PATH="$KER_ROOT/bin:$PATH"
set +e
(cd "$WORK" && "$KER_ROOT/bin/lake" comparator --config comparator.json --challenge-from-export challenge.ndjson --solution-from-export solution.ndjson ${EXTRA_FLAGS:-} $SANDBOX) 2>&1 | tee -a "$LOG"
CODE=${PIPESTATUS[0]}
set -e
step "verdict: lake comparator exit $CODE (0: accepted)"
exit "$CODE"
