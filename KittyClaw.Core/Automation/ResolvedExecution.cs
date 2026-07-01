namespace KittyClaw.Core.Automation;

/// <summary>
/// The result of resolving what model/agent to use for a ticket execution.
/// This is an immutable snapshot stored in AgentRun.ExecutionMetadata.
/// </summary>
public class ResolvedExecution
{
    public string AssignedSlotId { get; set; } = "";
    public string ResolvedAgent { get; set; } = "";
    public string ResolvedModel { get; set; } = "";
    public string ModelProfileId { get; set; } = "";
    public string RosterPresetId { get; set; } = "";
    public string FallbackPolicyId { get; set; } = "";
    
    /// <summary>
    /// Why this resolution was made: "slot-default", "override", "fallback", "emergency"
    /// </summary>
    public string Reason { get; set; } = "slot-default";
    
    public bool LockExecutor { get; set; }
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// If this run was handed off from a previous executor, store the previous run ID.
    /// </summary>
    public string? HandoffFromRunId { get; set; }
    
    /// <summary>
    /// Human-readable summary of why this executor was chosen.
    /// </summary>
    public string? ResolutionNotes { get; set; }
}
