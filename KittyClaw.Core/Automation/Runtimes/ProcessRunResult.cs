namespace KittyClaw.Core.Automation.Runtimes;

public sealed record ProcessRunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool TimedOut
);
