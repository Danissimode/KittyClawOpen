namespace KittyClaw.Core.Automation.TeamRoles;

/// <summary>
/// Conversation modes for the Command Dialogue Hub.
/// </summary>
public enum ConversationMode
{
    /// <summary>Main user-facing dialogue, orchestrator-led</summary>
    OrchestratorDialogue,
    
    /// <summary>Thread within a role inbox</summary>
    RoleInboxThread,
    
    /// <summary>Thread within an agent session</summary>
    AgentSessionThread,
    
    /// <summary>System activity log (lifecycle events)</summary>
    SystemActivityLog,
    
    /// <summary>Internal trace for debugging</summary>
    InternalTrace
}

/// <summary>
/// Message visibility levels.
/// </summary>
public enum MessageVisibility
{
    /// <summary>Visible to user in main dialogue</summary>
    UserVisible,
    
    /// <summary>Summarized by orchestrator for user</summary>
    OrchestratorSummary,
    
    /// <summary>Visible in team activity feed</summary>
    TeamActivity,
    
    /// <summary>Internal only, not user-facing</summary>
    Internal,
    
    /// <summary>Debug/trace only</summary>
    Debug,
    
    /// <summary>Health Center only</summary>
    HealthOnly,
    
    /// <summary>Audit trail only</summary>
    AuditOnly
}

/// <summary>
/// Reply policy for role communication.
/// </summary>
public enum ReplyPolicy
{
    /// <summary>Only orchestrator replies to user-facing dialogue</summary>
    OrchestratorOnly,
    
    /// <summary>Roles reply through orchestrator mediation</summary>
    MediatedRoles,
    
    /// <summary>Roles can reply directly when mentioned</summary>
    DirectRolesAllowed,
    
    /// <summary>All agents can reply (debug mode)</summary>
    DebugAllAgents
}

/// <summary>
/// A message routed through the communication system.
/// </summary>
public sealed class RoutedMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Source of the message</summary>
    public required string Source { get; init; }
    
    /// <summary>Target role/agent</summary>
    public required string TargetRole { get; init; }
    
    /// <summary>Specific agent (if direct)</summary>
    public string? TargetAgentId { get; init; }
    
    /// <summary>Message text</summary>
    public required string Text { get; init; }
    
    /// <summary>Visibility level</summary>
    public MessageVisibility Visibility { get; set; } = MessageVisibility.UserVisible;
    
    /// <summary>Conversation mode</summary>
    public ConversationMode Mode { get; set; } = ConversationMode.OrchestratorDialogue;
    
    /// <summary>Whether this was a direct mention</summary>
    public bool WasDirectMention { get; init; }
    
    /// <summary>Whether orchestrator delegated this</summary>
    public bool WasOrchestratorDelegated { get; init; }
    
    /// <summary>Associated ticket ID</summary>
    public int? TicketId { get; init; }
    
    /// <summary>Associated session ID</summary>
    public string? SessionId { get; init; }
    
    /// <summary>Reply to message ID</summary>
    public string? ReplyToMessageId { get; init; }
    
    /// <summary>Message status: pending, delivered, suppressed, summarized</summary>
    public string Status { get; set; } = "pending";
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Project-level conversation policy settings.
/// </summary>
public sealed class ConversationPolicy
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Default reply policy</summary>
    public ReplyPolicy ReplyPolicy { get; set; } = ReplyPolicy.MediatedRoles;
    
    /// <summary>Allow direct agent mentions (advanced mode)</summary>
    public bool AllowDirectAgentMentions { get; set; }
    
    /// <summary>Orchestrator auto-summarizes role responses</summary>
    public bool AutoSummarizeRoleResponses { get; set; } = true;
    
    /// <summary>Always show critical events in main dialogue</summary>
    public bool ShowCriticalEventsInMain { get; set; } = true;
    
    /// <summary>Roles that can always reply to main dialogue (besides orchestrator)</summary>
    public string? AlwaysVisibleRolesJson { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Message visibility levels for display.
/// </summary>
public static class MessageVisibilityValues
{
    public const string UserVisible = "user_visible";
    public const string OrchestratorSummary = "orchestrator_summary";
    public const string TeamActivity = "team_activity";
    public const string Internal = "internal";
    public const string Debug = "debug";
    public const string HealthOnly = "health_only";
    public const string AuditOnly = "audit_only";
}

/// <summary>
/// Reply policy values.
/// </summary>
public static class ReplyPolicyValues
{
    public const string OrchestratorOnly = "orchestrator_only";
    public const string MediatedRoles = "mediated_roles";
    public const string DirectRolesAllowed = "direct_roles_allowed";
    public const string DebugAllAgents = "debug_all_agents";
}
