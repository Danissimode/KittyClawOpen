namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// A persistent scheduled action in the workflow.
/// Survives app restarts and can be scoped to ticket, agent, run, or project.
/// </summary>
public sealed class WorkflowReminder
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    // ── Scope ──────────────────────────────────────────────────────────
    public int? TicketId { get; init; }
    public string? RunId { get; init; }
    public string? AgentId { get; init; }
    public string? SessionId { get; init; }
    
    // ── Schedule ───────────────────────────────────────────────────────
    /// <summary>Reminder type: oneTime, recurring, runWatchdog, quotaWatchdog, reviewReminder</summary>
    public required string ReminderType { get; init; }
    
    /// <summary>Schedule type: once, interval, cron (future)</summary>
    public required string ScheduleType { get; init; }
    
    /// <summary>When to fire (for oneTime)</summary>
    public DateTimeOffset? DueAt { get; init; }
    
    /// <summary>Interval for recurring (e.g., "00:05:00" for 5 minutes)</summary>
    public TimeSpan? Interval { get; init; }
    
    // ── Action ─────────────────────────────────────────────────────────
    /// <summary>Action type: postChat, createBlocker, notifyOwner, retryRun, switchExecutor, moveCard</summary>
    public required string ActionType { get; init; }
    
    /// <summary>Action parameters (JSON)</summary>
    public string? ActionPayloadJson { get; init; }
    
    /// <summary>Prompt to inject (for prompt-based actions)</summary>
    public string? Prompt { get; init; }
    
    // ── Lifecycle ──────────────────────────────────────────────────────
    public string Status { get; set; } = "active"; // active, fired, cancelled, expired
    public string? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastFiredAt { get; set; }
    public DateTimeOffset? NextFireAt { get; set; }
    public int FireCount { get; set; }
    public int? MaxFires { get; init; }
    
    // ── Metadata ───────────────────────────────────────────────────────
    public string? Description { get; init; }
    public string? MetadataJson { get; init; }
}

/// <summary>
/// Reminder types.
/// </summary>
public static class ReminderTypes
{
    public const string OneTime = "oneTime";
    public const string Recurring = "recurring";
    public const string RunWatchdog = "runWatchdog";
    public const string QuotaWatchdog = "quotaWatchdog";
    public const string ReviewReminder = "reviewReminder";
    public const string StaleTicketReminder = "staleTicketReminder";
    public const string SubtreeReminder = "subtreeReminder";
}

/// <summary>
/// Schedule types.
/// </summary>
public static class ScheduleTypes
{
    public const string Once = "once";
    public const string Interval = "interval";
}

/// <summary>
/// Action types for reminders.
/// </summary>
public static class ReminderActionTypes
{
    public const string PostChat = "postChat";
    public const string CreateBlocker = "createBlocker";
    public const string NotifyOwner = "notifyOwner";
    public const string RetryRun = "retryRun";
    public const string SwitchExecutor = "switchExecutor";
    public const string MoveCard = "moveCard";
    public const string CreateEvent = "createEvent";
}
