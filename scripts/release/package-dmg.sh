#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
ARCH="${2:-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP="$ROOT/dist/macos/osx-$ARCH/BeaverBoard.app"
DMG_ROOT="$ROOT/dist/dmg-root"
DMG="$ROOT/dist/BeaverBoard-${VERSION}-macOS-${ARCH}.dmg"

if [ ! -d "$APP" ]; then
    echo "ERROR: $APP not found. Run scripts/release/build-macos.sh first."
    exit 1
fi

rm -rf "$DMG_ROOT"
mkdir -p "$DMG_ROOT"

cp -R "$APP" "$DMG_ROOT/BeaverBoard.app"
ln -s /Applications "$DMG_ROOT/Applications"

echo "=== Packaging DMG ==="
hdiutil create \
  -volname "Beaver Board" \
  -srcfolder "$DMG_ROOT" \
  -ov \
  -format UDZO \
  "$DMG"

echo "=== DMG ready: $DMG ==="
ls -lh "$DMG"
