using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;

namespace KittyClaw.Core.Integrations.OpenCode;

/// <summary>
/// OpenCode runner implementation with full AgentRunRegistry integration.
/// This runner registers all runs with the global registry, enabling:
/// - Real-time event streaming to UI
/// - Process tracking for StopAsync
/// - Steering support via temp files (CLI mode)
/// - Consistent UX with ClaudeRunner
/// </summary>
public sealed class OpenCodeRunner : IAgentRunner
{
    private readonly OpenCodeConfig _config;
    private readonly AgentRunRegistry _runRegistry;
    private readonly ILogger<OpenCodeRunner>? _logger;
    private readonly IProviderModelCatalog? _modelCatalog;
    
    // Process tracking for StopAsync support
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly SemaphoreSlim _processLock = new(1, 1);
    
    public string Kind => "opencode";
    public string DisplayName => "OpenCode";
    public bool IsAvailable => CheckAvailability();
    
    public OpenCodeRunner(
        OpenCodeConfig config,
        AgentRunRegistry runRegistry,
        ILogger<OpenCodeRunner>? logger = null,
        IProviderModelCatalog? modelCatalog = null)
    {
        _config = config;
        _runRegistry = runRegistry;
        _logger = logger;
        _modelCatalog = modelCatalog;
    }
    
    private bool CheckAvailability()
    {
        // Check if OpenCode CLI is available
        if (_config.UseServer && !string.IsNullOrEmpty(_config.ServerUrl))
        {
            return true; // Server mode - assume available if URL configured
        }
        
        // Check if CLI is available
        if (!string.IsNullOrEmpty(_config.CliCommand))
        {
            return File.Exists(_config.CliCommand) || TryFindInPath(_config.CliCommand);
        }
        
        // Try to find opencode in PATH
        return TryFindInPath("opencode") || TryFindInPath("oc");
    }
    
