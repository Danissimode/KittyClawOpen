namespace KittyClaw.Core.Automation.Runtimes;

public interface IAgentRuntime
{
    string Id { get; }
    Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken);
}
