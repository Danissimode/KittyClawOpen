# Implementation Status

_Generated: 2026-06-28 | Branch: main | Commit: 86246ad_

## Build & Test Status

| Check | Status | Evidence |
|-------|--------|----------|
| Build | ✅ | `dotnet build` — 0 errors, 6 warnings (pre-existing) |
| TeamChat Tests | ✅ | 28/28 pass |
| Branding | ✅ | User-facing: Beaver Board, Technical: KittyClaw |

## Feature Matrix

| Feature | Status | Files | Tests | Notes |
|---------|--------|-------|-------|-------|
| **Beaver Board Branding** | ✅ Done | `App.razor`, `Home.razor`, `app.css`, `branding/`, `README.md` | — | Title, favicon, logos, CSS theme, localization |
| **Team Chat Dock** | ✅ Done | `TeamChatDock.razor`, `TeamChatState.cs` | 7 | Fixed bottom-right, filters, composer |
| **Role-Aware Agent Communication** | ✅ Done | `AgentRole.cs`, `AgentCommunicationService.cs`, `AgentChatProfile.cs`, `AgentRoleChatPolicy.cs` | 28 | Profiles, policies, signal filter |
| **Mention Parser** | ✅ Done | `TeamChatMentionParser.cs` | 11 | @team, @role, @agent, #ticket, run:id |
| **Signal Filter** | ✅ Done | `AgentChatSignalFilter.cs` | 10 | Important vs noisy, role policy |
| **OpenCode Integration** | ✅ Done | `KittyClaw.Core/Integrations/OpenCode/` | — | Zone A + Zone B architecture |
| **Ticket Auto-Run** | ✅ Done | `TicketAutoRunService.cs` | — | InProgress transition, plan gate, runner dispatch |
| **FailureLogbook** | ⚠️ In-Memory | `FailureLogStore.cs` | — | ConcurrentDictionary, needs SQLite migration |
| **Ticket Plan/Execution Fields** | ✅ Done | `Ticket.cs` | — | PlanStatus, RequiresPlan, ExecutionModeOverride, ProviderOverride, ModelOverride, ProfileOverride, UseWorktree, OpenCodeAgent |
| **API Endpoints** | ✅ Done | `Endpoints.TeamChat.cs`, `Endpoints.Failures.cs` | — | Messages, inbox, agent-chat, role-policies |

## Technical Debt

| Item | Priority | Impact |
|------|----------|--------|
| FailureLogStore uses ConcurrentDictionary | P5 | Failures lost on restart |
| TicketAutoRunService has no idempotency guard | P4 | Duplicate runs possible |
| No Done/Review gate enforcement | P10 | Cards can move to Done without review |

## Upstream Compatibility

| Check | Status |
|-------|--------|
| KittyClaw.slnx | ✅ Preserved |
| Namespaces `KittyClaw.*` | ✅ Preserved |
| Database paths | ✅ `%APPDATA%/KittyClaw/` |
| API routes | ✅ `/api/projects/{slug}/...` |
| Class names | ✅ ClaudeRunner, AutomationEngine, etc. |

## Dual-Layer Strategy

```
Engine Layer (KittyClaw)          Product Layer (Beaver Board)
├── Namespaces                    ├── Browser title
├── Solution/projects             ├── Favicon/logo
├── Database tables               ├── CSS theme
├── API routes                    ├── Onboarding text
├── Class names                   ├── README
└── Core logic                    └── Docs
```

## Open Questions

1. PR #3/#4 status on GitHub — need to check if draft or merged
2. README.OpenCode.md claims vs actual implementation
3. FailureLogbook SQLite migration timeline
