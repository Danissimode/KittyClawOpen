using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class ClaudeCodeRuntime : IAgentRuntime
{
    private readonly ClaudeRunner _claudeRunner;
    private readonly ILogger<ClaudeCodeRuntime>? _logger;

    public string Id => AgentRuntimeIds.ClaudeCode;

    public ClaudeCodeRuntime(ClaudeRunner claudeRunner, ILogger<ClaudeCodeRuntime>? logger = null)
    {
        _claudeRunner = claudeRunner;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var config = request.RuntimeConfig;
        var skillFile = $"{request.Assignee}/SKILL.md";
        var ctx = new ClaudeRunContext
        {
            ProjectSlug = request.ProjectSlug,
            WorkspacePath = request.WorkspacePath,
            AgentName = request.Assignee,
            SkillFile = skillFile,
            TicketId = request.TicketId,
            TicketTitle = request.TicketTitle,
            TicketStatus = request.CurrentColumn,
            MaxTurns = config.MaxTurns,
            ConcurrencyGroup = config.ConcurrencyGroup ?? request.Assignee,
            ExtraContext = request.Prompt,
            RetryOnResumeFailure = true,
            PersistSession = true,
            PresetRunId = request.RunId,
            SessionScope = config.SessionScope,
            InlineSkillContent = config.InlineSkillContent,
        };

        var run = await _claudeRunner.RunAsync(ctx, cancellationToken);

        var duration = run.EndedAt.HasValue
            ? run.EndedAt.Value - run.StartedAt
            : TimeSpan.Zero;

        var artifacts = new List<string>();

        return new AgentRunResult(
            Status: run.Status,
            ExitCode: run.ExitCode,
            Stdout: "",
            Stderr: "",
            StartedAt: run.StartedAt,
            FinishedAt: run.EndedAt ?? DateTimeOffset.UtcNow,
            Duration: duration,
            RuntimeId: Id,
            CommandDisplay: $"claude --print --session-id {run.SessionId ?? "new"}",
            Artifacts: artifacts,
            RunId: request.RunId
        );
    }
}