    /// <summary>
    /// Deep check with version, provider, and model extraction for health reporting
    /// </summary>
    public async Task<(bool Available, string? Version, string? Provider, string? Model)> DeepCheckAsync()
    {
        try
        {
            var cmd = ResolveCliCommand();
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, null, _config.DefaultProvider, _config.DefaultModel);
            
            var completed = proc.WaitForExit(5000); // 5 second timeout
            var version = await proc.StandardOutput.ReadToEndAsync();
            
            // Extract version, provider, and model from config
            return (completed && proc.ExitCode == 0, version.Trim(), _config.DefaultProvider, _config.DefaultModel);
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "DeepCheckAsync failed"); return (false, null, _config.DefaultProvider, _config.DefaultModel); }
    }
    
    private static bool TryFindInPath(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(processInfo);
            return process is not null;
        }
        catch (Exception) { /* TryFindInPath failed — command not found or not executable */ return false; }
    }
    
    public async Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var runId = request.RunId ?? Guid.NewGuid().ToString("N");
        var startedAt = DateTime.UtcNow;
        
        // Create AgentRun entity and register it (same pattern as ClaudeRunner)
        var agentRun = new AgentRun
        {
            RunId = runId,
            ProjectSlug = request.ProjectSlug,
            TicketId = request.TicketId,
            AgentName = request.AgentName,
            SkillFile = request.SkillFile,
            ConcurrencyGroup = request.ConcurrencyGroup ?? $"ticket-{request.TicketId}",
            StartedAt = startedAt,
            Model = request.Model,
            ChatTarget = request.ExecutionMetadata?.OpenCodeAgent,
            RunnerKind = Kind // Track which runner this is
        };
        
        // Wire up event hook before registration so no events are missed
        if (request.OnEventHook is not null)
            agentRun.OnEvent += request.OnEventHook;
        
        _runRegistry.Register(agentRun);
        
        try
        {
            // Validate OpenCode is available
            if (!IsAvailable)
            {
                throw new InvalidOperationException("OpenCode is not available. Please install OpenCode CLI or configure server.");
            }
            
            // Resolve provider and model
            var provider = request.Provider ?? _config.DefaultProvider ?? "openrouter";
            var model = request.Model ?? _config.DefaultModel ?? "anthropic/claude-3-5-sonnet-20241022";
            var opencodeAgent = request.ExecutionMetadata?.OpenCodeAgent ?? _config.DefaultAgent ?? "build";
            
            // Build execution metadata
            var executionMetadata = new ExecutionMetadata
            {
                Mode = request.ExecutionMode.ToString(),
                Runner = Kind,
                Provider = provider,
                Model = model,
                Profile = request.Profile ?? "developer",
                RunId = runId,
                WorktreePath = request.WorktreePath,
                BranchName = request.BranchName,
                TicketId = request.TicketId?.ToString(),
                ProjectId = request.ProjectSlug,
                OpenCodeAgent = opencodeAgent,
                SteerSupported = true // Now supported via temp file
            };
            
            // Attach metadata to the run so it persists with the run log
            agentRun.ExecutionMetadata = executionMetadata;
            
            // Determine execution path
            if (_config.UseServer && !string.IsNullOrEmpty(_config.ServerUrl))
            {
                return await ExecuteViaServerAsync(request, agentRun, executionMetadata, cancellationToken);
            }
            else
            {
                return await ExecuteViaCliAsync(request, agentRun, executionMetadata, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCode runner execution failed for run {RunId}", runId);
            agentRun.Push(new StreamEvent(DateTime.UtcNow, "error", ex.Message));
            _runRegistry.Complete(runId, AgentRunStatus.Failed, 1);
            
            var finishedAt = DateTimeOffset.UtcNow;
            return new AgentRunResult
            {
                Status = AgentRunStatus.Failed,
                ExitCode = 1,
                Stdout = string.Empty,
                Stderr = ex.Message,
                StartedAt = new DateTimeOffset(startedAt),
                FinishedAt = finishedAt,
                Duration = finishedAt - new DateTimeOffset(startedAt),
                RunnerKind = Kind,
                RunId = runId,
                ExecutionMetadata = new ExecutionMetadata
                {
                    Mode = request.ExecutionMode.ToString(),
                    Runner = Kind,
                    RunId = runId,
                    LastError = ex.Message,
                    SteerSupported = false
                }
            };
        }
    }
    
    private async Task<AgentRunResult> ExecuteViaServerAsync(
        AgentRunRequest request,
        AgentRun agentRun,
        ExecutionMetadata executionMetadata,
        CancellationToken cancellationToken)
    {
        var serverUrl = _config.ServerUrl!.TrimEnd('/');

        // Write structured context file alongside the prompt file for OpenCode to consume
        var workingDir = request.WorktreePath ?? request.WorkspacePath;
        Directory.CreateDirectory(workingDir);
        var contextFile = WriteContextFile(workingDir, agentRun.RunId, request, executionMetadata);

        // Start the run via POST /api/runs
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        
        // Prepend pending steer messages to prompt for server mode
        var effectivePrompt = request.Prompt ?? "";
        if (request.PendingSteerMessages?.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var steer in request.PendingSteerMessages)
            {
                sb.AppendLine($"[Steering message from previous turn]: {steer}");
            }
            sb.AppendLine();
            sb.Append(effectivePrompt);
            effectivePrompt = sb.ToString();
        }
        
        var startPayload = new
        {
            runId = agentRun.RunId,
            provider = executionMetadata.Provider,
            model = executionMetadata.Model,
            agent = executionMetadata.OpenCodeAgent,
            profile = executionMetadata.Profile,
            workspace = workingDir,
            maxTurns = request.MaxTurns,
            prompt = effectivePrompt,
            contextFile = contextFile,
            metadata = new
            {
                projectSlug = request.ProjectSlug,
                ticketId = request.TicketId,
                branchName = executionMetadata.BranchName,
                worktreePath = executionMetadata.WorktreePath
            }
        };

        HttpResponseMessage startResp;
        try
        {
            startResp = await httpClient.PostAsJsonAsync($"{serverUrl}/api/runs", startPayload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "OpenCode server unreachable at {Url}, falling back to CLI", serverUrl);
            CleanupContextFile(contextFile);
            return await ExecuteViaCliAsync(request, agentRun, executionMetadata, cancellationToken);
        }

        if (!startResp.IsSuccessStatusCode)
        {
            var errBody = await startResp.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogWarning("OpenCode server returned {Status} {Body}, falling back to CLI", startResp.StatusCode, errBody);
            CleanupContextFile(contextFile);
            return await ExecuteViaCliAsync(request, agentRun, executionMetadata, cancellationToken);
        }

        JsonElement startBody;
        try
        {
            startBody = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync(cancellationToken)).RootElement;
        }
        catch
        {
            CleanupContextFile(contextFile);
            return await ExecuteViaCliAsync(request, agentRun, executionMetadata, cancellationToken);
        }

        var sessionId = startBody.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        var opencodeRunId = startBody.TryGetProperty("runId", out var rid) ? rid.GetString() ?? agentRun.RunId : agentRun.RunId;

        executionMetadata.SessionId = sessionId;

        // Stream events via GET /api/runs/{runId}/stream (SSE)
        var streamUrl = $"{serverUrl}/api/runs/{opencodeRunId}/stream";
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, agentRun.Cancellation.Token);

        try
        {
            await StreamServerEventsAsync(httpClient, streamUrl, agentRun, stdoutBuilder, stderrBuilder, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our internal timeout or stop — the server-side run should already be stopped
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SSE stream error for run {RunId}", agentRun.RunId);
        }

        // Determine final status
        var status = agentRun.Status;
        var exitCode = status == AgentRunStatus.Completed ? 0 : 1;
        var finishedAt = DateTimeOffset.UtcNow;

        _runRegistry.Complete(agentRun.RunId, status, exitCode);
        CleanupContextFile(contextFile);

        return new AgentRunResult
        {
            Status = status,
            ExitCode = exitCode,
            Stdout = stdoutBuilder.ToString(),
            Stderr = stderrBuilder.ToString(),
            StartedAt = new DateTimeOffset(agentRun.StartedAt),
            FinishedAt = finishedAt,
            Duration = finishedAt - new DateTimeOffset(agentRun.StartedAt),
            RunnerKind = Kind,
            RunId = agentRun.RunId,
            ExecutionMetadata = executionMetadata
        };
    }

    /// <summary>
    /// Writes a structured context file that OpenCode can read to understand the run context.
    /// </summary>
    private string? WriteContextFile(string workingDir, string runId, AgentRunRequest request, ExecutionMetadata em)
    {
        try
        {
            var agentsDir = Path.Combine(workingDir, ".agents", "tmp");
            Directory.CreateDirectory(agentsDir);
            var path = Path.Combine(agentsDir, $"context-{runId}.md");

            var sb = new StringBuilder();
            sb.AppendLine("# Run Context");
            sb.AppendLine();
            sb.AppendLine($"- **Project**: {request.ProjectSlug}");
            if (request.TicketId is int tid)
                sb.AppendLine($"- **Ticket ID**: #{tid}");
            sb.AppendLine($"- **Run ID**: `{runId}`");
            sb.AppendLine($"- **Agent**: {em.OpenCodeAgent}");
            sb.AppendLine($"- **Provider**: {em.Provider}");
            sb.AppendLine($"- **Model**: {em.Model}");
            sb.AppendLine($"- **Profile**: {em.Profile}");
            sb.AppendLine($"- **Branch**: `{em.BranchName ?? "(default)"}`");
            if (!string.IsNullOrEmpty(em.WorktreePath))
                sb.AppendLine($"- **Worktree**: `{em.WorktreePath}`");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(request.Prompt))
            {
                sb.AppendLine("## Prompt");
                sb.AppendLine();
                sb.AppendLine(request.Prompt);
                sb.AppendLine();
            }

            // Include skill file path if present
            if (!string.IsNullOrEmpty(request.SkillFile))
            {
                sb.AppendLine("## Skill");
                sb.AppendLine();
                sb.AppendLine($"Skill file: `{request.SkillFile}`");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write context file for run {RunId}", runId);
            return null;
        }
    }

    private static void CleanupContextFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); } catch (Exception) { /* best-effort context file cleanup */ }
    }

    /// <summary>
    /// Reads SSE stream from OpenCode server and pushes events to AgentRun.
    /// Falls through on network errors (run is considered complete).
    /// </summary>
    private async Task StreamServerEventsAsync(
        HttpClient httpClient,
        string streamUrl,
        AgentRun agentRun,
        StringBuilder stdout,
        StringBuilder stderr,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, streamUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return;

        using var body = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            // SSE format: "event: kind"
            if (!line.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) continue;

            var kind = line["event:".Length..].Trim();

            // Read the data lines that follow
            string? data = null;
            while (true)
            {
                var nextLine = await reader.ReadLineAsync(ct);
                if (nextLine is null) break; // EOF
                if (nextLine == "") break; // blank line separates events
                if (nextLine.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                {
                    // Next event started — re-read it in the outer loop by not consuming
                    break;
                }
                if (nextLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    data = nextLine["data:".Length..].Trim();
                }
            }

            if (string.IsNullOrEmpty(data)) continue;

            var ev = ParseServerEvent(kind, data);
            if (ev is not null)
            {
                agentRun.Push(ev);

                // Accumulate stdout/stderr
                if (ev.Kind == "stdout")
                    stdout.AppendLine(ev.Text);
                else if (ev.Kind == "stderr")
                    stderr.AppendLine(ev.Text);

                // Mark run as complete on terminal events
                if (ev.Kind is "completed" or "error" or "stopped")
                {
                    if (ev.Kind == "error")
                        _runRegistry.Complete(agentRun.RunId, AgentRunStatus.Failed, 1);
                    else if (ev.Kind == "stopped")
                        _runRegistry.Complete(agentRun.RunId, AgentRunStatus.Stopped, null);
                    else
                        _runRegistry.Complete(agentRun.RunId, AgentRunStatus.Completed, 0);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Parse an SSE data payload into a StreamEvent. Returns null for unrecognised events.
    /// </summary>
    private static StreamEvent? ParseServerEvent(string kind, string data)
    {
        try
        {
            return kind.ToLowerInvariant() switch
            {
                "assistant" or "message" => new StreamEvent(DateTime.UtcNow, "assistant", data),
                "tool_use" => ExtractToolUse(data),
                "tool_result" => new StreamEvent(DateTime.UtcNow, "tool_result", data),
                "stdout" => new StreamEvent(DateTime.UtcNow, "stdout", data),
                "stderr" => new StreamEvent(DateTime.UtcNow, "stderr", data),
                "error" => new StreamEvent(DateTime.UtcNow, "error", data),
                "max_turns" => new StreamEvent(DateTime.UtcNow, "max_turns", data),
                "ask_user_question" => new StreamEvent(DateTime.UtcNow, "ask_user_question", data),
                "completed" => new StreamEvent(DateTime.UtcNow, "completed", data),
                "stopped" => new StreamEvent(DateTime.UtcNow, "stopped", data),
                "info" or "debug" => null, // noise — skip
                _ => new StreamEvent(DateTime.UtcNow, kind, data)
            };
        }
        catch
        {
            return new StreamEvent(DateTime.UtcNow, kind, data);
        }
    }

    private static StreamEvent ExtractToolUse(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "tool";
            return new StreamEvent(DateTime.UtcNow, "tool_use", name, data);
        }
        catch
        {
            return new StreamEvent(DateTime.UtcNow, "tool_use", "tool", data);
        }
    }

    // Extended StreamEvent that carries an optional Detail string
    private static StreamEvent CreateStreamEvent(string kind, string text, string? detail = null) =>
        new(DateTime.UtcNow, kind, text, detail ?? text);
    
    private async Task<AgentRunResult> ExecuteViaCliAsync(
        AgentRunRequest request, 
        AgentRun agentRun,
        ExecutionMetadata executionMetadata,
        CancellationToken cancellationToken)
    {
        var cliCommand = ResolveCliCommand();
        var workingDir = request.WorktreePath ?? request.WorkspacePath;
        Directory.CreateDirectory(workingDir);
        
        // Link external cancellation with run's internal cancellation
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, agentRun.Cancellation.Token);
        
        // Build CLI arguments using template
        var arguments = BuildCliArguments(request, executionMetadata, out var promptFile);
        var commandDisplay = $"{cliCommand} {string.Join(" ", arguments.Select(a => $"\"{a}\""))}";
        agentRun.CommandDisplay = commandDisplay;
        
        var processInfo = new ProcessStartInfo
        {
            FileName = cliCommand,
            Arguments = string.Join(" ", arguments),
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        // Copy environment variables
        foreach (var env in request.Environment)
        {
            processInfo.Environment[env.Key] = env.Value;
        }
        
        // Add OpenCode-specific env vars
        processInfo.Environment["OPENCODE_PROJECT"] = request.ProjectSlug;
        processInfo.Environment["OPENCODE_TICKET_ID"] = request.TicketId?.ToString() ?? "";
        processInfo.Environment["OPENCODE_RUN_ID"] = agentRun.RunId;
        if (request.ExecutionMetadata?.OpenCodeAgent is string agent)
            processInfo.Environment["OPENCODE_AGENT"] = agent;
        
        using var process = Process.Start(processInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start OpenCode process.");
        }
        
        // Track process for StopAsync
        _processes[agentRun.RunId] = process;
        
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        
        // Stream events to AgentRun
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stdoutBuilder.AppendLine(e.Data);
                var ev = new StreamEvent(DateTime.UtcNow, "stdout", e.Data);
                agentRun.Push(ev);
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderrBuilder.AppendLine(e.Data);
                var ev = new StreamEvent(DateTime.UtcNow, "stderr", e.Data);
                agentRun.Push(ev);
            }
        };
        
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        // Start steering file watcher task (linked cancellation)
        var steerTask = HandleSteeringAsync(agentRun, workingDir, linkedCts.Token);
        
        // Pre-seed steer file with any pending steer messages from previous runs
        if (request.PendingSteerMessages?.Count > 0)
        {
            foreach (var steer in request.PendingSteerMessages)
            {
                await agentRun.SteeringQueue.Writer.WriteAsync(steer, linkedCts.Token);
            }
        }
        
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // User cancelled - stop the process
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            await steerTask;
            _processes.TryRemove(agentRun.RunId, out _);
            _runRegistry.Complete(agentRun.RunId, AgentRunStatus.Stopped, null);
            CleanupPromptFile(promptFile);
            
            return new AgentRunResult
            {
                Status = AgentRunStatus.Stopped,
                ExitCode = null,
                StartedAt = new DateTimeOffset(agentRun.StartedAt),
                FinishedAt = DateTimeOffset.UtcNow,
                Duration = DateTimeOffset.UtcNow - new DateTimeOffset(agentRun.StartedAt),
                RunnerKind = Kind,
                RunId = agentRun.RunId,
                CommandDisplay = commandDisplay,
                ExecutionMetadata = executionMetadata
            };
        }
        
        await steerTask;
        CleanupPromptFile(promptFile);
        
        var finishedAt = DateTimeOffset.UtcNow;
        var exitCode = process.ExitCode;
        
        var status = exitCode == 0 ? AgentRunStatus.Completed : AgentRunStatus.Failed;
        
        // Complete the run in registry
        _runRegistry.Complete(agentRun.RunId, status, exitCode);
        _processes.TryRemove(agentRun.RunId, out _);
        
        // Update execution metadata
        executionMetadata.SessionId = null; // CLI mode doesn't have session ID
        executionMetadata.SteerSupported = true; // Now supported via temp file
        
        return new AgentRunResult
        {
            Status = status,
            ExitCode = exitCode,
            Stdout = stdoutBuilder.ToString(),
            Stderr = stderrBuilder.ToString(),
            StartedAt = new DateTimeOffset(agentRun.StartedAt),
            FinishedAt = finishedAt,
            Duration = finishedAt - new DateTimeOffset(agentRun.StartedAt),
            RunnerKind = Kind,
            RunId = agentRun.RunId,
            CommandDisplay = commandDisplay,
            ExecutionMetadata = executionMetadata
        };
    }
    
    /// <summary>
    /// Handle steering via temp file. OpenCode can poll this file for messages.
    /// </summary>
    private async Task HandleSteeringAsync(AgentRun agentRun, string workingDir, CancellationToken ct)
    {
        var steerDir = Path.Combine(workingDir, ".agents", "tmp");
        Directory.CreateDirectory(steerDir);
        var steerFile = Path.Combine(steerDir, $"steer-{agentRun.RunId}.txt");
        
        try
        {
            while (!ct.IsCancellationRequested && agentRun.Status == AgentRunStatus.Running)
            {
                // Drain steering queue and write to file
                while (agentRun.SteeringQueue.Reader.TryRead(out var msg))
                {
                    await File.WriteAllTextAsync(steerFile, msg, ct);
                    agentRun.Push(new StreamEvent(DateTime.UtcNow, "steer-sent", $"Steering message sent: {msg}"));
                }
                
                await Task.Delay(500, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Steering file watcher error for run {RunId}", agentRun.RunId);
        }
        finally
        {
            // Cleanup steering file
            try { File.Delete(steerFile); } catch (Exception ex) { _logger?.LogDebug(ex, "Steering file cleanup failed for {SteerFile}", steerFile); }
        }
    }
    
    private string ResolveCliCommand()
    {
        if (!string.IsNullOrEmpty(_config.CliCommand) && 
            (File.Exists(_config.CliCommand) || TryFindInPath(_config.CliCommand)))
        {
            return _config.CliCommand;
        }
        
        // Try to find in PATH
        if (TryFindInPath("opencode")) return "opencode";
        if (TryFindInPath("oc")) return "oc";
        
        return _config.CliCommand ?? "opencode";
    }
    
    private List<string> BuildCliArguments(AgentRunRequest request, ExecutionMetadata executionMetadata, out string? promptFile)
    {
        promptFile = null;
        var arguments = new List<string>();
        
        // Use command template if configured
        if (_config.CommandTemplate is not null)
        {
            return ApplyCommandTemplate(request, executionMetadata);
        }
        
        // Default CLI arguments
        if (!string.IsNullOrEmpty(executionMetadata.Model))
        {
            arguments.Add("--model");
            arguments.Add(executionMetadata.Model);
        }
        
        if (!string.IsNullOrEmpty(executionMetadata.OpenCodeAgent))
        {
            arguments.Add("--agent");
            arguments.Add(executionMetadata.OpenCodeAgent);
        }
        
        if (!string.IsNullOrEmpty(executionMetadata.Profile))
        {
            arguments.Add("--profile");
            arguments.Add(executionMetadata.Profile);
        }
        
        // Add max turns
        arguments.Add("--max-turns");
        arguments.Add(request.MaxTurns.ToString());
        
        // Add working directory if different from workspace
        if (!string.IsNullOrEmpty(request.WorktreePath) && 
            request.WorktreePath != request.WorkspacePath)
        {
            arguments.Add("--working-directory");
            arguments.Add(request.WorktreePath);
        }
        
        // Write prompt to a temp file and pass --prompt-file.
        // This avoids shell-escaping issues with long prompts and special characters.
        // Pending steer messages from a previous run are prepended so the agent receives them.
        if (!string.IsNullOrEmpty(request.Prompt) || request.PendingSteerMessages?.Count > 0)
        {
            var workingDir = request.WorktreePath ?? request.WorkspacePath;
            var tmpDir = Path.Combine(workingDir, ".agents", "tmp");
            Directory.CreateDirectory(tmpDir);
            promptFile = Path.Combine(tmpDir, $"prompt-{Guid.NewGuid():N}.txt");
            
            var promptBuilder = new StringBuilder();
            if (request.PendingSteerMessages?.Count > 0)
            {
                foreach (var steer in request.PendingSteerMessages)
                {
                    promptBuilder.AppendLine($"[Steering message from previous turn]: {steer}");
                }
                promptBuilder.AppendLine();
            }
            promptBuilder.Append(request.Prompt);
            
            File.WriteAllText(promptFile, promptBuilder.ToString());
            arguments.Add("--prompt-file");
            arguments.Add(promptFile);
        }
        
        return arguments;
    }
    
    private List<string> ApplyCommandTemplate(AgentRunRequest request, ExecutionMetadata executionMetadata)
    {
        var arguments = new List<string>();
        
        if (_config.CommandTemplate is null) return arguments;
        
        foreach (var argTemplate in _config.CommandTemplate)
        {
            var arg = argTemplate
                .Replace("{model}", executionMetadata.Model ?? "")
                .Replace("{agent}", executionMetadata.OpenCodeAgent ?? "")
                .Replace("{profile}", executionMetadata.Profile ?? "")
                .Replace("{maxTurns}", request.MaxTurns.ToString())
                .Replace("{worktreePath}", request.WorktreePath ?? "")
                .Replace("{workspacePath}", request.WorkspacePath)
                .Replace("{prompt}", request.Prompt ?? "");
            
            if (!string.IsNullOrEmpty(arg))
            {
                arguments.Add(arg);
            }
        }
        
        return arguments;
    }
    
    public async Task<bool> StopAsync(string runId, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stop requested for OpenCode run {RunId}", runId);
        
        // Try to get the process
        if (_processes.TryGetValue(runId, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    _logger?.LogInformation("Killed OpenCode process for run {RunId}", runId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to kill process for run {RunId}", runId);
            }
        }
        
        // Also check AgentRunRegistry for running status
        var run = _runRegistry.Get(runId);
        if (run is not null && run.Status == AgentRunStatus.Running)
        {
            run.Cancellation.Cancel();
            _runRegistry.Complete(runId, AgentRunStatus.Stopped, null);
            return true;
        }
        
        return false;
    }
    
    public async Task<bool> SteerAsync(string runId, string message, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Steer requested for OpenCode run {RunId}: {Message}", runId, message);
        
        var run = _runRegistry.Get(runId);
        if (run is null)
        {
            _logger?.LogWarning("Run {RunId} not found for steering", runId);
            return false;
        }
        
        // Queue the message for the steering file watcher
        await run.SteeringQueue.Writer.WriteAsync(message, cancellationToken);
        return true;
    }
    
    public async Task<AgentRunStatus> GetStatusAsync(string runId, CancellationToken cancellationToken)
    {
        var run = _runRegistry.Get(runId);
        if (run is not null)
        {
            return run.Status;
        }
        
        // Fallback to process check
        if (_processes.TryGetValue(runId, out var process))
        {
            return process.HasExited ? AgentRunStatus.Completed : AgentRunStatus.Running;
        }
        
        // Default to Running if we don't know
        return AgentRunStatus.Running;
    }
    
    private static void CleanupPromptFile(string? promptFile)
    {
        if (string.IsNullOrEmpty(promptFile)) return;
        try { File.Delete(promptFile); } catch (Exception) { /* best-effort prompt file cleanup */ }
    }
}

/// <summary>
/// Configuration for OpenCode runner
/// </summary>
public sealed class OpenCodeConfig
{
    /// <summary>
    /// Whether to use OpenCode server mode (true) or CLI mode (false)
    /// </summary>
    public bool UseServer { get; set; } = false;
    
    /// <summary>
    /// OpenCode server URL (used when UseServer = true)
    /// </summary>
    public string? ServerUrl { get; set; }
    
    /// <summary>
    /// OpenCode CLI command (e.g., "opencode", "oc", "/path/to/opencode")
    /// </summary>
    public string? CliCommand { get; set; }
    
    /// <summary>
    /// Default provider to use
    /// </summary>
    public string? DefaultProvider { get; set; } = "openrouter";
    
    /// <summary>
    /// Default model to use
    /// </summary>
    public string? DefaultModel { get; set; } = "openrouter/anthropic/claude-3-5-sonnet";
    
    /// <summary>
    /// Default OpenCode agent to use
    /// </summary>
    public string? DefaultAgent { get; set; } = "build";
    
    /// <summary>
    /// Command template for CLI arguments
    /// Use placeholders: {model}, {agent}, {profile}, {maxTurns}, {worktreePath}, {workspacePath}, {prompt}
    /// </summary>
    public List<string>? CommandTemplate { get; set; }
    
    /// <summary>
    /// Timeout in seconds for OpenCode execution
    /// </summary>
    public int TimeoutSeconds { get; set; } = 3600;
}
