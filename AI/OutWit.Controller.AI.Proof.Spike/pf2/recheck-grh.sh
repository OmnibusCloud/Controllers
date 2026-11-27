#!/bin/bash
# PF-2 spike: the reader's re-check (spec section 6.6, PF-1's recheck.sh) for a challenge that
# spans several modules - the RH/GRH kit's `Statements` (Mathlib-only definitions) and
# `Challenge` (the five theorems with `sorry`) - from nothing but the published tools, the
# challenge's text and the export. STEPS picks the steps (default 1,2,3,4) so that a long run can
# be split; each step logs its start and end.
#
#   recheck-grh.sh <certificate.env> <work folder>
#
# certificate.env (shell assignments):
#   REVISION_LEAN, KERNEL_LEAN      the revision's Lean (for the challenge) and the kernels' Lean
#   ASSET_<v>_<os>, SHA256_<v>_<os> each toolchain's release asset for this machine and its digest
#   MATHLIB_REV                     the Mathlib commit
#   CHALLENGE_URL_BASE              where the challenge's texts are fetched (the repository at its commit)
#   CHALLENGE_FILES                 "Module=relative/path:sha256 ..." in elaboration order
#   CHALLENGE_LEAN_ARGS             the options the project elaborates with (its lakefile's)
#   CHALLENGE_MODULE                the module exported, CHALLENGE_EXPORT_SHA256 the export's digest
#   CHALLENGE_EXPORT_URL            the challenge export as published (used when TRUST_CHALLENGE_EXPORT=1)
#   SOLUTION_MODULE, THEOREMS, AXIOMS   comparator.json's
#   EXPORT_FILE or EXPORT_URL       the project's export (.ndjson or .ndjson.zst); EXPORT_SHA256 the .ndjson's
#
# Needs bash, curl, git, sha256sum (or shasum), and a zstd reader (zstd, a Python with
# compression.zstd or zstandard, or Node). Exit 0 when the full verdict is "accepted" here.

set -euo pipefail
CERT="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
WORK="$2"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
# shellcheck disable=SC1090
. "$CERT"
STEPS="${STEPS:-1,2,3,4}"
LOG="$WORK/recheck.log"
step() { echo "== $(date -u +%H:%M:%S) $*" | tee -a "$LOG"; }
want() { case ",$STEPS," in *",$1,"*) return 0 ;; *) return 1 ;; esac; }
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
sha() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }
unzst() {
  zstd -dc "$1" > "$2" 2>/dev/null \
    || python3 -c "import sys, shutil; from compression import zstd; shutil.copyfileobj(zstd.open(sys.argv[1]), open(sys.argv[2], 'wb'), 1 << 22)" "$1" "$2" 2>/dev/null \
    || python3 -c "import sys, zstandard; zstandard.ZstdDecompressor().copy_stream(open(sys.argv[1], 'rb'), open(sys.argv[2], 'wb'))" "$1" "$2" 2>/dev/null \
    || node -e "const fs=require('fs'),z=require('zlib');fs.createReadStream(process.argv[1]).pipe(z.createZstdDecompress()).pipe(fs.createWriteStream(process.argv[2]))" "$(native "$1")" "$(native "$2")"
}

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
      *.tar.zst) tar --zstd -xf "$WORK/$asset" -C "$dest" 2>/dev/null || python3 -c "import sys, tarfile; tarfile.open(sys.argv[1], 'r:zst').extractall(sys.argv[2], filter='fully_trusted')" "$WORK/$asset" "$dest" 2>/dev/null || { unzst "$WORK/$asset" "$WORK/lean.tar" && tar -xf "$WORK/lean.tar" -C "$dest" && rm -f "$WORK/lean.tar"; } ;;
    esac
    rm -f "$WORK/$asset"
  fi
  echo "$dest/$(ls "$dest" | head -1)"
}

if want 1; then
  step "1. toolchains: $REVISION_LEAN (the challenge) and $KERNEL_LEAN (the kernels), digests checked"
  fetch_lean "$REVISION_LEAN" >/dev/null
  fetch_lean "$KERNEL_LEAN" >/dev/null
fi
REV_ROOT="$(fetch_lean "$REVISION_LEAN")"
KER_ROOT="$(fetch_lean "$KERNEL_LEAN")"

SOLUTION="$WORK/solution.ndjson"
if want 2; then
  step "2. the export, its digest checked"
  SOURCE="${EXPORT_FILE:-}"
  if [ -z "$SOURCE" ] || [ ! -f "$SOURCE" ]; then
    SOURCE="$WORK/$(basename "$EXPORT_URL")"
    curl -fsSL --retry 5 -o "$SOURCE" "$EXPORT_URL"
  fi
  case "$SOURCE" in
    *.zst) unzst "$SOURCE" "$SOLUTION" ;;
    *) cp "$SOURCE" "$SOLUTION" ;;
  esac
  [ "$(sha "$SOLUTION")" = "$EXPORT_SHA256" ] || { echo "export: digest differs" >&2; exit 3; }
