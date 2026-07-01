using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runtimes;
using Xunit;

namespace KittyClaw.Core.Tests.Automation;

public class ExecutionResolverTests
{
    private readonly Dictionary<string, ExecutionSlot> _slots;
    private readonly Dictionary<string, RosterPreset> _presets;
    private readonly Dictionary<string, FallbackPolicy> _policies;
    private readonly Dictionary<string, ModelProfileConfig> _profiles;

    public ExecutionResolverTests()
    {
        _slots = new Dictionary<string, ExecutionSlot>
        {
            ["programmer-1"] = new ExecutionSlot
            {
                Id = "programmer-1",
                Label = "Programmer 1",
                Role = "programmer",
                OpencodeAgent = "beaver-programmer",
                ActiveModelProfileId = "kimi-code-main",
                FallbackPolicyId = "coding-fallback",
                Status = "available"
            },
            ["reviewer"] = new ExecutionSlot
            {
                Id = "reviewer",
                Label = "Reviewer",
                Role = "reviewer",
                OpencodeAgent = "beaver-reviewer",
                ActiveModelProfileId = "flash-reviewer",
                Status = "available"
            }
        };

        _presets = new Dictionary<string, RosterPreset>
        {
            ["balanced-day"] = new RosterPreset
            {
                Id = "balanced-day",
                Label = "Balanced Day",
                IsActive = true,
                Slots = new Dictionary<string, RosterSlotConfig>
                {
                    ["programmer-1"] = new RosterSlotConfig
                    {
                        OpencodeAgent = "beaver-programmer",
                        ModelProfileId = "kimi-code-main"
                    },
                    ["reviewer"] = new RosterSlotConfig
                    {
                        OpencodeAgent = "beaver-reviewer",
                        ModelProfileId = "flash-reviewer"
                    }
                }
            }
        };

        _policies = new Dictionary<string, FallbackPolicy>
        {
            ["coding-fallback"] = new FallbackPolicy
            {
                Id = "coding-fallback",
                TriggerReasons = new List<string> { "quota-exhausted", "rate-limit" },
                ModelProfileChain = new List<string> { "kimi-code-main", "ollama-glm-local", "flash-subagent" }
            }
        };

        _profiles = new Dictionary<string, ModelProfileConfig>
        {
            ["kimi-code-main"] = new ModelProfileConfig
            {
                Id = "kimi-code-main",
                DisplayName = "Kimi 2.7 Code",
                Model = "kimi-2.7-code",
                OpencodeModel = "kimi/kimi-2.7-code",
                Provider = "kimi"
            },
            ["ollama-glm-local"] = new ModelProfileConfig
            {
                Id = "ollama-glm-local",
                DisplayName = "Local GLM",
                Model = "glm-4.7",
                OpencodeModel = "ollama/glm-4.7",
                Provider = "ollama"
            },
            ["flash-subagent"] = new ModelProfileConfig
            {
                Id = "flash-subagent",
                DisplayName = "Flash Subagent",
                Model = "deepseek-flash",
                OpencodeModel = "deepseek/deepseek-flash",
                Provider = "deepseek"
            },
            ["flash-reviewer"] = new ModelProfileConfig
            {
                Id = "flash-reviewer",
                DisplayName = "Flash Reviewer",
                Model = "deepseek-flash",
                OpencodeModel = "deepseek/deepseek-flash",
                Provider = "deepseek"
            }
        };
    }

