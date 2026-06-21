# KittyClawOpen CLI Runtimes

## Supported Runtimes

| Runtime | CLI Command | Status | Safety |
|---------|-------------|--------|--------|
| Mimo Code | `mimo run` | **Production #1** | `--dangerously-skip-permissions` blocked |
| Script | `pnpm governance:verify` | **Enabled** | Safe (no AI) |
| OpenCode | `opencode run` | Skeleton (disabled) | Verify locally before enabling |
| Codex | `codex exec` | Skeleton (disabled) | `--dangerously-bypass-approvals-and-sandbox` blocked |
| Vibe | `vibe --prompt` | Skeleton (disabled) | Auto-approve guarded; prefer `--agent plan` |
| Kimi Code | `kimi -p` | Skeleton (disabled) | `-p` mode auto-approves regular tools; verify locally |
| GitHub Copilot | `copilot --prompt` | Experimental (disabled) | Optional, not core architecture |
| Antigravity | `agy -p` | **Disabled** (unsafe) | Auto-approves tool calls including writes |
| Claude Code | `claude --print` | Legacy wrapper | Preserved for backward compat |

## Runtime Selection

Config-driven mapping:

```json
{
  "runtimeByMember": {
    "codex": "mimo-code",
    "mimo": "mimo-code",
    "opencode": "opencode",
    "vibe": "vibe",
    "kimi": "kimi-code",
    "copilot": "github-copilot",
    "antigravity": "antigravity",
    "qa": "script"
  }
}
```

Never hardcode:
```csharp
// WRONG
if (assignee == "codex") RunMimo();

// CORRECT
var runtimeId = config.RuntimeByMember[assignee] ?? config.DefaultRuntime;
var runtime = router.Resolve(runtimeId);
```

## Safety Guards

### Dangerous Flags (centrally blocked)
- `--dangerously-skip-permissions` (Mimo)
- `--dangerously-bypass-approvals-and-sandbox` (Codex)
- `-y`, `--yolo`, `--auto-approve` (Kimi)

### High-Risk Labels
- `security`, `rls`, `payments`, `stripe`, `critical`
- Max automatic status: `Review`
- Adds comment: "High-risk ticket: human review required."

### Status Policy
- `Ready → InProgress` (before execution)
- `InProgress → Review` (success)
- `InProgress → Blocked` (failure)
- `InProgress → ChangesRequested` (validation failure)
- **Never** auto-move to `Verified` or `Done`

## macOS App Launcher

```bash
bash packaging/macos/install-app.sh
open /Applications/KittyClawOpen.app
```

- Opens `http://localhost:8080` (PWA) or falls back to `http://localhost:5230`
- Detects existing backend to avoid duplicates
- Logs: `~/Library/Logs/KittyClawOpen/`
- State: `~/Library/Application Support/KittyClawOpen/`

## Smoke Tests

```bash
# Mimo
mimo run "Reply with exactly: KITTYCLAW_MIMO_OK" --format json --title "Smoke"

# OpenCode
opencode run "Reply with exactly: KITTYCLAW_OPENCODE_OK"

# Codex
codex exec "Reply with exactly: KITTYCLAW_CODEX_OK"

# Vibe
vibe --prompt "Reply with exactly: KITTYCLAW_VIBE_OK" --max-turns 3 --output json --agent plan

# Kimi
kimi -p "Reply with exactly: KITTYCLAW_KIMI_OK" --output-format stream-json

# Copilot
copilot --prompt "Reply with exactly: KITTYCLAW_COPILOT_OK" --output-format=json --no-remote
```

Do **not** run Antigravity smoke with write permissions until safety is verified.
