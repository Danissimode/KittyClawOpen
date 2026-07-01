namespace KittyClaw.Core.Automation;

/// <summary>
/// A RosterPreset defines a team configuration — which slots map to which agents/models.
/// Users can switch presets quickly (e.g., "Balanced Day" → "Local Only" when quota runs out).
/// </summary>
public class RosterPreset
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, RosterSlotConfig> Slots { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public int SortOrder { get; set; }
}

public class RosterSlotConfig
{
    public string OpencodeAgent { get; set; } = "";
    public string ModelProfileId { get; set; } = "";
    public string? Notes { get; set; }
}