    [Fact]
    public void Resolve_WithAssignedSlot_ReturnsSlotDefaults()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("programmer-1", result.AssignedSlotId);
        Assert.Equal("beaver-programmer", result.ResolvedAgent);
        Assert.Equal("kimi/kimi-2.7-code", result.ResolvedModel);
        Assert.Equal("kimi-code-main", result.ModelProfileId);
        Assert.Equal("slot-default", result.Reason);
    }

    [Fact]
    public void Resolve_WithNoSlot_AssignsFirstAvailableProgrammer()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: null,
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("programmer-1", result.AssignedSlotId);
        Assert.Equal("no-slot-assigned", result.Reason);
    }

    [Fact]
    public void Resolve_WithLockAndOverride_UsesOverride()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: "ollama-glm-local",
            lockExecutor: true);

        Assert.Equal("ollama-glm-local", result.ModelProfileId);
        Assert.Equal("ollama/glm-4.7", result.ResolvedModel);
        Assert.True(result.LockExecutor);
        Assert.Equal("locked-override", result.Reason);
    }

    [Fact]
    public void Resolve_WithNonLockingOverride_UsesOverride()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: "flash-subagent",
            lockExecutor: false);

        Assert.Equal("flash-subagent", result.ModelProfileId);
        Assert.Equal("deepseek/deepseek-flash", result.ResolvedModel);
        Assert.Equal("override", result.Reason);
    }

    [Fact]
    public void Resolve_EmptySlots_ReturnsNoSlotsConfigured()
    {
        var resolver = CreateResolver(slots: new Dictionary<string, ExecutionSlot>());
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: null,
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("no-slots-configured", result.Reason);
        Assert.Empty(result.AssignedSlotId);
    }

    [Fact]
    public void Resolve_EmptyProfiles_ReturnsNoProfilesConfigured()
    {
        var resolver = CreateResolver(profiles: new Dictionary<string, ModelProfileConfig>());
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("no-profiles-configured", result.Reason);
    }

    [Fact]
    public void Resolve_PresetOverridesSlotDefault()
    {
        // Preset says programmer-1 should use ollama, slot default is kimi
        var presets = new Dictionary<string, RosterPreset>
        {
            ["balanced-day"] = new RosterPreset
            {
                Id = "balanced-day",
                IsActive = true,
                Slots = new Dictionary<string, RosterSlotConfig>
                {
                    ["programmer-1"] = new RosterSlotConfig
                    {
                        ModelProfileId = "ollama-glm-local"
                    }
                }
            }
        };
        
        var resolver = CreateResolver(presets: presets);
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("ollama-glm-local", result.ModelProfileId);
        Assert.Equal("ollama/glm-4.7", result.ResolvedModel);
    }

    [Fact]
    public void Resolve_SetsRosterPresetId()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: null,
            lockExecutor: false);

        Assert.Equal("balanced-day", result.RosterPresetId);
    }

    [Fact]
    public void ResolveFallback_ChainsToNextModel()
    {
        var resolver = CreateResolver();
        
        var original = new ResolvedExecution
        {
            AssignedSlotId = "programmer-1",
            ResolvedModel = "kimi/kimi-2.7-code",
            ModelProfileId = "kimi-code-main",
            FallbackPolicyId = "coding-fallback"
        };

        var fallback = resolver.ResolveFallback(original, "quota-exhausted");

        Assert.Equal("ollama-glm-local", fallback.ModelProfileId);
        Assert.Equal("ollama/glm-4.7", fallback.ResolvedModel);
        Assert.Equal("fallback-quota-exhausted", fallback.Reason);
    }

    [Fact]
    public void ResolveFallback_ExhaustedChain_ReturnsOriginal()
    {
        var resolver = CreateResolver();
        
        var original = new ResolvedExecution
        {
            AssignedSlotId = "programmer-1",
            ResolvedModel = "deepseek/deepseek-flash",
            ModelProfileId = "flash-subagent",
            FallbackPolicyId = "coding-fallback"
        };

        var fallback = resolver.ResolveFallback(original, "quota-exhausted");

        Assert.Equal("fallback-exhausted", fallback.Reason);
        Assert.Equal("flash-subagent", fallback.ModelProfileId);
    }

    [Fact]
    public void ResolveFallback_UnknownTrigger_ReturnsOriginal()
    {
        var resolver = CreateResolver();
        
        var original = new ResolvedExecution
        {
            AssignedSlotId = "programmer-1",
            ResolvedModel = "kimi/kimi-2.7-code",
            ModelProfileId = "kimi-code-main",
            FallbackPolicyId = "coding-fallback"
        };

        var fallback = resolver.ResolveFallback(original, "unknown-error");

        Assert.Equal("slot-default", fallback.Reason);
        Assert.Equal("kimi-code-main", fallback.ModelProfileId);
    }

    [Fact]
    public void Resolve_UsesOpencodeModel_NotModel()
    {
        var resolver = CreateResolver();
        
        var result = resolver.Resolve(
            ticketId: 42,
            assignedSlotId: "programmer-1",
            overrideModelProfileId: null,
            lockExecutor: false);

        // Should use OpencodeModel (provider/model-id format), not Model
        Assert.Equal("kimi/kimi-2.7-code", result.ResolvedModel);
        Assert.NotEqual("kimi-2.7-code", result.ResolvedModel);
    }

    private ExecutionResolver CreateResolver(
        Dictionary<string, ExecutionSlot>? slots = null,
        Dictionary<string, RosterPreset>? presets = null,
        Dictionary<string, ModelProfileConfig>? profiles = null)
    {
        return new ExecutionResolver(
            slots ?? _slots,
            presets ?? _presets,
            _policies,
            profiles ?? _profiles,
            activePresetId: "balanced-day");
    }
}
