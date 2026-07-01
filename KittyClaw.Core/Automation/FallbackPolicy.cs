namespace KittyClaw.Core.Automation;

/// <summary>
/// A FallbackPolicy defines what happens when a model/provider fails (quota exhausted, rate limit, etc.).
/// It contains an ordered chain of fallback model profiles to try.
/// </summary>
public class FallbackPolicy
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    
    /// <summary>
    /// Trigger reasons that activate this fallback chain.
    /// Values: quota-exhausted, rate-limit, provider-unavailable, model-not-found, network-error
    /// </summary>
    public List<string> TriggerReasons { get; set; } = new();
    
    /// <summary>
    /// Ordered list of model profile IDs to try. First is primary, rest are fallbacks.
    /// </summary>
    public List<string> ModelProfileChain { get; set; } = new();
    
    public int MaxRetries { get; set; } = 2;
    
    /// <summary>
    /// If true, the slot assignment stays the same even when falling back to a different model.
    /// </summary>
    public bool PreserveSlot { get; set; } = true;
    
    /// <summary>
    /// If true, require user approval before downgrading to a weaker model.
    /// Important for orchestrator/parent cards to prevent silent quality degradation.
    /// </summary>
    public bool RequireApprovalForDowngrade { get; set; }
    
    /// <summary>
    /// If true, send notification when fallback is triggered.
    /// </summary>
    public bool Notify { get; set; } = true;
}
