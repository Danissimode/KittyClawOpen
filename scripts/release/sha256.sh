#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DIST="$ROOT/dist"

echo "=== SHA-256 Checksums ==="
shasum -a 256 "$DIST"/*.dmg > "$DIST/checksums.txt"
cat "$DIST/checksums.txt"
