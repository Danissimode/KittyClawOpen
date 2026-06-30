# Feature Status Matrix

This document is the source of truth for what is actually implemented vs. what is planned/stub/future. Updated after each release.

Last updated: 2026-06-30

## Status Legend

| Status | Meaning |
|--------|---------|
| **Done** | Fully implemented, user-facing, tested |
| **Partial** | Implemented but incomplete UX or known gaps |
| **Stub** | Interface/class exists but no functional implementation |
| **Future** | Described in docs but no code yet |
| **N/A** | Not applicable |

---

## Core Board

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Kanban board (columns, cards, drag-drop) | **Done** | ✅ | Full CRUD, reorder, custom columns |
| Labels | **Done** | ✅ | Color-coded, multi-select |
| Members / agents | **Done** | ✅ | With avatar and status |
| Comments | **Done** | ✅ | @mentions, #ticket refs |
| Sub-tickets | **Done** | ✅ | parentId, hierarchy |
| Priority | **Done** | ✅ | Idea → Critical |
| Blocker / approval flags | **Done** | ✅ | Blocking tickets |
| Card drawer | **Done** | ✅ | Tabs: Details, Evidence, Chat, Execution |
| Dashboard tiles | **Partial** | ✅ | markdown, kpi, table, progress, charts, heatmap, timeline, mermaid; AI chat tile creation works; refresh via LLM prompt |
| Advanced search | **Partial** | ✅ | Full-text across tickets; UI complete |
| Image upload | **Done** | ✅ | Via chat description, stored in data dir |

---

## Automation Engine

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Triggers: interval | **Done** | ✅ | |
| Triggers: ticketInColumn | **Done** | ✅ | |
| Triggers: statusChange | **Done** | ✅ | |
| Triggers: gitCommit | **Done** | ✅ | |
| Triggers: boardIdle | **Done** | ✅ | |
| Triggers: agentInactivity | **Done** | ✅ | |
| Conditions (all types) | **Done** | ✅ | ticketLabel, ticketPriority, columnCardCount, timeRange, random, ... |
| Actions: runAgent | **Done** | ✅ | |
| Actions: moveTicketStatus | **Done** | ✅ | |
| Actions: addComment | **Done** | ✅ | |
| Actions: consolidateAgentMemory | **Done** | ✅ | |
| Actions: commitAgentMemory | **Done** | ✅ | |
| Actions: executePowerShell | **Done** | ✅ | **Off by default** (security) |
| Actions: createTicket | **Done** | ✅ | |
| Automation editor UI | **Partial** | ✅ | TriggerEditor, ConditionEditor, ActionEditor all exist |
| In-memory chain guard (prevents duplicate runs) | **Partial** | ⚠️ | Not serialized to disk; lost on app restart |

---

## Agent Runners

| Runner | Status | User-facing | Notes |
|--------|--------|-------------|-------|
| ClaudeRunner (claude CLI) | **Done** | ✅ | Full streaming, stop, steer, max_turns, ask_user_question |
| OpenCodeRunner | **Partial** | ✅ | CLI mode works; server mode falls back to CLI; steering via temp file; prompt as inline arg |
| RunnerRegistry | **Done** | ✅ | Abstraction over runners; default selection |
| RunnerAvailabilityChecker | **Done** | ✅ | Detects which runners are installed |
| ClaudeCodeRuntime | **Done** | ⚠️ | Registered but internal |
| MimoCodeRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| ScriptRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| CodexRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| GitHubCopilotRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| AntigravityRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| VibeRuntime | **Stub** | ❌ | Registered in DI, no real implementation |
| KimiCodeRuntime | **Stub** | ❌ | Registered in DI, no real implementation |

---

## Execution / OpenCode Integration

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Execution Tab in ticket drawer | **Partial** | ✅ | Shows runner, status, worktree; buttons (Start/Stop/Steer) present |
| Worktree per card | **Partial** | ✅ | WorktreeService exists; branch per ticket; cleanup not automatic |
| Provider/model catalog | **Done** | ✅ | OpenCodeProviderModelCatalog with OpenAI, Anthropic, OpenRouter, Ollama, Mistral, Gemini, DeepSeek |
| Execution metadata storage | **Partial** | ⚠️ | Stored in AgentRun; SQLite persistence not wired |
| Steering (Claude) | **Done** | ✅ | Via temp file; ResumeSteerMessages pattern works |
| Steering (OpenCode CLI) | **Partial** | ⚠️ | Steering via temp file (`.agents/tmp/steer-{runId}.txt`); requires OpenCode to poll this file |
| Done Gate | **Future** | ❌ | Config schema exists in OpenCode-Integration.md; not enforced in code |
| CAO Governance | **Future** | ❌ | CaoGoverned execution mode in docs; no runner implementation |
| TeamWorkflow (multi-agent) | **Future** | ❌ | Documented in OpenCode-Integration.md; no implementation |
| Manual execution mode | **Done** | ✅ | Available in execution mode selector |
| Quota/cost tracking | **Done** | ✅ | CostTracker persists to RunLogStore |
| Failure logbook | **Done** | ✅ | FailureLogStore backed by SQLite |

---

## Team Chat

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Shared command chat (TeamChatDock) | **Done** | ✅ | Dockable panel; agent attribution |
| Agent ↔ agent messaging | **Done** | ✅ | Via @mentions and steering bridge |
| TeamCommandRouter | **Done** | ✅ | Routes commands to agents |
| AgentChatPolicyService | **Partial** | ⚠️ | Registered; policy enforcement not fully wired |
| Persistent chat across runs | **Done** | ✅ | ChatService backed by SQLite |

