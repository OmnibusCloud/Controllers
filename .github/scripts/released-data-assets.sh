#!/usr/bin/env bash
# Holds a controller package's data assets to their pins before the package is pushed to a feed.
#
# The assets are read from the manifest the package ships (content/module/controller.json): what
# the build evaluated from the project's ControllerDataAsset items and what every consumer reads,
# whatever form the project file wrote them in (metadata as elements or attributes, an Update
# item, an imported file). Each asset is fetched from its address and compared with its SHA-256
# and its size. A gh-release://owner/repo/tag/file address is the release download of that file;
# an https:// address is fetched as it is; any other address cannot be checked and fails.
#
# Usage: released-data-assets.sh <package.nupkg>
# Exit 0 when every declared asset is released as pinned; 1 when one is not, when one is not
# pinned in the form the build requires, or when the package declares none.
set -euo pipefail

package="${1:?usage: released-data-assets.sh <package.nupkg>}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# One line per asset: id, address, SHA-256, size, separated by the unit separator (not a tab: a
# tab is white space to read, and an empty field would shift the others). A package without the
# manifest, or a manifest that is not JSON, stops here.
python3 -I - "$package" > "$work/assets.txt" <<'PY'
import json
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as archive:
    manifest = json.loads(archive.read("content/module/controller.json").decode("utf-8-sig"))

for asset in manifest.get("dataAssets") or []:
    fields = [str(asset.get(key, "")) for key in ("id", "uri", "sha256", "size")]
    # A field that would break the line apart leaves the asset unpinned to the loop below.
    if any(("\x1f" in field) or ("\n" in field) or ("\r" in field) for field in fields):
        fields = [" ".join(fields[0].replace("\x1f", " ").split()), "", "", ""]
    print("\x1f".join(fields))
PY

checked=0
failed=0
while IFS=$'\x1f' read -r id uri pin size; do
  size="${size%$'\r'}"  # a Python on Windows ends its lines with CR LF
  checked=$((checked+1))
  if ! [[ "$pin" =~ ^[0-9a-f]{64}$ ]] || ! [[ "$size" =~ ^[1-9][0-9]*$ ]]; then
    echo "  FAIL $id  (not pinned: sha256 '$pin', size '$size')"
    failed=$((failed+1))
    continue
  fi
  case "$uri" in
    gh-release://*)
      rest="${uri#gh-release://}"
      repository="$(printf '%s' "$rest" | cut -d/ -f1-2)"
      tag="$(printf '%s' "$rest" | cut -d/ -f3)"
      file="$(printf '%s' "$rest" | cut -d/ -f4-)"
      if [ -z "$repository" ] || [ -z "$tag" ] || [ -z "$file" ]; then
        echo "  FAIL $id  (not a release address: $uri)"
        failed=$((failed+1))
        continue
      fi
      url="https://github.com/$repository/releases/download/$tag/$file"
      ;;
    https://*)
      url="$uri"
      ;;
    *)
      echo "  FAIL $id  (an address this guard cannot fetch: '$uri')"
      failed=$((failed+1))
      continue
      ;;
  esac
  if ! curl -fsSL --retry 3 -o "$work/asset" "$url"; then
    echo "  FAIL $id  (not at $url)"
    failed=$((failed+1))
    continue
  fi
  seen="$(sha256sum "$work/asset" | cut -d' ' -f1)"
  bytes="$(wc -c < "$work/asset" | tr -d ' ')"
  if [ "$seen" = "$pin" ] && [ "$bytes" = "$size" ]; then
    echo "  OK   $id  $uri  $seen  $bytes bytes"
  else
    echo "  FAIL $id  (the release has $seen, $bytes bytes; the package pins $pin, $size bytes)"
    failed=$((failed+1))
  fi
  rm -f "$work/asset"
done < "$work/assets.txt"

if [ "$checked" -eq 0 ]; then
  echo "::error::the package declares no data asset (content/module/controller.json of $package)"
  exit 1
fi
if [ "$failed" -gt 0 ]; then
  echo "::error::$failed of $checked data asset(s) are not released as pinned. Release the archives the toolchain workflows built, then publish."
  exit 1
fi
echo "every one of the $checked data asset(s) is released as pinned"
