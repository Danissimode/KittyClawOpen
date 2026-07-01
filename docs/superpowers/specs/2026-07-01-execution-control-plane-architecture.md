# Beaver Board Execution Architecture — Control Plane over OpenCode

**Status:** Approved Architecture  
**Date:** 2026-07-01  
**Author:** danissimode

---

## Core Principle

**Beaver Board = Control Plane**  
**OpenCode = Execution Plane**

Beaver Board does NOT become a multi-provider client. It stores and selects:
- Who executes → which OpenCode agent
- Which model string → provider/model-id format
- Which permissions/rules
- Which fallback/rotation policy

Physical execution goes through OpenCode, where model is specified as `provider/model-id`.

---

## Architecture Diagram

```
Beaver Board (Control Plane)
  ├─ Card / Parent / Subcard
  ├─ Assigned Slot
  ├─ Active Roster
  ├─ Fallback Policy
  ├─ Quota/Health State
  └─ Execution Resolver
        ↓
OpenCode (Execution Plane)
  ├─ provider auth via /connect
  ├─ provider config in opencode.json
  ├─ model format provider/model-id
  ├─ primary/subagent agents
  ├─ permissions
  └─ AGENTS.md/rules
        ↓
AgentRun
  ├─ immutable resolved snapshot
  ├─ logs
  ├─ evidence
  └─ handoff summary
```

---

## Key Entities

### 1. ExecutionSlot

The "executor" in UI.

```json
{
  "id": "programmer-1",
  "label": "Programmer 1",
  "role": "programmer",
  "opencodeAgent": "beaver-programmer",
  "activeModelProfileId": "kimi-code-main",
  "fallbackPolicyId": "coding-fallback",
  "status": "available"
}
```

Examples:
- orchestrator
- programmer-1, programmer-2, programmer-3
- reviewer
- qa
- documentalist

Card assignment:
```json
{
  "ticketId": 42,
  "assignedSlot": "programmer-1"
}
```

### 2. OpenCodeModelProfile

Reusable model profile.

```json
{
  "id": "kimi-code-main",
  "label": "Kimi 2.7 Code",
  "opencodeModel": "kimi/kimi-2.7-code",
  "purpose": "coding",
  "costTier": "medium",
  "speedTier": "fast",
  "qualityTier": "strong",
  "supportsTools": true,
  "enabled": true
}
```

Other profiles:
```json
[
  {
    "id": "mimo-auto-orchestrator",
    "label": "MiMo Auto Orchestrator",
    "opencodeModel": "mimo/mimo-auto",
    "purpose": "planning"
  },
  {
    "id": "ollama-glm-local",
    "label": "Local GLM",
    "opencodeModel": "ollama/glm-4.7",
    "purpose": "local-coding"
  },
  {
    "id": "flash-subagent",
    "label": "Flash Subagent",
    "opencodeModel": "deepseek/deepseek-flash",
    "purpose": "cheap-subtasks"
  }
]
```

### 3. RosterPreset

Team configuration for current day mode.

```json
{
  "id": "balanced-day",
  "label": "Balanced Day",
  "slots": {
    "orchestrator": {
      "opencodeAgent": "beaver-orchestrator",
      "modelProfileId": "mimo-auto-orchestrator"
    },
    "programmer-1": {
      "opencodeAgent": "beaver-programmer",
      "modelProfileId": "kimi-code-main"
    },
    "programmer-2": {
      "opencodeAgent": "beaver-programmer-local",
      "modelProfileId": "ollama-glm-local"
    },
    "reviewer": {
      "opencodeAgent": "beaver-reviewer",
      "modelProfileId": "flash-reviewer"
    }
  }
}
```

UI button:
```
Switch roster:
[ Premium / Smart ]
[ Balanced Day ]
[ Cheap / Quota Saving ]
[ Local Only ]
[ Emergency Fallback ]
```

### 4. FallbackPolicy

What happens when quota runs out.

```json
{
  "id": "coding-fallback",
  "on": [
    "quota-exhausted",
    "rate-limit",
    "provider-unavailable",
    "model-not-found",
    "network-error"
  ],
  "chain": [
    "kimi-code-main",
    "ollama-glm-local",
    "flash-subagent"
  ],
  "maxRetries": 2,
  "preserveSlot": true,
  "requireUserApprovalForDowngrade": false,
  "notify": true
}
```

