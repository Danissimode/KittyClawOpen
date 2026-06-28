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
}

/// <summary>
/// A single failure event attached to a ticket.
/// Persisted in-memory and optionally flushed to disk.
/// </summary>
public sealed class FailureLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectSlug { get; init; }
    public required int TicketId { get; init; }
    public required string Kind { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? RequiredAction { get; init; }
    public string? RunId { get; init; }
    public string? StackTrace { get; init; }
    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}