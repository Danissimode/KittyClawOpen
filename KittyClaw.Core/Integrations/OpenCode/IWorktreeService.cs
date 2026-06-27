using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Automation.Runtimes;

namespace KittyClaw.Core.Integrations.OpenCode;

/// <summary>
/// Interface for worktree management.
/// This service handles creation, resolution, and cleanup of per-ticket worktrees.
/// </summary>
public interface IWorktreeService
{
    /// <summary>
    /// Ensure a worktree exists for a ticket and return its information
    /// </summary>
    Task<WorktreeInfo> EnsureForTicketAsync(TicketExecutionContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get worktree information for a ticket (returns null if not exists)
    /// </summary>
    Task<WorktreeInfo?> GetForTicketAsync(string projectSlug, int ticketId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Merge worktree changes back to main branch
    /// </summary>
    Task<WorktreeMergeResult> MergeAsync(string projectSlug, int ticketId, WorktreeMergeOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cleanup worktree for a ticket
    /// </summary>
    Task<WorktreeCleanupResult> CleanupAsync(string projectSlug, int ticketId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the worktree root directory for a project
    /// </summary>
    string GetWorktreeRoot(string projectSlug, string workspacePath);
}

/// <summary>
/// Context for ticket execution
/// </summary>
public sealed class TicketExecutionContext
{
    public required string ProjectSlug { get; init; }
    public required string WorkspacePath { get; init; }
    public required int TicketId { get; init; }
    public string? TicketTitle { get; init; }
    public string? Assignee { get; init; }
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.DirectOpenCode;
    public bool ForceWorktree { get; init; } = true;
}

/// <summary>
/// Information about a worktree
/// </summary>
public sealed record WorktreeInfo
{
    public required string ProjectSlug { get; init; }
    public required int TicketId { get; init; }
    public required string WorktreePath { get; init; }
    public required string BranchName { get; init; }
    public required string RootPath { get; init; }
    public bool Exists { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>
/// Options for merging a worktree
/// </summary>
public sealed class WorktreeMergeOptions
{
    public string? CommitMessage { get; init; }
    public string? TargetBranch { get; init; } = "main";
    public bool CreateMergeCommit { get; init; } = true;
    public bool DeleteAfterMerge { get; init; } = true;
    public bool FastForwardOnly { get; init; } = false;
}

/// <summary>
/// Result of a worktree merge operation
/// </summary>
public sealed class WorktreeMergeResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MergeCommitHash { get; init; }
    public bool WorktreeDeleted { get; init; }
    public string? BranchName { get; init; }
}

/// <summary>
/// Result of a worktree cleanup operation
/// </summary>
public sealed class WorktreeCleanupResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WorktreeDeleted { get; init; }
    public bool BranchDeleted { get; init; }
}
