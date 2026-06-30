# Beaver Board Kanban

<p align="center">
  <img src="branding/beaver-board/beaver-icon.svg" alt="Beaver Board Kanban" width="64" />
</p>

<h1 align="center">Run your AI team from <span style="color: #F97316">one board.</span></h1>

<p align="center">
  <strong>A Kanban board where your AI agents become your development team.</strong><br/>
  Connect task cards to OpenCode sessions and shared command chat.
</p>

**Beaver Board Kanban** is a Kanban orchestrator for solo developers who coordinate AI coding agents. It connects task cards, agent status, shared command chat, blockers, approvals, and execution evidence in one developer workflow.

---

## Visual Preview

<p align="center">
  <img src="docs/assets/generated/hero-banner.png" alt="Beaver Board Kanban Hero" width="800" />
  <br>
  <sub>↑ Beaver Board Kanban: Run your AI team from one board</sub>
</p>

<p align="center">
  <img src="docs/assets/generated/architecture-flow.png" alt="OpenCode Integration Flow" width="800" />
  <br>
  <sub>↑ Task cards connect to AI agents, which execute through OpenCode</sub>
</p>

<p align="center">
  <img src="docs/assets/generated/ui-closeup.png" alt="Beaver Board Kanban UI" width="800" />
  <br>
  <sub>↑ Everything in context: tasks, code changes, and AI conversations</sub>
</p>

> **Note**: PNG assets are generated from the [readme-assets-generator-3.html](docs/assets/readme-assets-generator-3.html) template. See the [branding guide](docs/branding-guide.md) for details.

---

## Why this exists

AI coding workflows are fragmented. Tasks live in boards, agents run in terminals, decisions happen in chat, and evidence is scattered across logs, diffs, and PRs. Beaver Board Kanban brings these into one orchestrated workflow.

## Core idea

Each card is not just a task. It can become an executable workflow connected to an AI agent, shared command chat, status updates, blockers, approvals, and evidence.

## Key features

- Kanban board for AI-assisted development
- AI-agent task assignment
- Two-way task-agent communication
- Shared command chat with agents
- OpenCode-first workflow direction
- Agent blockers and approval requests
- Task-linked evidence (logs, diffs, tests, reports, PRs)
- Solo developer orchestration
- Fork-friendly open-source base

---

## OpenCode integration

### OpenCode-first direction

Beaver Board Kanban is designed with OpenCode as the primary execution layer. The board sends task context to OpenCode agents and receives progress, blockers, and evidence back into the board.

> **Status:** This integration is planned and under active development. See [docs/OpenCode-Integration.md](docs/OpenCode-Integration.md) for the full integration architecture and roadmap.

---

## Example workflow

