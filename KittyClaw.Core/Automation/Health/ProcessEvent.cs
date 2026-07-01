namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// A structured event in the Process Event Ledger.
/// Replaces simple logging with actionable, linked, resolvable events.
/// </summary>
public sealed class ProcessEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    
    /// <summary>Event severity: info, warning, error, critical</summary>
    public required string Level { get; init; }
    
    /// <summary>Event category: quota, watchdog, failure, reminder, system, health</summary>
    public required string Category { get; init; }
    
    /// <summary>Specific event type within category</summary>
    public required string EventType { get; init; }
    
    /// <summary>Human-readable title</summary>
    public required string Title { get; init; }
    
    /// <summary>Detailed description</summary>
    public string? Message { get; init; }
    
    // ── Links to entities ──────────────────────────────────────────────
    public int? TicketId { get; init; }
    public string? RunId { get; init; }
    public string? AgentId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? SessionId { get; init; }
    
    // ── Source and context ─────────────────────────────────────────────
    /// <summary>What generated this event: quota-probe, watchdog, automation, manual</summary>
    public required string Source { get; init; }
    
    /// <summary>Raw data from the source (JSON string)</summary>
    public string? RawPayload { get; init; }
    
    // ── Lifecycle ──────────────────────────────────────────────────────
    /// <summary>Event status: open, acknowledged, resolved, dismissed</summary>
    public string Status { get; set; } = "open";
    
    /// <summary>How the event was resolved</summary>
    public string? Resolution { get; set; }
    
    /// <summary>Who resolved the event</summary>
    public string? ResolvedBy { get; set; }
    
    // ── Suggested actions ──────────────────────────────────────────────
    /// <summary>JSON array of suggested action slugs</summary>
    public string? SuggestedActionsJson { get; init; }
    
    // ── Timestamps ─────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// Event types for each category.
/// </summary>
public static class ProcessEventTypes
{
    // Quota events
    public const string QuotaThresholdReached = "QuotaThresholdReached";
    public const string QuotaExhausted = "QuotaExhausted";
    public const string QuotaRecovered = "QuotaRecovered";
    public const string QuotaProbeFailed = "QuotaProbeFailed";
    public const string QuotaSourceMissing = "QuotaSourceMissing";
    
    // Watchdog events
    public const string RunSilentTimeout = "RunSilentTimeout";
    public const string RunExceededMaxTime = "RunExceededMaxTime";
    public const string RunStuck = "RunStuck";
    public const string SseDisconnected = "SseDisconnected";
    public const string ProcessExitedUnexpectedly = "ProcessExitedUnexpectedly";
    
    // Failure events
    public const string RunFailed = "RunFailed";
    public const string AgentUnavailable = "AgentUnavailable";
    public const string ProviderUnavailable = "ProviderUnavailable";
    
    // Reminder events
    public const string ReviewReminder = "ReviewReminder";
    public const string StaleTicketReminder = "StaleTicketReminder";
    public const string QuotaWatchdogReminder = "QuotaWatchdogReminder";
    
    // System events
    public const string StartupHealthCheck = "StartupHealthCheck";
    public const string ConfigReload = "ConfigReload";
}

/// <summary>
/// Event levels.
/// </summary>
public static class ProcessEventLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}

/// <summary>
/// Event categories.
/// </summary>
public static class ProcessEventCategories
{
    public const string Quota = "quota";
    public const string Watchdog = "watchdog";
    public const string Failure = "failure";
    public const string Reminder = "reminder";
    public const string System = "system";
    public const string Health = "health";
}

/// <summary>
/// Event statuses.
/// </summary>
public static class ProcessEventStatuses
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";
}