For orchestrator (stricter):
```json
{
  "id": "orchestrator-fallback",
  "chain": [
    "mimo-auto-orchestrator",
    "kimi-code-main",
    "premium-planner"
  ],
  "requireUserApprovalForDowngrade": true
}
```

---

## Execution Levels

### Level 1 — Global Roster Switch

Example: Kimi quota exhausted.

```
Balanced Day → Local Only
```

All new programmer-1 runs start via ollama/glm-4.7.

**Important:** Old running sessions are NOT force-migrated. Context would be lost and file conflicts possible. Switching applies to new runs and restart/resume after explicit user action.

### Level 2 — Slot Executor Change

Example: Only programmer-1 exhausted.

```
programmer-1:
  was: kimi/kimi-2.7-code
  now: ollama/glm-4.7
```

Cards with `assignedSlot = programmer-1` do NOT need to change.

### Level 3 — Card Override

For specific important cards:

```json
{
  "cardId": 42,
  "assignedSlot": "programmer-1",
  "overrideModel": "mimo/mimo-auto",
  "lockExecutor": true
}
```

`lockExecutor: true` prevents auto-fallback from moving critical/security/release tasks to weaker models.

---

## OpenCode Subagent Inheritance (Critical)

OpenCode docs: if subagent has no model, it inherits the model of the primary agent that called it.

**For Beaver Board this rule is ironclad:**

> If parent is smart model, subagents do NOT inherit model automatically.
> Each subagent slot must have explicit model.

**Wrong:**
```json
{
  "agent": {
    "beaver-orchestrator": {
      "model": "mimo/mimo-auto"
    },
    "beaver-programmer-flash": {
      "mode": "subagent"
    }
  }
}
```

Problem: `beaver-programmer-flash` may inherit `mimo/mimo-auto` from orchestrator.

**Correct:**
```json
{
  "agent": {
    "beaver-orchestrator": {
      "mode": "primary",
      "model": "mimo/mimo-auto"
    },
    "beaver-programmer-flash": {
      "mode": "subagent",
      "model": "deepseek/deepseek-flash"
    },
    "beaver-programmer-local": {
      "mode": "subagent",
      "model": "ollama/glm-4.7"
    }
  }
}
```

---

## OpenCode Agent Configuration

```json
{
  "agent": {
    "beaver-orchestrator": {
      "mode": "primary",
      "model": "mimo/mimo-auto",
      "prompt": "{file:./.beaver/agents/orchestrator.md}",
      "permission": {
        "edit": "ask",
        "bash": "ask",
        "task": {
          "*": "deny",
          "beaver-programmer-*": "allow",
          "beaver-reviewer": "ask"
        }
      }
    },
    "beaver-programmer-kimi": {
      "mode": "subagent",
      "model": "kimi/kimi-2.7-code",
      "prompt": "{file:./.beaver/agents/programmer.md}",
      "permission": {
        "edit": "allow",
        "bash": {
          "*": "ask",
          "npm test*": "allow",
          "dotnet test*": "allow",
          "git diff*": "allow",
          "git status*": "allow",
          "git push*": "deny"
        }
      }
    },
    "beaver-programmer-local": {
      "mode": "subagent",
      "model": "ollama/glm-4.7",
      "prompt": "{file:./.beaver/agents/programmer.md}",
      "permission": {
        "edit": "allow",
        "bash": "ask"
      }
    },
    "beaver-reviewer": {
      "mode": "subagent",
      "model": "deepseek/deepseek-flash",
      "prompt": "{file:./.beaver/agents/reviewer.md}",
      "permission": {
        "edit": "deny",
        "bash": {
          "*": "ask",
          "git diff*": "allow",
          "grep *": "allow"
        }
      }
    }
  }
}
```

---

## Data Separation

### What OpenCode Stores

- Provider auth
- Provider config
- Model IDs
- Agent definitions
- Permissions
- AGENTS.md / rules
- Network proxy/cert config

### What Beaver Board Stores

- Active roster
- Slot → OpenCode agent mapping
- Slot → Model profile mapping
- Card → Assigned slot
- Card → Override/lock
- Parent → Subcard defaults
- Fallback policy
- Quota/health state cache
- Run resolved snapshot

**Beaver Board does NOT store secrets and does NOT try to replace OpenCode provider layer.**