1. Create a card: `BB-102 Refactor Auth API`
2. Assign it to `Agent-Roo`
3. Send task context to OpenCode
4. Agent moves the card to `In Progress`
5. Agent asks a question in shared command chat
6. Developer approves the decision
7. Agent attaches test output and report
8. Card moves to `Review`

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude Code CLI](https://docs.claude.com/en/docs/claude-code/overview) — `claude` on your PATH
- [Git](https://git-scm.com/downloads) — `git` on your PATH

On first launch an onboarding popup detects whether `claude` and `git` are available. You can continue without them, but agent runs and auto-commits will fail until they are installed and on the PATH.

### Run

From the repo root:

```
run.bat        (Windows)
./run.sh       (macOS / Linux)
```

Both wrap `dotnet watch --project KittyClaw.Web --non-interactive` and serve Beaver Board Kanban at **http://localhost:5230** with hot reload enabled.

### Creating a project

From the home page, type a name and click **Create**. A popup asks you to set a workspace folder (absolute path to a repo/folder) and offers to create it if missing. Click **Initialize** to:

1. Create the project registry entry + per-project SQLite DB.
2. Copy the project template from `ProjectTemplate/` (`preamble.md`, `{agent}/SKILL.md`, `{agent}/memory/MEMORY.md` index, `memory-consolidation.md`, `automations.json`, `CLAUDE.md`) into the workspace — agent files under `<workspace>/.agents/`, `CLAUDE.md` at the workspace root.
3. Run `git init` if the workspace is not already a git repo (skipped if `git` isn't installed).
4. Create a member for each agent slug found in the template.
5. Navigate to the board.

The workspace folder itself is never deleted by Beaver Board, even when you delete a project.

### Data Storage

All Beaver Board data is stored locally in `%APPDATA%/BeaverBoard/` (configurable via `BEAVERBOARD_DATA_DIR`, falls back to `KITTYCLAW_DATA_DIR` for existing setups):

- `registry.db` — project registry
- `projects/{slug}.db` — per-project database (tickets, comments, labels, columns, members)
- `uploads/` — uploaded images
- `runs/{runId}.json` — agent run snapshots (events, status, exit code)
- `settings.json` — language + onboarding flag

> **Migration:** If you were using `%APPDATA%/KittyClaw/`, Beaver Board will automatically pick up your existing data via the `KITTYCLAW_DATA_DIR` fallback. A clean install uses `%APPDATA%/BeaverBoard/` by default.

Per-project agent state lives **in the workspace**: `<workspace>/.agents/{agent}/memory/` (scored `MEMORY.md` index + per-topic lesson files), `<workspace>/.agents/channel/` (session state), etc.

---

## Project Structure

> **Note:** Internal namespaces (`KittyClaw.*`) are retained for upstream compatibility with the parent repository. The product-facing identity is **Beaver Board Kanban**.

| Path | Description |
|---|---|
| **KittyClaw.Core** | Domain models, EF Core contexts, services, automation engine, embedded project template |
| **KittyClaw.Core.Tests** | xUnit tests (conditions, triggers, signals, JSON polymorphism) |
| **KittyClaw.Web** | Blazor Server UI + REST API |
| **KittyClaw.QaRunner** | Isolated test-instance launcher (Playwright + scenario runner) used by the qa-tester agent |
| **KittyClaw.ClaudeMock** | Mock `claude` CLI used by `KittyClaw.QaRunner` for hermetic agent dispatch in tests |
| **ProjectTemplate/** | Source of truth for new-project initialization. Files under `Agents/` are written to `<workspace>/.agents/`; `CLAUDE.md` is written to the workspace root. |
| **tools/** | Repo helpers (e.g. `publish-stable.ps1` to bundle Web + QaRunner + ClaudeMock for a stable channel) |

---

## Tech Stack

- **.NET 10** / **Blazor Server** (interactive SSR)
- **SQLite** via Entity Framework Core (one DB per project)
- **OpenAPI** with auto-generated Markdown docs
- External: **[Claude Code CLI](https://docs.claude.com/en/docs/claude-code/overview)** + **[Git](https://git-scm.com/downloads)** (required on PATH for agent dispatch and auto-commits)

---

## Architecture

Per-feature architecture documentation lives under [`doc/`](doc/index.md). Start at `doc/index.md` for an indexed map of the automation engine, agent dispatch, project template, REST API, storage, and Kanban UI.

---

## API

All endpoints are under `/api`. The documentation is auto-generated from the live OpenAPI spec:

- Human-readable Markdown: `GET http://localhost:5230/api/docs`
- Machine-readable JSON: `GET http://localhost:5230/openapi/v1.json`

---

## For AI Agents

This app is designed to be operated by AI agents through its REST API. Here's how to get started:

1. **Read the live API docs** at `http://localhost:5230/api/docs` — every endpoint, request/response example, and schema, always up to date with the running server.
2. **Identify yourself** — `author` is **required** on every mutating endpoint; omitting it returns HTTP 400. Use your plain agent name (e.g. `"programmer"`, `"groomer"`). The human user is `"owner"`.
3. **Discover the board** — call `GET /api/projects` first, then `GET /api/projects/{slug}/columns` to learn the workflow stages and `GET /api/projects/{slug}/members` for assignable members.
4. **Use the right status** — ticket statuses must match existing column names. Fetch columns before moving tickets.
5. **Track your work** — add comments on tickets to explain what you did or what you need. Use `@mentions` to notify members, `#id` to reference tickets in the same project, and `#{slug}:{id}` to reference tickets in another project.
6. **Labels & priority** — use `GET /api/projects/{slug}/labels` to discover available labels, and set priority to `Idea`, `NiceToHave`, `Required`, or `Critical`.
7. **Check mentions** — call `GET /api/projects/{slug}/mentions/{your-handle}` to find tickets that mention you.
8. **Sub-tickets** — set `parentId` when creating a ticket to make it a child. Use `PUT /api/projects/{slug}/tickets/{id}/parent` to reparent, or `DELETE` it to detach. List sub-tickets with `?parentId={id}`.

### Conventions

- **Author format**: `"owner"` for the human user, plain agent name (e.g. `"programmer"`) for AI agents
- **Priority levels**: `Idea`, `NiceToHave`, `Required`, `Critical`
- **Default column**: `Backlog`

---

## UI Features

- Onboarding popup on first launch with Claude Code + Git detection
- Project creation popup with workspace selection + one-click agent template initialization
- Kanban board with drag-and-drop
- Customizable dashboard view with free-drag tiles (Markdown, KPI, charts, Heatmap, Timeline, ...), AI chat-based tile creation, and auto-refresh via LLM prompts
- Ticket detail panel with comments and activity timeline
- Live agent run drawer (SSE stream of Claude Code output, steer + stop controls)
- New-instruction chat drawer to send an ad-hoc prompt to an agent
- Automations page: list, enable/disable, edit (triggers / conditions / actions), reload from disk, re-initialize agent template
- Markdown rendering with `@mention`, `#id`, and `#{slug}:{id}` cross-project ticket reference support
- Advanced search syntax: `#42`, `@owner`, `>date`, `priority:critical`, `label:bug`, `by:owner`
- Sub-tickets with parent/child relationships and progress tracking
- Column management (create, reorder, customize colors)
- Label and member management
- Image upload in descriptions and comments

---

## Dashboard

Each project has a customizable **Dashboard** view alongside the kanban board. Tiles are free-dragged, auto-refresh on a schedule, and can be created or edited from the in-app AI chat panel — the agent writes the tile's folder for you.

<p align="center">
  <img src="docs/assets/dashboard.png" alt="Beaver Board Kanban dashboard" width="800" />
</p>

### Tile types

| Template id   | What it renders                                                       |
| ------------- | --------------------------------------------------------------------- |
| `markdown`    | Free-form Markdown content                                            |
| `table`       | Tabular data with headers and rows                                    |
| `kpi`         | Single large number with label and optional delta                     |
| `kpi-grid`    | Grid of multiple KPI cards                                            |
| `progress`    | Progress bar with current / target values                             |
| `sparkline`   | Compact inline trend line                                             |
| `bar-chart`   | Vertical or horizontal bar chart                                      |
| `donut`       | Donut / pie chart of categorical proportions                          |
| `gauge`       | Radial gauge for a bounded value                                      |
| `status-grid` | Grid of colored status pills (up/down/warn)                           |
| `heatmap`     | Calendar-style heatmap of intensity over time                         |
| `leaderboard` | Ranked list with scores                                               |
| `timeline`    | Chronological list of events                                          |
| `image`       | Static or refreshed image                                             |
| `mermaid`     | Mermaid diagram (flowchart, sequence, ...)                            |

### Folder layout

Each tile lives in its own folder under `.dashboard/` in the project workspace:

```
.dashboard/
  <tile-slug>/
    tile.yaml        # template, title, refresh schedule, prompt
    script.ps1       # optional refresh script (or script.sh, script.py, ...)
    output.json      # last refresh output consumed by the template
```

### `tile.yaml` key fields

- `template` — one of the ids in the table above.
- `title` — display name shown in the tile header.
- `refresh` — interval (e.g. `5m`, `1h`) for periodic refresh.
- `refreshAt` — cron-style time-of-day refresh (alternative to `refresh`).
- `prompt` — instructions sent to the agent when (re)generating `output.json`.

Tiles can be created from the dashboard's AI chat panel by describing what you want — the agent picks a template, writes `tile.yaml`, generates the refresh script, and produces the initial `output.json`.

---

## Automation model

- **Triggers**: `interval`, `ticketInColumn`, `statusChange`, `subTicketStatus`, `ticketCommentAdded`, `gitCommit`, `boardIdle`, `agentInactivity`.
- **Conditions**: `ticketInColumn`, `ticketCountInColumn`, `fieldLength`, `priority`, `labels`, `assignedTo`, `hasParent`, `allSubTicketsInStatus`, `ticketAge`.
- **Actions**: `runAgent`, `moveTicketStatus`, `setLabels`, `assignTicket`, `addComment`, `consolidateAgentMemory`, `commitAgentMemory`, `executePowerShell`.
- `{assignee}` placeholder in `runAgent.agent` / `runAgent.concurrencyGroup` resolves from the firing ticket's `assignedTo`.
- Canonical post-run chain: `runAgent` -> `consolidateAgentMemory` (focused claude pass that curates the agent's `memory/` index + topic files) -> `commitAgentMemory` (commits the result).

---

## Project status

This is an experimental open-source fork/customization. The visual branding and GitHub Pages landing are being developed first, followed by deeper OpenCode integration and agent-command workflow.

---

## Roadmap

### Phase 1 — Branding and GitHub presentation

- Beaver Board Kanban identity
- README rewrite
- GitHub Pages landing
- Visual assets
- Removal of obsolete KittyClaw-facing branding

### Phase 2 — Kanban UX polish

- Improved task cards
- Developer-focused dark theme
- Agent metadata on cards
- Status labels and task states

### Phase 3 — Shared command chat

- Agent mentions
- Task-linked conversations
- Human approval flow
- Blocker reporting

### Phase 4 — OpenCode integration

- Link cards to OpenCode sessions
- Send task context to agents
- Receive status updates
- Capture execution evidence

### Phase 5 — Agent orchestration

- Agent-driven card movement
- PR/evidence linking
- Multi-agent workflow
- Optional support for other CLI executors

---

## Attribution

Beaver Board Kanban started as a fork/custom adaptation based on [KittyClaw](https://github.com/Ekioo/KittyClaw) and is being redesigned into a Kanban orchestrator for AI-agent-assisted development.

---

## License

MIT — see [LICENSE](LICENSE) for details.
