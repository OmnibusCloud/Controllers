#!/usr/bin/env bash
# Bundles sucrase's transform for the Verify TypeScript runtime: the packages of package-lock.json
# (every one pinned by its integrity hash), esbuild 0.25.10, one minified script that defines the
# global `sucrase`, and the licences of every package the bundle carries. Build time only.
# Usage: bundle.sh <out-dir> <work-dir>
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="$(mkdir -p "$2" && cd "$2" && pwd)/typescript"

rm -rf "$WORK"
mkdir -p "$WORK"
cp "$HERE/package.json" "$HERE/package-lock.json" "$HERE/entry.mjs" "$HERE/licences.mjs" "$WORK/"
(
  cd "$WORK"
  npm ci --ignore-scripts --no-audit --no-fund --loglevel=error
  node node_modules/esbuild/bin/esbuild entry.mjs --bundle --format=iife --global-name=sucrase --minify \
    --metafile=meta.json --log-level=warning --outfile="$OUT/sucrase.js"
  node licences.mjs meta.json "$OUT/LICENSE-sucrase.txt"
)
