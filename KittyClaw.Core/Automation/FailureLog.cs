using System;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Kinds of failures that can block ticket execution.
/// Used to classify, filter, and report failures in the logbook.
/// </summary>
public static class FailureKinds
{
    public const string PlanNotApproved = "plan-not-approved";
    public const string WorktreeCreateFailed = "worktree-create-failed";
    public const string RunnerUnavailable = "runner-unavailable";
    public const string ProviderNotConfigured = "provider-not-configured";
    public const string ModelNotFound = "model-not-found";
    public const string OpenCodeServerUnavailable = "opencode-server-unavailable";
    public const string RunStartFailed = "run-start-failed";
    public const string PolicyDenied = "policy-denied";
    public const string DuplicateRunBlocked = "duplicate-run-blocked";
    public const string CaoNotImplemented = "cao-not-implemented";
    public const string TeamWorkflowNotImplemented = "team-workflow-not-implemented";
    public const string QuotaExhausted = "quota-exhausted";
    public const string RateLimit = "rate-limit";
    public const string ProviderUnavailable = "provider-unavailable";
    public const string NetworkError = "network-error";
    public const string PermissionDenied = "permission-denied";
    public const string Timeout = "timeout";
    public const string UnknownError = "unknown-error";
}

/// <summary>
/// A single failure event attached to a ticket.
/// Persisted in SQLite for production reliability.
/// </summary>
public sealed class FailureLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required int TicketId { get; init; }
    public required string Kind { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RequiredAction { get; init; }
    public string? RunId { get; init; }
    public string? StackTrace { get; init; }
    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? MetadataJson { get; init; }
    
    // ── Extended fields for production diagnostics ──────────────────────
    /// <summary>Which agent was running when failure occurred</summary>
    public string? Agent { get; init; }
    
    /// <summary>Which runner was used (claude, opencode)</summary>
    public string? Runner { get; init; }
    
    /// <summary>Provider that failed (openrouter, anthropic, ollama)</summary>
    public string? Provider { get; init; }
    
    /// <summary>Model that was being used</summary>
    public string? Model { get; init; }
    
    /// <summary>Execution mode at time of failure</summary>
    public string? ExecutionMode { get; init; }
    
    /// <summary>Error classification (quota, rate-limit, auth, network, etc.)</summary>
    public string? ErrorType { get; init; }
    
    /// <summary>Process exit code if applicable</summary>
    public int? ExitCode { get; init; }
    
    /// <summary>Whether a fallback was attempted</summary>
    public bool FallbackUsed { get; init; }
    
    /// <summary>How the failure was resolved (manual, auto-retry, fallback, etc.)</summary>
    public string? Resolution { get; set; }
}