namespace KittyClaw.Core.Automation.Runtimes;

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout
);
