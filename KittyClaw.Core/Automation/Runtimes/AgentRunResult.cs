namespace KittyClaw.Core.Automation.Runtimes;

public sealed record AgentRunResult(
    AgentRunStatus Status,
    int? ExitCode,
    string Stdout,
    string Stderr,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    string RuntimeId,
    string CommandDisplay,
    IReadOnlyList<string> Artifacts,
    string? RunId = null
);
