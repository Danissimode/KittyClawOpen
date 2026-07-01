# KittyClaw Upstream Intake

## Overview

KittyClaw is the historical upstream project from which Beaver Board was derived.
This document establishes a controlled, manual process for importing useful upstream
changes without leaking branding, private paths, or user-specific configuration.

## Principles

- **KittyClaw is upstream, not identity.** Beaver Board is a separate public product.
- **No blind merges.** Every upstream commit is reviewed, classified, and adapted.
- **Public baseline first.** Before any upstream import, `scripts/audit/public-baseline.sh` must pass.
- **Preserve user data.** Migration is copy, not move.

## Remote Setup

```bash
git remote add upstream-kittyclaw <KITTYCLAW_REPO_URL>
```

Use `upstream-kittyclaw`, not `upstream`, to avoid confusion with Beaver Board's own upstream.

## Classification

| Decision | Meaning |
|----------|---------|
| `ADOPT` | Import as-is (rare — only truly generic fixes) |
| `ADAPT` | Import with modifications (branding, paths, public baseline) |
| `REWRITE` | Take the idea, implement independently |
| `SKIP` | Not relevant to Beaver Board |
| `BLOCK` | Dangerous, private, or product-damaging |

## Intake Procedure

1. **Fetch**
   ```bash
   git fetch upstream-kittyclaw
   ```

2. **Review**
   ```bash
   git log --oneline --decorate main..upstream-kittyclaw/main
   ```

3. **Inspect each commit**
   ```bash
   git show --stat <commit>
   git show <commit>
   ```

4. **Branch**
   ```bash
   git checkout -b upstream/kittyclaw-sync-$(date +%Y-%m-%d)
   ```

5. **Cherry-pick without auto-commit**
   ```bash
   git cherry-pick -n <commit>
   ```

6. **Clean**
   ```bash
   ./scripts/audit/public-baseline.sh
   ```
   Fix all failures.

7. **Test**
   ```bash
   dotnet build
   dotnet test
   ```

8. **Commit with Beaver Board context**
   ```bash
   git commit -m "fix(board): adapt upstream card rendering stability patch"
   ```
   In commit body:
   ```
   Adapted from KittyClaw upstream commit <hash>.
   Changes cleaned for Beaver Board branding, storage paths, and public packaging.
   ```

9. **Record**
   Create or update `.memory/upstream/kittyclaw-intake-YYYY-MM-DD.md`.

## Intake Record Template

Create `.memory/upstream/kittyclaw-intake-YYYY-MM-DD.md`:

```markdown
# KittyClaw Upstream Intake — YYYY-MM-DD

## Source
- Remote: upstream-kittyclaw
- Source branch: main
- Compared against: main
- Commit range: abc123..def456

## Summary
| Commit | Area | Decision | Reason |
|--------|------|----------|--------|
| abc123 | Board | ADAPT | Useful but contains KittyClaw naming |
| def456 | Docs | SKIP | Not relevant |
| ghi789 | Security | ADOPT | Generic security fix |

## Adopted
- ...

## Adapted
- ...

## Skipped
- ...

## Blocked
- ...

## Required cleanup
- [ ] rg KittyClaw
- [ ] rg PetPals
- [ ] rg /Users/
- [ ] rg model names
- [ ] run tests
- [ ] run packaging smoke test
```

## Guardrails

- Run `scripts/audit/public-baseline.sh` before every commit.
- Never commit upstream commit messages verbatim.
- Never expose `/Users/danissimode`, `Documents/GitHub`, `PetPals`, or private model configs.
- If a commit touches `SettingsService`, `Program.cs`, or `BeaverBoardPaths`, double-check platform paths.
