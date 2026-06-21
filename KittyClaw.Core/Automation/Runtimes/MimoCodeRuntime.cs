using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class MimoCodeRuntime : IAgentRuntime
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<MimoCodeRuntime>? _logger;

    public string Id => AgentRuntimeIds.MimoCode;

    public MimoCodeRuntime(ProcessRunner processRunner, ILogger<MimoCodeRuntime>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var config = request.RuntimeConfig;

        if (config.DangerouslySkipPermissions)
        {
            throw new InvalidOperationException(
                $"Runtime '{Id}' does not support dangerouslySkipPermissions. Set it to false in the runtime configuration.");
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
            var tempFile = Path.Combine(Path.GetTempPath(), $"kittyclaw-mimo-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempFile, request.Prompt, cancellationToken);
            arguments.Add(tempFile);
        }

        arguments.Add("--format");
        arguments.Add(config.OutputFormat ?? "json");
        arguments.Add("--title");
        arguments.Add(title);

        if (!string.IsNullOrWhiteSpace(config.Model))
        {
            arguments.Add("--model");
            arguments.Add(config.Model);
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
            if (!string.IsNullOrWhiteSpace(result.Stdout))
            {
                // Try to parse JSON artifacts if the output format is json
                if (config.OutputFormat == "json")
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(result.Stdout);
                        if (doc.RootElement.TryGetProperty("artifacts", out var artEl) && artEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in artEl.EnumerateArray())
                            {
                                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                                    artifacts.Add(item.GetString() ?? "");
                            }
                        }
                    }
                    catch { /* best-effort artifact extraction */ }
                }
            }

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
                Artifacts: Array.Empty<string>()
            );
        }
    }
}
