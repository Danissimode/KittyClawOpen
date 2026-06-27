using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KittyClaw.Core.Automation.Runners;

/// <summary>
/// Interface for storing and retrieving ticket execution metadata.
/// This is a stable extension point that allows different storage backends.
/// </summary>
public interface ITicketExecutionMetadataStore
{
    /// <summary>
    /// Save execution metadata for a ticket
    /// </summary>
    Task SaveAsync(TicketExecutionMetadata metadata, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get execution metadata for a ticket
    /// </summary>
    Task<TicketExecutionMetadata?> GetAsync(string projectSlug, int ticketId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get execution metadata by run ID
    /// </summary>
    Task<TicketExecutionMetadata?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update execution metadata for a ticket
    /// </summary>
    Task UpdateAsync(TicketExecutionMetadata metadata, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all execution metadata for a project
    /// </summary>
    Task<IReadOnlyList<TicketExecutionMetadata>> GetByProjectAsync(string projectSlug, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete execution metadata for a ticket
    /// </summary>
    Task DeleteAsync(string projectSlug, int ticketId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata about a ticket execution
/// </summary>
public sealed class TicketExecutionMetadata
{
    public required string ProjectSlug { get; init; }
    public required int TicketId { get; init; }
    public required string RunId { get; init; }
    public required string ExecutionMode { get; init; }
    public required string RunnerKind { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Profile { get; init; }
    public string? OpenCodeAgent { get; init; }
    public string? SessionId { get; init; }
    public string? WorktreePath { get; init; }
    public string? BranchName { get; init; }
    public string? RootPath { get; init; }
    public required AgentRunStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? LastError { get; init; }
    public string? PlanStatus { get; init; }
    public string? RiskLevel { get; init; }
    public bool ReviewRequired { get; init; }
    public bool ProofRequired { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
