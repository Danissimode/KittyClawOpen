using KittyClaw.Core.Automation.Runtimes;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Resolves what model/agent to use for a ticket execution based on:
/// 1. Ticket override (if lockExecutor is set)
/// 2. Assigned slot → active model profile
/// 3. Fallback policy (if primary model fails)
/// 
/// Key principle: Cards are assigned to roles/slots, not specific models.
/// The model is the current implementation of the slot in the active roster.
/// </summary>
public class ExecutionResolver
{
    private readonly Dictionary<string, ExecutionSlot> _slots;
    private readonly Dictionary<string, RosterPreset> _presets;
    private readonly Dictionary<string, FallbackPolicy> _policies;
    private readonly Dictionary<string, ModelProfileConfig> _profiles;
    private readonly string _activePresetId;

    public ExecutionResolver(
        Dictionary<string, ExecutionSlot> slots,
        Dictionary<string, RosterPreset> presets,
        Dictionary<string, FallbackPolicy> policies,
        Dictionary<string, ModelProfileConfig> profiles,
        string activePresetId)
    {
        _slots = slots;
        _presets = presets;
        _policies = policies;
        _profiles = profiles;
        _activePresetId = activePresetId;
    }

    /// <summary>
    /// Resolve execution plan for a ticket.
    /// </summary>
    public ResolvedExecution Resolve(
        int ticketId,
        string? assignedSlotId,
        string? overrideModelProfileId,
        bool lockExecutor,
        string? handoffFromRunId = null)
    {
        var result = new ResolvedExecution
        {
            HandoffFromRunId = handoffFromRunId,
            RosterPresetId = _activePresetId
        };

        // 1. Check ticket override (lockExecutor takes precedence)
        if (lockExecutor && !string.IsNullOrEmpty(overrideModelProfileId))
        {
            return ResolveFromOverride(ticketId, overrideModelProfileId, result);
        }

        // 2. Look up assigned slot
        if (string.IsNullOrEmpty(assignedSlotId) || !_slots.TryGetValue(assignedSlotId, out var slot))
        {
            // No slot assigned — use first available programmer slot or default
            if (_slots.Count == 0)
            {
                result.Reason = "no-slots-configured";
                return result;
            }
            
            slot = _slots.Values.FirstOrDefault(s => s.Role == "programmer" && s.Status == "available")
                ?? _slots.Values.FirstOrDefault(s => s.Status == "available")
                ?? _slots.Values.First();
            
            result.Reason = "no-slot-assigned";
        }
        else
        {
            result.Reason = "slot-default";
        }

        result.AssignedSlotId = slot.Id;
        result.ResolvedAgent = slot.OpencodeAgent;
        result.FallbackPolicyId = slot.FallbackPolicyId;

        // 3. Check for non-locking override
        if (!lockExecutor && !string.IsNullOrEmpty(overrideModelProfileId))
        {
            result.ResolvedModel = ResolveModelFromProfile(overrideModelProfileId);
            result.ModelProfileId = overrideModelProfileId;
            result.Reason = "override";
            return result;
        }

        // 4. Apply roster preset override if slot config exists
        // Preset is the source of truth for slot → model mapping
        if (_presets.TryGetValue(_activePresetId, out var preset) &&
            preset.Slots.TryGetValue(slot.Id, out var slotConfig) &&
            !string.IsNullOrEmpty(slotConfig.ModelProfileId) &&
            _profiles.ContainsKey(slotConfig.ModelProfileId))
        {
            result.ModelProfileId = slotConfig.ModelProfileId;
            result.ResolvedModel = ResolveModelFromProfile(slotConfig.ModelProfileId);
            return result;
        }

        // 5. Fall back to slot's active model profile
        var profileId = slot.ActiveModelProfileId;
        if (string.IsNullOrEmpty(profileId) || !_profiles.ContainsKey(profileId))
        {
            // Fall back to first available profile
            if (_profiles.Count > 0)
            {
                profileId = _profiles.Keys.First();
                result.Reason = "profile-fallback";
            }
            else
            {
                result.Reason = "no-profiles-configured";
                return result;
            }
        }

        result.ModelProfileId = profileId;
        result.ResolvedModel = ResolveModelFromProfile(profileId);

        return result;
    }

    /// <summary>
    /// Resolve fallback chain when primary model fails.
    /// </summary>
    public ResolvedExecution ResolveFallback(
        ResolvedExecution original,
        string failureReason)
    {
        if (string.IsNullOrEmpty(original.FallbackPolicyId) ||
            !_policies.TryGetValue(original.FallbackPolicyId, out var policy))
        {
            return original;
        }

        // Check if this failure reason triggers the fallback
        if (!policy.TriggerReasons.Contains(failureReason))
        {
            return original;
        }

        // Find current position in chain and get next
        var currentIndex = policy.ModelProfileChain.IndexOf(original.ModelProfileId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = currentIndex + 1;
        if (nextIndex >= policy.ModelProfileChain.Count)
        {
            // No more fallbacks
            original.Reason = "fallback-exhausted";
            return original;
        }

        var nextProfileId = policy.ModelProfileChain[nextIndex];
        if (!_profiles.TryGetValue(nextProfileId, out var nextProfile))
        {
            original.Reason = "fallback-profile-missing";
            return original;
        }

        return new ResolvedExecution
        {
            AssignedSlotId = original.AssignedSlotId,
            ResolvedAgent = original.ResolvedAgent,
            ResolvedModel = nextProfile.OpencodeModel ?? nextProfile.Model,
            ModelProfileId = nextProfileId,
            RosterPresetId = original.RosterPresetId,
            FallbackPolicyId = original.FallbackPolicyId,
            Reason = $"fallback-{failureReason}",
            LockExecutor = original.LockExecutor,
            HandoffFromRunId = original.HandoffFromRunId,
            ResolutionNotes = $"Fallback from {original.ModelProfileId} to {nextProfileId} due to {failureReason}"
        };
    }

    private ResolvedExecution ResolveFromOverride(int ticketId, string profileId, ResolvedExecution result)
    {
        result.ModelProfileId = profileId;
        result.ResolvedModel = ResolveModelFromProfile(profileId);
        result.Reason = "locked-override";
        result.LockExecutor = true;
        result.ResolutionNotes = $"Locked to profile {profileId} by ticket override";
        return result;
    }

    private string ResolveModelFromProfile(string profileId)
    {
        if (_profiles.TryGetValue(profileId, out var profile))
        {
            // Use OpencodeModel (provider/model-id format) if available, fall back to Model
            return profile.OpencodeModel ?? profile.Model;
        }
        return "";
    }
}