---

## Card Execution Flow

```
User clicks Run Card
  ↓
Beaver Board loads card assignment
  ↓
ExecutionResolver resolves slot
  ↓
Checks active roster
  ↓
Checks model health/quota state
  ↓
Builds ResolvedOpenCodeExecutionPlan
  ↓
Starts OpenCode with:
    agent = beaver-programmer-kimi
    model = kimi/kimi-2.7-code
    rules/instructions = project + card context
  ↓
Stores immutable snapshot in AgentRun.ExecutionMetadata
```

### Execution Snapshot

```json
{
  "ticketId": 42,
  "assignedSlot": "programmer-1",
  "resolvedAgent": "beaver-programmer-kimi",
  "resolvedModel": "kimi/kimi-2.7-code",
  "rosterPreset": "balanced-day",
  "fallbackPolicy": "coding-fallback",
  "resolvedAt": "2026-07-01T16:20:00Z",
  "reason": "slot-default",
  "lockExecutor": false
}
```

**Important:** If in 1 hour programmer-1 becomes Ollama, old run still shows it ran via Kimi.

---

## Executor Switching Modes

| Mode | Behavior |
|------|----------|
| `new-runs-only` | Switch affects only new runs |
| `restart-on-new-executor` | Current card can be restarted on new executor |
| `resume-same-session-only` | Resume only with same model/agent |
| `force-migrate` | Dangerous mode, manual only, with summary handoff |

**Default:** `new-runs-only`

Reason: Transferring active session between Kimi and Ollama may lose hidden context. Safe option — finish old run, save summary/evidence, then start new run with handoff prompt.

---

## Handoff Prompt on Executor Switch

When quota exhausted and card needs to switch from Kimi to Ollama:

```
You are taking over Ticket #42 from previous executor.

Previous executor:
- slot: programmer-1
- agent: beaver-programmer-kimi
- model: kimi/kimi-2.7-code
- status: stopped due to quota exhaustion

Current state:
- changed files:
- completed steps:
- failing tests:
- open questions:
- next safe action:

Continue from the current repository state.
Do not repeat completed work.
First inspect git diff and relevant files, then proceed.
```

This dramatically reduces losses on executor switching.

---

## UI Components

### Top Block: AI Team Roster

```
AI Team Roster
Orchestrator       MiMo Auto        healthy
Programmer 1       Kimi 2.7 Code    quota 74%
Programmer 2       Ollama GLM       local ready
Reviewer           DeepSeek Flash   healthy
[Switch Preset] [Reassign Slot] [Pause Provider] [Local Only]
```

### Card Executor Info

```
Executor
Slot: Programmer 1
Current executor: Kimi 2.7 Code
Fallback: Ollama GLM → Flash Subagent
Lock: off
[Run] [Run with other executor] [Lock to this executor]
```

### Parent Card Execution

```
Parent execution:
Orchestrator: MiMo Auto
Subcard defaults:
  Context inheritance: on
  Model inheritance: off
  Default subcard executor: Programmer 1
  Fallback: coding-fallback
```

---

## Implementation Plan

### Minimum Viable Package

1. Add `OpenCodeModelProfile` entity
2. Add `ExecutionSlot` entity
3. Add `RosterPreset` entity
4. Add `TicketExecutionOverride` entity
5. Add `ExecutionResolver` service
6. In run metadata write:
   - `assignedSlot`
   - `resolvedOpenCodeAgent`
   - `resolvedOpenCodeModel`
   - `rosterPresetId`
   - `fallbackPolicyId`
   - `handoffFromRunId`
7. Add executor switcher UI
8. Add explicit model to OpenCode config generator for each subagent

### What Already Exists in Codebase

- `AgentRun.Model`
- `AgentRun.RuntimeId`
- `AgentRun.RoleId`
- `AgentRun.ModelProfileId`
- `AgentRun.CommandDisplay`
- `AgentRun.ExecutionMetadata`

---

## Key Principle (For the Record)

> **Card is assigned to a role/slot, not a specific model.**
> 
> The specific model is the current implementation of the slot in the active roster.
> Therefore during the day you can quickly change executor due to quotas, availability, price or quality without rewriting cards or breaking run history.
> 
> For parent/subcard: context is inherited, model is NOT — unless explicitly enabled.
