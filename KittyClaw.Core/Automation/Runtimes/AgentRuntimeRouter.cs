namespace KittyClaw.Core.Automation.Runtimes;

public sealed class AgentRuntimeRouter
{
    private readonly AgentRuntimeProjectConfig _config;
    private readonly IReadOnlyDictionary<string, IAgentRuntime> _runtimes;

    public AgentRuntimeRouter(AgentRuntimeProjectConfig config, IEnumerable<IAgentRuntime> runtimes)
    {
        _config = config;
        _runtimes = runtimes.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IAgentRuntime Resolve(string assignee)
    {
        var runtimeId = _config.RuntimeByMember.TryGetValue(assignee, out var mapped) ? mapped : _config.DefaultRuntime;
        if (!_runtimes.TryGetValue(runtimeId, out var runtime))
            throw new InvalidOperationException($"Runtime '{runtimeId}' is not registered.");
        return runtime;
    }

    public bool IsHighRisk(IReadOnlyList<string> labels) =>
        labels.Any(l => _config.HighRiskLabels.Contains(l, StringComparer.OrdinalIgnoreCase));
}
