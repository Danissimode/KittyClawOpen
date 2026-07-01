namespace KittyClaw.Core.Automation.TeamRoles;

/// <summary>
/// A team role defines a function in the AI team.
/// Examples: orchestrator, planner, architect, programmer, validator.
/// </summary>
public sealed class TeamRole
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Short identifier (e.g., "programmer", "validator")</summary>
    public required string Slug { get; init; }
    
    /// <summary>Display name (e.g., "Programmer", "Validator")</summary>
    public required string Name { get; init; }
    
    /// <summary>What this role does</summary>
    public string? Description { get; init; }
    
    /// <summary>Default execution profile for this role</summary>
    public string? DefaultExecutionProfileId { get; init; }
    
    /// <summary>JSON array of capabilities (e.g., ["board.read", "code.edit"])</summary>
    public string? CapabilitiesJson { get; init; }
    
    /// <summary>Max risk level this role can handle: low, medium, high, critical</summary>
    public string RiskLimit { get; init; } = "medium";
    
    /// <summary>Whether this role is enabled</summary>
    public bool Enabled { get; init; } = true;
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A concrete agent assigned to a role.
/// </summary>
public sealed class AgentProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Display name (e.g., "programmer-1", "validator-1")</summary>
    public required string DisplayName { get; init; }
    
    /// <summary>Which role this agent fulfills</summary>
    public required string RoleId { get; init; }
    
    /// <summary>Which execution profile to use</summary>
    public string? ExecutionProfileId { get; init; }
    
    /// <summary>Agent status: idle, running, blocked, failed, paused</summary>
    public string Status { get; set; } = "idle";
    
    /// <summary>Max concurrent runs for this agent</summary>
    public int MaxConcurrentRuns { get; init; } = 1;
    
    /// <summary>Current run count (denormalized)</summary>
    public int CurrentRunCount { get; set; }
    
    /// <summary>Whether this agent is enabled</summary>
    public bool Enabled { get; init; } = true;
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Execution profile defines runtime/provider/model/permissions.
/// </summary>
public sealed class ExecutionProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Display name</summary>
    public required string Name { get; init; }
    
    /// <summary>Runtime: opencode, claude, internal</summary>
    public string Runtime { get; init; } = "opencode";
    
    /// <summary>Provider: kimi, ollama, deepseek, anthropic, etc.</summary>
    public string? Provider { get; init; }
    
    /// <summary>Model identifier</summary>
    public string? Model { get; init; }
    
    /// <summary>Full OpenCode model string (provider/model-id)</summary>
    public string? OpencodeModel { get; init; }
    
    /// <summary>JSON array of permissions (e.g., ["read", "edit", "bash"])</summary>
    public string? PermissionsJson { get; init; }
    
    /// <summary>Whether worktree is required for code changes</summary>
    public bool WorktreeRequired { get; init; }
    
    /// <summary>Max turns per run</summary>
    public int MaxTurns { get; init; } = 20;
    
    /// <summary>Timeout in minutes</summary>
    public int TimeoutMinutes { get; init; } = 45;
    
    /// <summary>Whether this profile is enabled</summary>
    public bool Enabled { get; init; } = true;
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Role assignment for a specific ticket.
/// </summary>
public sealed class TicketRoleAssignment
{
    public int TicketId { get; set; }
    public string ProjectSlug { get; set; } = "";
    
    /// <summary>Which role is assigned to execute this ticket</summary>
    public string? AssignedRoleId { get; set; }
    
    /// <summary>Which specific agent is assigned</summary>
    public string? AssignedAgentId { get; set; }
    
    /// <summary>Which role should review/validate</summary>
    public string? ReviewerRoleId { get; set; }
    
    /// <summary>Which specific agent should review</summary>
    public string? ReviewerAgentId { get; set; }
    
    /// <summary>Whether architect review is required</summary>
    public bool ArchitectRequired { get; set; }
    
    /// <summary>Whether validator review is required</summary>
    public bool ValidatorRequired { get; set; } = true;
}

/// <summary>
/// Policy for what a role can or cannot do.
/// </summary>
public sealed class RolePolicy
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required string RoleId { get; init; }
    
    /// <summary>Action pattern (e.g., "ticket.move.done", "ticket.run", "code.edit")</summary>
    public required string Action { get; init; }
    
    /// <summary>Effect: allow, deny</summary>
    public required string Effect { get; init; }
    
    /// <summary>Optional condition JSON</summary>
    public string? ConditionJson { get; init; }
    
    /// <summary>Reason for the policy</summary>
    public string? Reason { get; init; }
    
    public bool Enabled { get; init; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Built-in role slugs.
/// </summary>
public static class RoleSlugs
{
    public const string Owner = "owner";
    public const string Orchestrator = "orchestrator";
    public const string Planner = "planner";
    public const string Architect = "architect";
    public const string Programmer = "programmer";
    public const string Validator = "validator";
    public const string Documenter = "documenter";
    public const string HealthMonitor = "health-monitor";
}

/// <summary>
/// Common capabilities.
/// </summary>
public static class Capabilities
{
    public const string BoardRead = "board.read";
    public const string BoardWrite = "board.write";
    public const string TicketRun = "ticket.run";
    public const string TicketRunAssigned = "ticket.run.assigned";
    public const string CodeEdit = "code.edit";
    public const string BashRun = "bash.run";
    public const string EvidenceAttach = "evidence.attach";
    public const string TicketMoveReview = "ticket.move.review";
    public const string TicketMoveDone = "ticket.move.done";
    public const string DecomposePreview = "decompose.preview";
    public const string DecomposeApply = "decompose.apply";
    public const string CommandPlanCreate = "command.plan.create";
    public const string ArchitectReview = "architect.review";
    public const string HealthRead = "health.read";
    public const string SchedulerRead = "scheduler.read";
}
