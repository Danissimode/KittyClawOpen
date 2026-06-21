namespace KittyClaw.Core.Automation.Runtimes;

public sealed class PromptBuilder : IAgentPromptBuilder
{
    public string BuildPrompt(AgentRunRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# KittyClawOpen Agent Task");
        sb.AppendLine($"Project: {request.ProjectSlug}");
        sb.AppendLine($"Workspace: {request.WorkspacePath}");
        sb.AppendLine($"Ticket: #{request.TicketId}");
        sb.AppendLine($"Assignee: {request.Assignee}");
        sb.AppendLine($"Runtime: {request.RuntimeConfig.Id}");
        sb.AppendLine($"Column: {request.CurrentColumn}");
        sb.AppendLine($"Labels: {string.Join(", ", request.Labels)}");
        sb.AppendLine("## Task Contract");
        sb.AppendLine(request.TicketDescription ?? "No description provided.");
        sb.AppendLine("## Mandatory Rules");
        sb.AppendLine("- Do not use Next.js.");
        sb.AppendLine("- Use the existing project stack.");
        sb.AppendLine("- Respect project governance.");
        sb.AppendLine("- Do not create duplicate architecture.");
        sb.AppendLine("- Do not auto-mark this task as Done.");
        sb.AppendLine("- For high-risk labels, produce evidence and request human review.");
        sb.AppendLine("## Required Final Response");
        sb.AppendLine("Return:");
        sb.AppendLine("1. changed files;");
        sb.AppendLine("2. commands run;");
        sb.AppendLine("3. validation results;");
        sb.AppendLine("4. risks;");
        sb.AppendLine("5. recommended next status.");
        return sb.ToString();
    }
}
