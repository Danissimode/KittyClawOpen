namespace KittyClaw.Core.Automation.Runtimes;

public sealed class AgentRuntimeProjectConfig
{
    public required string ProjectSlug { get; init; }
    public required string WorkspacePath { get; init; }
    public required string DefaultRuntime { get; init; } = "mimo-code";
    public IReadOnlyList<string> HighRiskLabels { get; init; } = new[] { "security", "rls", "payments", "stripe", "critical" };
    public IReadOnlyDictionary<string, string> RuntimeByMember { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, AgentRuntimeConfig> Runtimes { get; init; } = new Dictionary<string, AgentRuntimeConfig>();
}
