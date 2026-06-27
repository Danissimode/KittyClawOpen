using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KittyClaw.Core.Automation.Runners;

/// <summary>
/// Generic interface for agent runners.
/// This is a stable extension point that allows adding new runners (OpenCode, CAO, etc.)
/// without modifying core AutomationEngine logic.
/// </summary>
public interface IAgentRunner
{
    /// <summary>
    /// Unique identifier for this runner type (e.g., "claude", "opencode", "cao")
    /// </summary>
    string Kind { get; }
    
    /// <summary>
    /// Human-readable display name for this runner
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Whether this runner is available and properly configured
    /// </summary>
    bool IsAvailable { get; }
    
    /// <summary>
    /// Start an agent run with the specified request
    /// </summary>
    Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken);
    
    /// <summary>
    /// Stop an ongoing run
    /// </summary>
    Task<bool> StopAsync(string runId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Send a steering message to an ongoing run
    /// </summary>
    Task<bool> SteerAsync(string runId, string message, CancellationToken cancellationToken);
    
    /// <summary>
    /// Get the current status of a run
    /// </summary>
    Task<AgentRunStatus> GetStatusAsync(string runId, CancellationToken cancellationToken);
}

/// <summary>
/// Result from an agent run
/// </summary>
public sealed class AgentRunResult
{
    public required AgentRunStatus Status { get; init; }
    public int? ExitCode { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public string RunnerKind { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string? RunId { get; init; }
    public string? CommandDisplay { get; init; }
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();
    public ExecutionMetadata? ExecutionMetadata { get; init; }
}

/// <summary>
/// Request to start an agent run
/// </summary>
public sealed class AgentRunRequest
{
    public required string ProjectSlug { get; init; }
    public required string WorkspacePath { get; init; }
    public required string AgentName { get; init; }
    public required string SkillFile { get; init; }
    public int? TicketId { get; init; }
    public string? TicketTitle { get; init; }
    public string? TicketStatus { get; init; }
    public string? TicketDescription { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public string? Assignee { get; init; }
    public string? CurrentColumn { get; init; }
    public required string Prompt { get; init; }
    public string? ConcurrencyGroup { get; init; }
    public int MaxTurns { get; init; } = 200;
    public IDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? Profile { get; init; }
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.LegacyClaude;
    public string? WorktreePath { get; init; }
    public string? BranchName { get; init; }
    public string? RunId { get; init; }
    public ExecutionMetadata? ExecutionMetadata { get; init; }
    public Action<StreamEvent>? OnEventHook { get; init; }
}

/// <summary>
/// Execution modes for agent runs
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Legacy ClaudeRunner path - existing behavior
    /// </summary>
    LegacyClaude,
    
    /// <summary>
    /// Direct OpenCode execution without CAO governance
    /// </summary>
    DirectOpenCode,
    
    /// <summary>
    /// CAO-governed execution (stub for future implementation)
    /// </summary>
    CaoGoverned,
    
    /// <summary>
    /// Team workflow execution (stub for future implementation)
    /// </summary>
    TeamWorkflow,
    
    /// <summary>
    /// Manual execution mode
    /// </summary>
    Manual
}

/// <summary>
/// Execution metadata for agent runs
/// </summary>
public sealed class ExecutionMetadata
{
    public string? Mode { get; set; }
    public string? Runner { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Profile { get; set; }
    public string? RunId { get; set; }
    public string? SessionId { get; set; }
    public string? WorktreePath { get; set; }
    public string? BranchName { get; set; }
    public string? TicketId { get; set; }
    public string? ProjectId { get; set; }
    public string? OpenCodeAgent { get; set; }
    public bool SteerSupported { get; set; } = true;
    public string? LastError { get; set; }
}
