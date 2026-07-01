namespace KittyClaw.Core.Automation;

/// <summary>
/// Builds handoff prompts when switching executors mid-task.
/// This ensures the new executor has context about what was done before.
/// </summary>
public static class HandoffPromptBuilder
{
    /// <summary>
    /// Build a handoff prompt for taking over a ticket from a previous executor.
    /// </summary>
    public static string Build(
        int ticketId,
        string previousSlot,
        string previousAgent,
        string previousModel,
        string previousStatus,
        string? changedFiles = null,
        string? completedSteps = null,
        string? failingTests = null,
        string? openQuestions = null,
        string? nextSafeAction = null)
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine($"You are taking over Ticket #{ticketId} from previous executor.");
        sb.AppendLine();
        sb.AppendLine("Previous executor:");
        sb.AppendLine($"- slot: {previousSlot}");
        sb.AppendLine($"- agent: {previousAgent}");
        sb.AppendLine($"- model: {previousModel}");
        sb.AppendLine($"- status: {previousStatus}");
        sb.AppendLine();
        sb.AppendLine("Current state:");
        
        if (!string.IsNullOrEmpty(changedFiles))
        {
            sb.AppendLine($"- changed files: {changedFiles}");
        }
        else
        {
            sb.AppendLine("- changed files: (inspect git diff)");
        }
        
        if (!string.IsNullOrEmpty(completedSteps))
        {
            sb.AppendLine($"- completed steps: {completedSteps}");
        }
        
        if (!string.IsNullOrEmpty(failingTests))
        {
            sb.AppendLine($"- failing tests: {failingTests}");
        }
        
        if (!string.IsNullOrEmpty(openQuestions))
        {
            sb.AppendLine($"- open questions: {openQuestions}");
        }
        
        if (!string.IsNullOrEmpty(nextSafeAction))
        {
            sb.AppendLine($"- next safe action: {nextSafeAction}");
        }
        
        sb.AppendLine();
        sb.AppendLine("Continue from the current repository state.");
        sb.AppendLine("Do not repeat completed work.");
        sb.AppendLine("First inspect git diff and relevant files, then proceed.");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build a minimal handoff prompt for quota exhaustion scenarios.
    /// </summary>
    public static string BuildQuotaHandoff(
        int ticketId,
        string previousSlot,
        string previousAgent,
        string previousModel)
    {
        return Build(
            ticketId,
            previousSlot,
            previousAgent,
            previousModel,
            "stopped due to quota exhaustion");
    }
}
