using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class ScriptRuntime : IAgentRuntime
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<ScriptRuntime>? _logger;

    public string Id => AgentRuntimeIds.Script;

    public ScriptRuntime(ProcessRunner processRunner, ILogger<ScriptRuntime>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var config = request.RuntimeConfig;
        var workingDir = config.WorkingDirectoryOverride ?? request.WorkspacePath;
        var arguments = new List<string>(config.Args);

        string? promptInput = null;
        if (config.PromptMode == PromptMode.Argument && !string.IsNullOrEmpty(request.Prompt))
        {
            arguments.Add(request.Prompt);
        }
        else if (config.PromptMode == PromptMode.Stdin && !string.IsNullOrEmpty(request.Prompt))
        {
            promptInput = request.Prompt;
        }
        else if (config.PromptMode == PromptMode.TempFile && !string.IsNullOrEmpty(request.Prompt))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"kittyclaw-script-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempFile, request.Prompt, cancellationToken);
            arguments.Add(tempFile);
        }

        var commandDisplay = $"{config.Command} {string.Join(" ", arguments.Select(a => $"\"{a}\""))}";

        var processRequest = new ProcessRunRequest(
            FileName: config.Command,
            Arguments: arguments,
            WorkingDirectory: workingDir,
            StandardInput: promptInput,
            Environment: config.Environment,
            Timeout: TimeSpan.FromSeconds(config.TimeoutSeconds)
        );

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await _processRunner.RunAsync(processRequest, cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;

            var status = result.ExitCode == 0
                ? AgentRunStatus.Completed
                : (result.TimedOut ? AgentRunStatus.Stopped : AgentRunStatus.Failed);

            return new AgentRunResult(
                Status: status,
                ExitCode: result.ExitCode,
                Stdout: result.Stdout,
                Stderr: result.Stderr,
                StartedAt: startedAt,
                FinishedAt: finishedAt,
                Duration: finishedAt - startedAt,
                RuntimeId: Id,
                CommandDisplay: commandDisplay,
                Artifacts: Array.Empty<string>(),
                RunId: request.RunId
            );
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.UtcNow;
            return new AgentRunResult(
                Status: AgentRunStatus.Stopped,
                ExitCode: null,
                Stdout: "",
                Stderr: "",
                StartedAt: startedAt,
                FinishedAt: finishedAt,
                Duration: finishedAt - startedAt,
                RuntimeId: Id,
                CommandDisplay: commandDisplay,
                Artifacts: Array.Empty<string>()
            );
        }
    }
}
