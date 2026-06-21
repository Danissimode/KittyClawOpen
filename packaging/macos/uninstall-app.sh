#!/usr/bin/env bash
set -euo pipefail
APP_NAME="KittyClawOpen"
PURGE=false

for arg in "$@"; do
  if [ "$arg" = "--purge" ]; then
    PURGE=true
  fi
done

if [ -d "/Applications/${APP_NAME}.app" ]; then
  rm -rf "/Applications/${APP_NAME}.app"
  echo "Removed /Applications/${APP_NAME}.app"
fi

if [ -d "$HOME/Applications/${APP_NAME}.app" ]; then
  rm -rf "$HOME/Applications/${APP_NAME}.app"
  echo "Removed $HOME/Applications/${APP_NAME}.app"
fi

if [ "$PURGE" = true ]; then
  rm -rf "$HOME/Library/Application Support/KittyClawOpen"
  rm -rf "$HOME/Library/Logs/KittyClawOpen"
  echo "Purged user data and logs"
fi

echo "Uninstalled ${APP_NAME}"
