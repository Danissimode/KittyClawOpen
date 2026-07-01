#!/usr/bin/env bash
set -euo pipefail

echo "=== Beaver Board Public Baseline Audit ==="
FAIL=0

check() {
  local pattern="$1"
  local label="$2"
  local matches
  matches=$(rg -n "$pattern" . \
    --glob '!bin/**' \
    --glob '!obj/**' \
    --glob '!dist/**' \
    --glob '!artifacts/**' \
    --glob '!.git/**' \
    --glob '!node_modules/**' \
    --glob '!packaging/**' \
    --glob '!scripts/audit/**' \
    2>/dev/null || true)
  if [ -n "$matches" ]; then
    echo "---"
    echo "FAIL: $label"
    echo "$matches"
    FAIL=1
  fi
}

check_warn() {
  local pattern="$1"
  local label="$2"
  local matches
  matches=$(rg -n "$pattern" . \
    --glob '!bin/**' \
    --glob '!obj/**' \
    --glob '!dist/**' \
    --glob '!artifacts/**' \
    --glob '!.git/**' \
    --glob '!node_modules/**' \
    --glob '!packaging/**' \
    --glob '!scripts/audit/**' \
    2>/dev/null || true)
  if [ -n "$matches" ]; then
    echo "---"
    echo "WARN: $label"
    echo "$matches"
  fi
}

# Hard blocks — must not exist
check "/Users/danissimode"            "private local user path"
check "Documents/GitHub"              "private local repo path"
check "PetPals"                       "private project reference"
check "DEEPSEEK_API_KEY"              "secret-like key reference"
check "OPENAI_API_KEY"                "secret-like key reference"
check "ANTHROPIC_API_KEY"             "secret-like key reference"
check "MISTRAL_API_KEY"               "secret-like key reference"

# KittyClaw — allowed only in specific attribution/internal contexts
echo "---"
echo "Checking KittyClaw references..."
KITTY_MATCHES=$(rg -n "KittyClaw|kittyclaw" . \
  --glob '!bin/**' \
  --glob '!obj/**' \
  --glob '!dist/**' \
  --glob '!artifacts/**' \
  --glob '!.git/**' \
  --glob '!node_modules/**' \
  --glob '!packaging/**' \
  --glob '!docs/ATTRIBUTION.md' \
  --glob '!scripts/audit/**' \
  --glob '!KittyClaw.*/**' \
  --glob '!**/*.sln' \
  --glob '!**/*.csproj' \
  --glob '!global.json' \
  2>/dev/null || true)
if [ -n "$KITTY_MATCHES" ]; then
  echo "FAIL: KittyClaw reference found in user-facing or packaging-facing files"
  echo "$KITTY_MATCHES"
  FAIL=1
else
  echo "OK: KittyClaw references are confined to project files and attribution docs"
fi

# Warnings — should be reviewed but may not block
check_warn "claude-3|gpt-4|deepseek|mistral|kimi|opencode" "model/provider name in public-facing content"

if [ "$FAIL" -eq 0 ]; then
  echo ""
  echo "✅ Public baseline audit passed."
  exit 0
else
  echo ""
  echo "❌ Public baseline audit failed. Clean up the issues above."
  exit 1
fi