---

## Security

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| CORS: LocalOnly whitelist | **Done** | ✅ | localhost + 127.0.0.1 only |
| executePowerShell off-by-default | **Done** | ✅ | `EnabledByDefault: false` in ActionSpec |
| Security banner (local-only warning) | **Done** | ✅ | Dismissable; persisted in settings |
| Public repo safety guide | **Done** | ✅ | `docs/public-repo-safety.md` |
| Health endpoint | **Done** | ✅ | `GET /api/health` — no sensitive data |
| Secrets scanning | **N/A** | — | No CI configured yet |
| Dashboard script execution | **Partial** | ⚠️ | DashboardScriptRunner exists; no policy gate |

---

## Data / Storage

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| SQLite per-project DB | **Done** | ✅ | |
| %APPDATA%/BeaverBoard/ | **Done** | ✅ | Default path as of v0.9 |
| %APPDATA%/KittyClaw/ fallback | **Done** | ✅ | KITTYCLAW_DATA_DIR env var |
| Agent memory in workspace | **Done** | ✅ | `<workspace>/.agents/` |
| Run snapshots (JSON) | **Done** | ✅ | runs/{runId}.json |
| Onboarding detection | **Done** | ✅ | Claude CLI + Git detection on first launch |

---

## UI / UX

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Blazor Server (SSR) | **Done** | ✅ | Interactive components |
| Dark theme | **Done** | ✅ | CSS vars, system/light/dark |
| Drag-and-drop | **Done** | ✅ | |
| Live run drawer (AgentRunDrawer) | **Done** | ✅ | SSE streaming events |
| RunnerStatusBar | **Done** | ✅ | Shows running agents |
| Toast notifications | **Done** | ✅ | |
| Escape key stack | **Done** | ✅ | |
| Reconnect modal | **Done** | ✅ | |
| Onboarding popup | **Done** | ✅ | |
| Project creation with workspace picker | **Done** | ✅ | |
| Settings: language, theme, preferred runner | **Done** | ✅ | |
| OpenCode project settings | **Partial** | ✅ | Provider, model, agent, auth status shown |
| CI pass/fail badge (from update check) | **Done** | ✅ | UpdateBanner shows latest release |
| Board filter / sort state | **Done** | ✅ | Per-session, persisted in component state |

---

## API / OpenAPI

| Feature | Status | User-facing | Notes |
|---------|--------|-------------|-------|
| Full REST API | **Done** | ✅ | All CRUD for projects, tickets, columns, labels, members, chats |
| OpenAPI JSON spec | **Done** | ✅ | `/openapi/v1.json` |
| Markdown API docs | **Done** | ✅ | `/api/docs` |
| SSE board events | **Done** | ✅ | `/api/projects/{slug}/events` |
| Auth: `author` required on mutating endpoints | **Done** | ✅ | HTTP 400 if missing |
| Per-project automation API | **Done** | ✅ | |

---

## Docs

| Document | Status | Notes |
|----------|--------|-------|
| README.md | **Done** | Clear value prop; honest about OpenCode status |
| README.OpenCode.md (root) | **Partial** | Duplicate of docs/OpenCode-Integration.md — candidate for removal |
| doc/index.md | **Done** | Architecture map |
| doc/agent-dispatch.md | **Done** | Claude-centric; accurate |
| doc/automation-engine.md | **Done** | Covers triggers, conditions, actions |
| doc/dashboard.md | **Done** | Tile types, creation flow |
| doc/kanban-ui.md | **Partial** | Needs updates post UI redesign |
| doc/storage.md | **Partial** | Still mentions KittyClaw paths |
| doc/project-template.md | **Done** | Accurate |
| doc/worktree-workflow.md | **Done** | |
| doc/rest-api.md | **Done** | Accurate |
| doc/update-check.md | **Done** | |
| doc/ui-ux-master-plan-v2.md | **Future** | Historical, superceded |
| doc/graphic-charter.md | **Done** | Branding guide |
| docs/OpenCode-Integration.md | **Done** | 758 lines; comprehensive but some future sections |
| docs/public-repo-safety.md | **Done** | |
| docs/Runner-API.md | **Partial** | |
| docs/integrations/mcp-security-boundary.md | **Future** | MCP docs |

---

## Infrastructure

| Feature | Status | Notes |
|---------|--------|-------|
| .NET 10 build | **Done** | `dotnet build` passes, 0 errors |
| Tests: core automation | **Partial** | Some tests exist; timeout issues in CI |
| Tests: OpenCode runner | **Partial** | OpenCodeRunnerTests.cs exists |
| Release: run.sh / run.bat | **Done** | dotnet watch wrapper |
| tools/publish-stable.ps1 | **Partial** | Exists; not tested end-to-end |
| GitHub Actions CI | **N/A** | Not configured yet |

---

## Known Issues

1. **Board._liveProgress warning** — unused field in Board.razor (CS0649, non-blocking)
2. **OpenCode CLI steering** — temp file approach works but OpenCode must actively poll the file; no SSE/websocket path confirmed
3. **Automation chain guard** — in-memory only; duplicate runs possible after app restart
4. **docs/ vs doc/** — two doc directories; `docs/` is KittyClaw-style, `doc/` is BeaverBoard-style; creates confusion
5. **README.OpenCode.md** — root-level duplicate of `docs/OpenCode-Integration.md`
6. **8 stub runtimes registered** — Mimo, Script, Codex, GitHubCopilot, Antigravity, Vibe, Kimi — registered in DI but non-functional

---

## Next Milestone: v1.0 (MVP Complete)

See `doc/roadmap-v1.md` for the full v1.0 plan.
