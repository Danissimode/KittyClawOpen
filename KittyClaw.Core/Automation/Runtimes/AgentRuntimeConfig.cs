namespace KittyClaw.Core.Automation.Runtimes;

public sealed class AgentRuntimeConfig
{
    public required string Id { get; init; }
    public required bool Enabled { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();
    public PromptMode PromptMode { get; init; } = PromptMode.Argument;
    public int TimeoutSeconds { get; init; } = 1800;
    public bool Experimental { get; init; } = false;
    public string? WorkingDirectoryOverride { get; init; }
    public string? Model { get; init; }
    public string? Agent { get; init; }
    public string? OutputFormat { get; init; } = "json";
    public bool DangerouslySkipPermissions { get; init; } = false;
    public bool AllowAutoApprove { get; init; } = false;
    public int MaxTurns { get; init; } = 200;
    public string? SessionScope { get; init; }
    public string? InlineSkillContent { get; init; }
    public string? ConcurrencyGroup { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
