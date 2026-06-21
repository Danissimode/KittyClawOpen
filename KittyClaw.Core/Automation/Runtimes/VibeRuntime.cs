using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class VibeRuntime : IAgentRuntime
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<VibeRuntime>? _logger;

    public string Id => AgentRuntimeIds.Vibe;

    public VibeRuntime(ProcessRunner processRunner, ILogger<VibeRuntime>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var config = request.RuntimeConfig;

        if (config.AllowAutoApprove)
        {
            _logger?.LogWarning("Vibe runtime was configured with allowAutoApprove=true; this is discouraged for unattended automation.");
        }

        var workingDir = config.WorkingDirectoryOverride ?? request.WorkspacePath;
        var arguments = new List<string>(config.Args);

        var title = $"KC #{request.TicketId}: {request.TicketTitle}";

        string? promptInput = null;
        if (config.PromptMode == PromptMode.Argument)
        {
            arguments.Add(request.Prompt);
        }
        else if (config.PromptMode == PromptMode.Stdin)
        {
            promptInput = request.Prompt;
        }
        else if (config.PromptMode == PromptMode.TempFile)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"kittyclaw-vibe-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempFile, request.Prompt, cancellationToken);
            arguments.Add(tempFile);
        }

        arguments.Add("--max-turns");
        arguments.Add(config.MaxTurns > 0 ? config.MaxTurns.ToString() : "5");

        if (!string.IsNullOrWhiteSpace(config.OutputFormat))
        {
            arguments.Add("--output");
            arguments.Add(config.OutputFormat);
        }

        if (!string.IsNullOrWhiteSpace(config.Agent))
        {
            arguments.Add("--agent");
            arguments.Add(config.Agent);
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

            var artifacts = new List<string>();

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
                Artifacts: artifacts,
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
                Artifacts: Array.Empty<string>(),
                RunId: request.RunId
            );
        }
    }
}
