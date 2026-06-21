#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="KittyClawOpen"

if [ -d "/Applications/${APP_NAME}.app" ]; then
  echo "Removing existing /Applications/${APP_NAME}.app"
  rm -rf "/Applications/${APP_NAME}.app"
fi

if [ -d "$HOME/Applications/${APP_NAME}.app" ]; then
  echo "Removing existing ~/Applications/${APP_NAME}.app"
  rm -rf "$HOME/Applications/${APP_NAME}.app"
fi

echo "Installing ${APP_NAME}.app to /Applications..."
if cp -R "${SCRIPT_DIR}/${APP_NAME}.app" "/Applications/" 2>/dev/null; then
  chmod +x "/Applications/${APP_NAME}.app/Contents/MacOS/${APP_NAME}"
  echo "Installed to /Applications/${APP_NAME}.app"
else
  echo "Cannot write to /Applications. Installing to ~/Applications instead..."
  mkdir -p "$HOME/Applications"
  cp -R "${SCRIPT_DIR}/${APP_NAME}.app" "$HOME/Applications/"
  chmod +x "$HOME/Applications/${APP_NAME}.app/Contents/MacOS/${APP_NAME}"
  echo "Installed to $HOME/Applications/${APP_NAME}.app"
fi
