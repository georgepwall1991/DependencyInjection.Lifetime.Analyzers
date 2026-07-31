#!/usr/bin/env bash
# Rasterizes the Open Graph social card and records the source hash it was rendered from.
#
# assets/social-card.png is what link previews actually serve, so it must never lag behind
# assets/social-card.svg. The sidecar hash lets CI detect that drift without needing a
# rasterizer installed.
set -euo pipefail

cd "$(dirname "$0")/.."

source_svg="assets/social-card.svg"
rendered_png="assets/social-card.png"
sidecar="assets/social-card.png.source-sha256"

if ! command -v rsvg-convert >/dev/null 2>&1; then
  echo "rsvg-convert is required to re-render the social card (brew install librsvg)." >&2
  exit 1
fi

rsvg-convert -w 1200 -h 630 "$source_svg" -o "$rendered_png"
shasum -a 256 "$source_svg" | awk '{print $1}' > "$sidecar"

echo "Rendered $rendered_png from $source_svg ($(cat "$sidecar"))."
