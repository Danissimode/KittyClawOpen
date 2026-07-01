#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
RID="${2:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DIST="$ROOT/dist/macos/$RID"
APP="$DIST/BeaverBoard.app"
PUBLISH="$APP/Contents/Resources/app"
DOTNET="${DOTNET:-$(which dotnet 2>/dev/null || echo "/usr/local/share/dotnet/dotnet")}"

rm -rf "$DIST"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$PUBLISH"

echo "=== Publishing Beaver Board v$VERSION for $RID ==="

echo "Using dotnet: $DOTNET"
"$DOTNET" publish "$ROOT/KittyClaw.Web/KittyClaw.Web.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$PUBLISH" \
  -p:PublishSingleFile=false

echo "=== Copying app metadata ==="
cp "$ROOT/packaging/macos/app/Info.plist" "$APP/Contents/Info.plist"
cp "$ROOT/packaging/macos/app/BeaverBoard" "$APP/Contents/MacOS/BeaverBoard"
chmod +x "$APP/Contents/MacOS/BeaverBoard"

# Placeholder icon — if no .icns exists, skip
if [ -f "$ROOT/packaging/macos/app/BeaverBoard.icns" ]; then
    cp "$ROOT/packaging/macos/app/BeaverBoard.icns" "$APP/Contents/Resources/BeaverBoard.icns"
else
    echo "WARNING: No BeaverBoard.icns found; app will use default macOS icon."
fi

echo "=== Built $APP ==="
ls -la "$APP/Contents/MacOS/"
ls -la "$PUBLISH/"
