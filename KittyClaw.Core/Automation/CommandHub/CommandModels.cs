namespace KittyClaw.Core.Automation.CommandHub;

/// <summary>
/// A conversation thread in the Command Dialogue Hub.
/// </summary>
public sealed class CommandConversation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Source channel: internal, telegram, slack</summary>
    public required string Source { get; init; }
    
    /// <summary>External thread/chat ID (for Telegram/Slack)</summary>
    public string? ExternalThreadId { get; init; }
    
    /// <summary>External user ID</summary>
    public string? ExternalUserId { get; init; }
    
    /// <summary>Conversation status: open, closed, archived</summary>
    public string Status { get; set; } = "open";
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastMessageAt { get; set; }
}

/// <summary>
/// A message in the command dialogue.
/// </summary>
public sealed class CommandMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required string ConversationId { get; init; }
    
    /// <summary>Message source: internal, telegram, slack</summary>
    public required string Source { get; init; }
    
    /// <summary>External message ID</summary>
    public string? ExternalMessageId { get; init; }
    
    /// <summary>User who sent the message</summary>
    public required string UserId { get; init; }
    
    /// <summary>Message text</summary>
    public required string Text { get; init; }
    
    /// <summary>Target agent (if @mention): orchestrator, planner, health, scheduler</summary>
    public string? TargetAgent { get; init; }
    
    /// <summary>Parsed intent (if parsed)</summary>
    public string? IntentType { get; init; }
    
    /// <summary>Message role: user, assistant, system</summary>
    public string Role { get; init; } = "user";
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Parsed intent from a command message.
/// </summary>
public sealed class CommandIntent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required string MessageId { get; init; }
    
    /// <summary>Intent type: status, health, report, backlog_next, tree, start_ticket, 
    /// start_backlog, run_ready_children, schedule, decompose, move_ticket, etc.</summary>
    public required string Type { get; init; }
    
    /// <summary>Risk level: low, medium, high, critical</summary>
    public string Risk { get; set; } = "low";
    
    /// <summary>Whether this intent requires approval</summary>
    public bool RequiresApproval { get; set; }
    
    /// <summary>Parsed parameters (JSON)</summary>
    public string? ParametersJson { get; init; }
    
    /// <summary>Confidence score (0-1) for NLP parsing</summary>
    public double Confidence { get; set; }
    
    /// <summary>Raw intent text if deterministic command</summary>
    public string? RawCommand { get; init; }
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A command plan that groups actions for approval.
/// </summary>
public sealed class CommandPlan
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required string ConversationId { get; init; }
    public required string MessageId { get; init; }
    
    /// <summary>Human-readable summary of what will be done</summary>
    public required string Summary { get; init; }
    
    /// <summary>Detailed description of the plan</summary>
    public string? Description { get; init; }
    
    /// <summary>Risk level: low, medium, high, critical</summary>
    public string Risk { get; set; } = "low";
    
    /// <summary>Plan status: pending_approval, approved, rejected, expired, executed, failed</summary>
    public string Status { get; set; } = "pending_approval";
    
    /// <summary>JSON array of actions to execute</summary>
    public required string ActionsJson { get; init; }
    
    /// <summary>Who created the plan</summary>
    public required string CreatedBy { get; init; }
    
    /// <summary>Who approved the plan</summary>
    public string? ApprovedBy { get; init; }
    
    /// <summary>Rejection reason</summary>
    public string? RejectionReason { get; init; }
    
    /// <summary>When the plan expires (for approval timeout)</summary>
    public DateTime? ExpiresAt { get; init; }
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
}

/// <summary>
/// An individual action within a command plan.
/// </summary>
public sealed class CommandAction
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    
    /// <summary>Action type: move_ticket, start_run, schedule_run, assign_agent, 
    /// create_ticket, create_blocker, run_ready_children, etc.</summary>
    public required string Type { get; init; }
    
    /// <summary>Target ticket ID (if applicable)</summary>
    public int? TicketId { get; init; }
    
    /// <summary>Target agent ID (if applicable)</summary>
    public string? AgentId { get; init; }
    
    /// <summary>Action parameters (JSON)</summary>
    public string? ParametersJson { get; init; }
    
    /// <summary>Action status: pending, executed, failed, skipped</summary>
    public string Status { get; set; } = "pending";
    
    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>Result data (JSON)</summary>
    public string? ResultJson { get; init; }
}

/// <summary>
/// Command risk levels.
/// </summary>
public static class CommandRiskLevels
{
    public const string Low = "low";         // read-only
    public const string Medium = "medium";   // board changes
    public const string High = "high";       // starts/stops agents
    public const string Critical = "critical"; // deletes, overrides, mass actions
}

/// <summary>
/// Command intent types.
/// </summary>
public static class CommandIntentTypes
{
    // Read-only
    public const string Status = "status";
    public const string Health = "health";
    public const string Report = "report";
    public const string BacklogNext = "backlog_next";
    public const string Tree = "tree";
    public const string TicketDetail = "ticket_detail";
    public const string AgentStatus = "agent_status";
    
    // Planning
    public const string Decompose = "decompose";
    public const string ProposeTasks = "propose_tasks";
    public const string Schedule = "schedule";
    
    // Board mutation
    public const string MoveTicket = "move_ticket";
    public const string AssignAgent = "assign_agent";
    public const string CreateTicket = "create_ticket";
    
    // Execution
    public const string StartTicket = "start_ticket";
    public const string StartBacklog = "start_backlog";
    public const string RunReadyChildren = "run_ready_children";
    public const string StopRun = "stop_run";
    
    // Recovery
    public const string RestartRun = "restart_run";
    public const string CreateBlocker = "create_blocker";
    public const string ResolveEvent = "resolve_event";
}

/// <summary>
/// Command permissions/roles.
/// </summary>
public static class CommandRoles
{
    public const string Viewer = "viewer";         // read-only
    public const string Contributor = "contributor"; // propose actions
    public const string Operator = "operator";     // move tasks, schedule
    public const string Owner = "owner";           // start/stop, approve
    public const string Admin = "admin";           // connectors, settings
}
