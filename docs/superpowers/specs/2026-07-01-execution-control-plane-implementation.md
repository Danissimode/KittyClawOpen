# Execution Control Plane — Implementation Plan

**Status:** In Progress  
**Date:** 2026-07-01  
**Based on:** 2026-07-01-execution-control-plane-architecture.md

---

## Existing Foundation

The codebase already has:

| Entity | Location | Key Properties |
|--------|----------|----------------|
| `ModelProfileConfig` | `KittyClaw.Core/Automation/Runtimes/ModelProfileConfig.cs` | Id, Model, Provider, DisplayName, HighRiskAllowed |
| `AgentRuntimeProjectConfig` | `KittyClaw.Core/Automation/Runtimes/AgentRuntimeProjectConfig.cs` | ModelProfileByRole, ModelProfileByRuntime |
| `Ticket` | `KittyClaw.Core/Models/Ticket.cs` | ModelProfileId, ModelOverride, ProviderOverride, OpenCodeAgent |
| `ExecutionMetadata` | `KittyClaw.Core/Automation/Runners/IAgentRunner.cs` | Model, Provider, OpenCodeAgent |
| `AgentRun` | `KittyClaw.Core/Automation/AgentRun.cs` | Model, ModelProfileId, ExecutionMetadata |

**What's missing:**
- ExecutionSlot (card → slot assignment)
- RosterPreset (team configuration)
- FallbackPolicy (quota handling)
- Slot-based resolution in ExecutionResolver
- UI for roster management

---

## Phase 1: Core Entities

### 1.1 Add ExecutionSlot

**File:** `KittyClaw.Core/Automation/ExecutionSlot.cs`

```csharp
public class ExecutionSlot
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Role { get; set; } = ""; // programmer, reviewer, qa, orchestrator
    public string OpencodeAgent { get; set; } = "";
    public string ActiveModelProfileId { get; set; } = "";
    public string FallbackPolicyId { get; set; } = "";
    public string Status { get; set; } = "available"; // available, busy, paused
    public DateTime? LastUsedAt { get; set; }
    public int? LastRunTicketId { get; set; }
}
```

### 1.2 Add RosterPreset

**File:** `KittyClaw.Core/Automation/RosterPreset.cs`

```csharp
public class RosterPreset
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, RosterSlotConfig> Slots { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime? ActivatedAt { get; set; }
}

public class RosterSlotConfig
{
    public string OpencodeAgent { get; set; } = "";
    public string ModelProfileId { get; set; } = "";
}
```

### 1.3 Add FallbackPolicy

**File:** `KittyClaw.Core/Automation/FallbackPolicy.cs`

```csharp
public class FallbackPolicy
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> TriggerReasons { get; set; } = new(); // quota-exhausted, rate-limit, etc.
    public List<string> ModelProfileChain { get; set; } = new(); // ordered fallback models
    public int MaxRetries { get; set; } = 2;
    public bool PreserveSlot { get; set; } = true;
    public bool RequireApprovalForDowngrade { get; set; }
    public bool Notify { get; set; } = true;
}
```

### 1.4 Add TicketSlotAssignment

**File:** `KittyClaw.Core/Automation/TicketSlotAssignment.cs`

```csharp
public class TicketSlotAssignment
{
    public int TicketId { get; set; }
    public string AssignedSlotId { get; set; } = "";
    public string? OverrideModelProfileId { get; set; }
    public bool LockExecutor { get; set; }
    public DateTime AssignedAt { get; set; }
    public string AssignedBy { get; set; } = ""; // "owner" or agent name
}
```

---

## Phase 2: Storage

### 2.1 Add ExecutionSlotStore

**File:** `KittyClaw.Core/Automation/ExecutionSlotStore.cs`

Persist to `{dataDir}/slots.json` or `{dataDir}/roster/`.

### 2.2 Add RosterPresetStore

**File:** `KittyClaw.Core/Automation/RosterPresetStore.cs`

Persist to `{dataDir}/roster-presets.json`.

### 2.3 Add FallbackPolicyStore

**File:** `KittyClaw.Core/Automation/FallbackPolicyStore.cs`

