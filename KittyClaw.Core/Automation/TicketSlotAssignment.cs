namespace KittyClaw.Core.Automation;

/// <summary>
/// Maps a ticket to an execution slot, with optional override and lock settings.
/// Cards are assigned to roles/slots, not specific models — the model is resolved from the active roster.
/// </summary>
public class TicketSlotAssignment
{
    public int TicketId { get; set; }
    public string AssignedSlotId { get; set; } = "";
    
    /// <summary>
    /// Optional override model profile for this specific ticket.
    /// Takes precedence over slot's default model.
    /// </summary>
    public string? OverrideModelProfileId { get; set; }
    
    /// <summary>
    /// If true, prevent auto-fallback from moving this ticket to a different model.
    /// Use for critical/security/release tasks that must run on a specific model.
    /// </summary>
    public bool LockExecutor { get; set; }
    
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string AssignedBy { get; set; } = ""; // "owner" or agent name
    public string? Notes { get; set; }
}
