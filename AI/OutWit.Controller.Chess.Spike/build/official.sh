#!/usr/bin/env bash
# Fetches one of upstream's own release binaries of the pinned tag (a reference only: it must
# give the same bench and the same records as our build), verifies the archive's SHA-256 and
# prints the path of the binary inside it.
#
# usage: official.sh <ASSET_PIN_NAME> <dir>     e.g. official.sh OFFICIAL_LINUX_X64 /tmp/official
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
# shellcheck disable=SC1091
. "$here/../pins.env"

pin=${!1}
dir=$2
asset=${pin%%:*}
want=${pin##*:}
mkdir -p "$dir"

curl -fsSL --retry 3 --max-time 900 -o "$dir/$asset" "$SF_RELEASE_URL/$asset"
if command -v sha256sum >/dev/null 2>&1; then got=$(sha256sum "$dir/$asset" | cut -d' ' -f1)
else got=$(shasum -a 256 "$dir/$asset" | cut -d' ' -f1); fi
if [ "$got" != "$want" ]; then
  echo "::error::$asset has SHA-256 $got, pinned $want" >&2
  exit 1
fi

case "$asset" in
  *.zip) python -I -m zipfile -e "$dir/$asset" "$dir/x" ;;
  *) mkdir -p "$dir/x" && tar -xzf "$dir/$asset" -C "$dir/x" ;;
esac
rm -f "$dir/$asset"

name=${asset%.tar.gz}
name=${name%.zip}
bin="$dir/x/stockfish/$name"
[ -f "$bin.exe" ] && bin="$bin.exe"
chmod +x "$bin" 2>/dev/null || true
echo "$bin"