fi

if want 3; then
  if [ "${TRUST_CHALLENGE_EXPORT:-0}" = 1 ]; then
    step "3. the challenge export as published (trusted: not reproduced here), its digest checked"
    curl -fsSL --retry 5 -o "$WORK/challenge.ndjson.zst" "$CHALLENGE_EXPORT_URL"
    unzst "$WORK/challenge.ndjson.zst" "$WORK/challenge.ndjson"
  else
    step "3. Mathlib $MATHLIB_REV, its official cache, the challenge's modules elaborated in order and exported"
    ML="$WORK/mathlib4"
    if [ ! -d "$ML/.git" ]; then
      git init -q "$ML"
      git -C "$ML" fetch -q --depth 1 https://github.com/leanprover-community/mathlib4 "$MATHLIB_REV"
      git -C "$ML" checkout -q FETCH_HEAD
    fi
    export PATH="$REV_ROOT/bin:$PATH"
    (cd "$ML" && MATHLIB_CACHE_DIR="$WORK/mathlib-cache" lake exe cache get)
    SRC="$WORK/challenge"
    mkdir -p "$SRC"
    LP="$(cd "$ML" && lake env printenv LEAN_PATH)"
    SEP=":"; SRCP="$SRC"
    if [ "$OSK" = windows ]; then SEP=";"; SRCP="$(cygpath -w "$SRC")"; fi
    for entry in $CHALLENGE_FILES; do
      module="${entry%%=*}"; rest="${entry#*=}"; path="${rest%%:*}"; digest="${rest##*:}"
      curl -fsSL --retry 3 -o "$SRC/$module.lean" "$CHALLENGE_URL_BASE/$path"
      [ "$(sha "$SRC/$module.lean")" = "$digest" ] || { echo "challenge text $path: digest differs" >&2; exit 3; }
      # shellcheck disable=SC2086
      (cd "$SRC" && LEAN_PATH="$LP$SEP$SRCP" lean $CHALLENGE_LEAN_ARGS -o "$module.olean" "$module.lean")
      echo "elaborated $module ($path, $digest)" | tee -a "$LOG"
    done
    NAMES="Quot Quot.mk Quot.lift Quot.ind $(echo "$THEOREMS" | tr , ' ') $(echo "$AXIOMS" | tr , ' ') Nat.add Nat.sub Nat.mul Nat.pow Nat.gcd Nat.div Nat.mod Nat.beq Nat.ble Nat.land Nat.lor Nat.xor Nat.shiftLeft Nat.shiftRight String.ofList Char.ofNat List eagerReduce Nat String String.mk Char optParam autoParam semiOutParam outParam"
    # shellcheck disable=SC2086
    (cd "$SRC" && LEAN_PATH="$LP$SEP$SRCP" leanexport "$CHALLENGE_MODULE" -- $NAMES > "$WORK/challenge.ndjson")
  fi
  GOT="$(sha "$WORK/challenge.ndjson")"
  echo "challenge export $GOT (certificate: $CHALLENGE_EXPORT_SHA256)" | tee -a "$LOG"
  [ "$GOT" = "$CHALLENGE_EXPORT_SHA256" ] || { echo "challenge export: digest differs" >&2; exit 4; }
fi

if want 4; then
  step "4. lake comparator of $KERNEL_LEAN on the two exports ${SANDBOX:-(inside bubblewrap)}"
  printf '{"challenge_module":"%s","solution_module":"%s","theorem_names":["%s"],"permitted_axioms":["%s"]}\n' \
    "$CHALLENGE_MODULE" "$SOLUTION_MODULE" "$(echo "$THEOREMS" | sed 's/,/","/g')" "$(echo "$AXIOMS" | sed 's/,/","/g')" > "$WORK/comparator.json"
  export PATH="$KER_ROOT/bin:$PATH"
  set +e
  (cd "$WORK" && "$KER_ROOT/bin/lake" comparator --config comparator.json --challenge-from-export challenge.ndjson --solution-from-export solution.ndjson ${EXTRA_FLAGS:-} $SANDBOX) 2>&1 | tee -a "$LOG"
  CODE=${PIPESTATUS[0]}
  set -e
  step "verdict: lake comparator exit $CODE (0: accepted)"
  exit "$CODE"
fi
step "steps $STEPS done"
