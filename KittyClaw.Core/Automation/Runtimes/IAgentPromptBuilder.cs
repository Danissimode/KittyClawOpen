namespace KittyClaw.Core.Automation.Runtimes;

public interface IAgentPromptBuilder
{
    string BuildPrompt(AgentRunRequest request);
}
