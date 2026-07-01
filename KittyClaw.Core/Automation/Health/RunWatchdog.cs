using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// Monitors agent runs for stuck/silent/hung conditions.
/// Creates ProcessEvents when issues are detected.
/// </summary>
public sealed class RunWatchdog
{
    private readonly ProcessEventStore _eventStore;
    private readonly AgentRunRegistry _runRegistry;
    private readonly ILogger? _logger;

    // Watchdog thresholds
    private static readonly TimeSpan SilentTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(2);
    private static readonly TimeSpan StaleReviewTimeout = TimeSpan.FromHours(24);

    public RunWatchdog(
        ProcessEventStore eventStore,
        AgentRunRegistry runRegistry,
        ILogger? logger = null)
    {
        _eventStore = eventStore;
        _runRegistry = runRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Check all active runs for issues.
    /// </summary>
    public async Task CheckAllRunsAsync(string projectSlug, CancellationToken ct = default)
    {
        var activeRuns = _runRegistry.ActiveForProject(projectSlug).ToList();
        
        foreach (var run in activeRuns)
        {
            await CheckRunAsync(projectSlug, run, ct);
        }
    }

    /// <summary>
    /// Check a single run for issues.
    /// </summary>
    public async Task CheckRunAsync(string projectSlug, AgentRun run, CancellationToken ct = default)
    {
        if (run.StartedAt is null) return;
        
        var elapsed = DateTime.UtcNow - run.StartedAt.Value;
        
        // Check for excessive run duration
        if (elapsed > MaxRunDuration)
        {
            await CreateEventAsync(
                projectSlug,
                ProcessEventLevels.Warning,
                ProcessEventCategories.Watchdog,
                ProcessEventTypes.RunExceededMaxTime,
                $"Run {run.RunId} exceeded max duration ({elapsed.TotalHours:F1}h)",
                $"Agent: {run.AgentName}, Ticket: #{run.TicketId}",
                run.RunId,
                run.TicketId,
                run.AgentName,
                ct);
        }
        
        // Check for stale run (no events for a while)
        if (elapsed > SilentTimeout && run.Status == AgentRunStatus.Running)
        {
            // Check if there are recent events indicating activity
            var hasRecentActivity = await HasRecentActivityAsync(run.RunId, SilentTimeout, ct);
            
            if (!hasRecentActivity)
            {
                await CreateEventAsync(
                    projectSlug,
                    ProcessEventLevels.Warning,
                    ProcessEventCategories.Watchdog,
                    ProcessEventTypes.RunSilentTimeout,
                    $"Run {run.RunId} has no output for {elapsed.TotalMinutes:F0} minutes",
                    $"Agent: {run.AgentName}, Ticket: #{run.TicketId}. Run may be stuck.",
                    run.RunId,
                    run.TicketId,
                    run.AgentName,
                    ct);
            }
        }
    }

    /// <summary>
    /// Check for stale reviews (tickets in Review column too long).
    /// </summary>
    public async Task CheckStaleReviewsAsync(string projectSlug, IEnumerable<Core.Models.Ticket> reviewTickets, CancellationToken ct = default)
    {
        foreach (var ticket in reviewTickets)
        {
            // Check if ticket has been in Review for too long
            var lastActivity = ticket.UpdatedAt;
            if (DateTime.UtcNow - lastActivity > StaleReviewTimeout)
            {
                await CreateEventAsync(
                    projectSlug,
                    ProcessEventLevels.Info,
                    ProcessEventCategories.Reminder,
                    ProcessEventTypes.ReviewReminder,
                    $"Ticket #{ticket.Id} awaiting review for {(DateTime.UtcNow - lastActivity).TotalHours:F0}h",
                    $"Title: {ticket.Title}. Consider reviewing or requesting changes.",
                    null,
                    ticket.Id,
                    null,
                    ct);
            }
        }
    }

    private async Task<bool> HasRecentActivityAsync(string runId, TimeSpan window, CancellationToken ct)
    {
        // Check if the run has had any recent events
        // This is a simplified check - in production, check actual event stream
        var run = _runRegistry.Get(runId);
        if (run is null) return false;
        
        // If the run was recently registered or has recent events, consider it active
        if (run.StartedAt.HasValue && (DateTime.UtcNow - run.StartedAt.Value) < window)
        {
            return true;
        }
        
        return false;
    }

    private async Task CreateEventAsync(
        string projectSlug,
        string level,
        string category,
        string eventType,
        string title,
        string message,
        string? runId,
        int? ticketId,
        string? agentId,
        CancellationToken ct)
    {
        await _eventStore.RecordAsync(new ProcessEvent
        {
            ProjectSlug = projectSlug,
            Level = level,
            Category = category,
            EventType = eventType,
            Title = title,
            Message = message,
            RunId = runId,
            TicketId = ticketId,
            AgentId = agentId,
            Source = "run-watchdog",
            SuggestedActionsJson = GetSuggestedActions(eventType)
        }, ct);
    }

    private string GetSuggestedActions(string eventType) => eventType switch
    {
        ProcessEventTypes.RunSilentTimeout => "[\"steer-run\",\"stop-run\",\"check-agent-health\"]",
        ProcessEventTypes.RunExceededMaxTime => "[\"stop-run\",\"review-progress\",\"escalate\"]",
        ProcessEventTypes.SseDisconnected => "[\"restart-run\",\"check-network\"]",
        ProcessEventTypes.ProcessExitedUnexpectedly => "[\"restart-run\",\"check-logs\"]",
        _ => "[]"
    };
}
