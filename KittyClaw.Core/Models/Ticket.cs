namespace KittyClaw.Core.Models;

public class Ticket
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Backlog";
    public TicketPriority Priority { get; set; } = TicketPriority.NiceToHave;
    public int SortOrder { get; set; }
    public string? AssignedTo { get; set; }
    public string? CliRuntimeId { get; set; }
    public string? CaoRoleId { get; set; }
    public string? ModelProfileId { get; set; }
    public string? RiskLevel { get; set; }
    public string? Reviewer { get; set; }
    public string? RequiredEvidence { get; set; }
    public string? EvidenceCompleted { get; set; }
    public string CreatedBy { get; set; } = "owner";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? ParentId { get; set; }

    // ── Hierarchical Kanban ─────────────────────────────────────────────
    /// <summary>Root ticket ID for this subtree (top-level parent)</summary>
    public int? RootId { get; set; }
    
    /// <summary>Depth in tree: 0=root, 1=task, 2=subtask</summary>
    public int Depth { get; set; }
    
    /// <summary>Materialized path for fast subtree queries (e.g., "1/5/23")</summary>
    public string? Path { get; set; }
    
    /// <summary>Order among siblings</summary>
    public int TreeOrder { get; set; }
    
    /// <summary>Ticket kind: epic, task, subtask, checklist_item, decision, review, qa, release</summary>
    public string Kind { get; set; } = "task";
    
    /// <summary>Execution role: orchestrator, planner, worker, reviewer, joiner</summary>
    public string? ExecutionRole { get; set; }
    
    /// <summary>Execution mode: manual, sequential, parallel, join</summary>
    public string ExecutionMode { get; set; } = "manual";
    
    /// <summary>Parallel group ID (if part of a parallel execution group)</summary>
    public string? ParallelGroupId { get; set; }
    
    /// <summary>Dependency policy: all_dependencies_done, any_dependency_done</summary>
    public string DependencyPolicy { get; set; } = "all_dependencies_done";
    
    /// <summary>Aggregate policy: children_define_progress, manual, all_must_pass</summary>
    public string AggregatePolicy { get; set; } = "children_define_progress";
    
    /// <summary>Child count for quick display (denormalized)</summary>
    public int ChildCount { get; set; }
    
    /// <summary>Completed child count for progress</summary>
    public int ChildrenDoneCount { get; set; }
    
    /// <summary>Failed child count</summary>
    public int ChildrenFailedCount { get; set; }
    
    /// <summary>Blocked child count</summary>
    public int ChildrenBlockedCount { get; set; }
    
    /// <summary>Has active run in subtree</summary>
    public bool SubtreeHasActiveRun { get; set; }
    
    // ── Recursive Decomposition ─────────────────────────────────────────
    /// <summary>Whether this card has children (denormalized)</summary>
    public bool HasChildren { get; set; }
    
    /// <summary>Container mode: executable, container, orchestrator</summary>
    public string ContainerMode { get; set; } = "executable";
    
    /// <summary>Completion policy: manual, all_required_children_done, all_required_leaf_tasks_done, all_children_reviewed</summary>
    public string CompletionPolicy { get; set; } = "manual";
    
    /// <summary>How this card was decomposed: manual, planner_agent, checklist_conversion, template, import</summary>
    public string? SplitOrigin { get; set; }
    
    /// <summary>Who decomposed this card</summary>
    public string? DecomposedBy { get; set; }
    
    /// <summary>When this card was decomposed</summary>
    public DateTime? DecomposedAt { get; set; }
    
    /// <summary>Total leaf tasks in subtree (denormalized)</summary>
    public int LeafTaskCount { get; set; }
    
    /// <summary>Completed leaf tasks (denormalized)</summary>
    public int LeafTasksDoneCount { get; set; }
    
    /// <summary>Whether this card is marked as optional (skippable)</summary>
    public bool IsOptional { get; set; }
    
    /// <summary>Whether this card is cancelled/skipped</summary>
    public bool IsCancelled { get; set; }
    
    /// <summary>Reason for cancellation/skip</summary>
    public string? CancelReason { get; set; }

    // ── Plan workflow ───────────────────────────────────────────────────
    /// <summary>Current plan status: none, drafting, awaiting-approval, approved, rejected.</summary>
    public string PlanStatus { get; set; } = "none";
    /// <summary>Plan body — what the agent will do, step by step.</summary>
    public string? PlanBody { get; set; }
    public string? PlanApprovedBy { get; set; }
    public DateTime? PlanApprovedAt { get; set; }
    public bool RequiresPlan { get; set; } = false;

    // ── Execution overrides (per-ticket) ────────────────────────────────
    /// <summary>Execution mode override: LegacyClaude, DirectOpenCode, CaoGoverned, TeamWorkflow, Manual, or null=inherit.</summary>
    public string? ExecutionModeOverride { get; set; }
    /// <summary>OpenCode agent override: build, plan, or custom agent name.</summary>
    public string? OpenCodeAgent { get; set; }
    /// <summary>Provider override (openrouter, anthropic, openai, ollama, litellm, etc.).</summary>
    public string? ProviderOverride { get; set; }
    /// <summary>Model override (provider-specific model id).</summary>
    public string? ModelOverride { get; set; }
    /// <summary>Profile override (developer, planner, reviewer, etc.).</summary>
    public string? ProfileOverride { get; set; }
    /// <summary>Whether this ticket must run inside its own worktree.</summary>
    public bool UseWorktree { get; set; } = true;
    /// <summary>Optional forbidden paths for this ticket (comma-separated glob patterns).</summary>
    public string? ForbiddenPaths { get; set; }

    // ── Execution slot assignment (control plane) ───────────────────────
    /// <summary>Which execution slot this ticket is assigned to (e.g., "programmer-1", "reviewer").</summary>
    public string? AssignedSlotId { get; set; }
    /// <summary>Optional override model profile for this ticket (takes precedence over slot's default).</summary>
    public string? OverrideModelProfileId { get; set; }
    /// <summary>If true, prevent auto-fallback from moving this ticket to a different model.</summary>
    public bool LockExecutor { get; set; }

    public List<Comment> Comments { get; set; } = [];
    public List<ActivityEntry> Activities { get; set; } = [];
    public List<Label> Labels { get; set; } = [];
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<SubTicketInfo> SubTickets { get; set; } = [];
}

public static class PlanStatuses
{
    public const string None = "none";
    public const string Drafting = "drafting";
    public const string AwaitingApproval = "awaiting-approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
