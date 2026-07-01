namespace KittyClaw.Core.Automation;

/// <summary>
/// An ExecutionSlot represents an "executor" in the UI — a named role that cards can be assigned to.
/// The actual model/provider is resolved from the active roster, allowing quick swaps without changing card assignments.
/// </summary>
public class ExecutionSlot
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Role { get; set; } = ""; // programmer, reviewer, qa, orchestrator, documentalist
    public string OpencodeAgent { get; set; } = "";
    public string ActiveModelProfileId { get; set; } = "";
    public string FallbackPolicyId { get; set; } = "";
    public string Status { get; set; } = "available"; // available, busy, paused, error
    public DateTime? LastUsedAt { get; set; }
    public int? LastRunTicketId { get; set; }
    public string? Notes { get; set; }
}