Persist to `{dataDir}/fallback-policies.json`.

### 2.4 Extend Ticket Table

Add column `AssignedSlotId` to Tickets table via inline migration.

---

## Phase 3: Resolution Service

### 3.1 Create ExecutionResolver

**File:** `KittyClaw.Core/Automation/ExecutionResolver.cs`

```csharp
public class ExecutionResolver
{
    // Resolves what model/agent to use for a ticket
    public ResolvedExecution Resolve(
        Ticket ticket,
        RosterPreset activeRoster,
        Dictionary<string, ExecutionSlot> slots,
        Dictionary<string, ModelProfileConfig> profiles)
    {
        // 1. Check ticket override (lockExecutor)
        // 2. Look up assigned slot
        // 3. Look up slot's active model profile
        // 4. Check quota/health state
        // 5. Apply fallback if needed
        // 6. Return resolved execution plan
    }
}

public class ResolvedExecution
{
    public string AssignedSlotId { get; set; }
    public string ResolvedAgent { get; set; }
    public string ResolvedModel { get; set; }
    public string ModelProfileId { get; set; }
    public string RosterPresetId { get; set; }
    public string FallbackPolicyId { get; set; }
    public string Reason { get; set; } // "slot-default", "override", "fallback"
    public bool LockExecutor { get; set; }
    public DateTime ResolvedAt { get; set; }
}
```

### 3.2 Create HandoffPromptBuilder

**File:** `KittyClaw.Core/Automation/HandoffPromptBuilder.cs`

Generates handoff context when switching executors.

---

## Phase 4: Integration

### 4.1 Update ActionExecutor

In `ActionExecutor.Runners.cs`, use `ExecutionResolver` before dispatching to runner.

### 4.2 Update AgentRun Metadata

Write resolved execution info to `ExecutionMetadata`:

```csharp
executionMetadata.AssignedSlot = resolved.AssignedSlotId;
executionMetadata.ResolvedOpenCodeAgent = resolved.ResolvedAgent;
executionMetadata.ResolvedOpenCodeModel = resolved.ResolvedModel;
executionMetadata.RosterPresetId = resolved.RosterPresetId;
executionMetadata.FallbackPolicyId = resolved.FallbackPolicyId;
```

### 4.3 Update TicketAutoRunService

When auto-running tickets, use slot-based resolution.

---

## Phase 5: API Endpoints

### 5.1 Roster Endpoints

```
GET    /api/roster/slots              - List all slots
PUT    /api/roster/slots/{id}         - Update slot
GET    /api/roster/presets            - List presets
POST   /api/roster/presets            - Create preset
PUT    /api/roster/presets/{id}       - Update preset
POST   /api/roster/presets/{id}/activate - Activate preset
GET    /api/roster/fallbacks          - List fallback policies
POST   /api/roster/tickets/{id}/assign - Assign slot to ticket
POST   /api/roster/tickets/{id}/override - Set ticket override
```

### 5.2 Resolution Endpoint

```
POST   /api/roster/resolve/{ticketId} - Preview resolution for a ticket
```

---

## Phase 6: UI Components

### 6.1 RosterPanel (top of board)

Shows active roster with slot → model mapping and quick switcher.

### 6.2 SlotSelector (on ticket card)

Dropdown to assign/override slot.

### 6.3 ExecutorInfo (in ticket detail)

Shows resolved executor, fallback chain, lock status.

### 6.4 RosterPresetSwitcher

Button group for switching active preset.

---

## Phase 7: OpenCode Config Generator

Update OpenCode config generation to ensure each subagent has explicit model.

---

## Implementation Order

1. ✅ Create spec document
2. Core entities (ExecutionSlot, RosterPreset, FallbackPolicy)
3. Storage stores
4. ExecutionResolver service
5. Extend Ticket table
6. Update ActionExecutor to use resolver
7. API endpoints
8. UI components
9. OpenCode config generator updates
10. HandoffPromptBuilder

---

## Migration Path

Since runs are JSON-on-disk (not SQLite), we can add new fields without migration.

For Tickets table, add `AssignedSlotId` column via inline migration.

Default behavior: if no slot assigned, use existing ModelProfileId resolution.
