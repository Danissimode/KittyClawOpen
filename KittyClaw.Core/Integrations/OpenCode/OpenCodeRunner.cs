using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;

namespace KittyClaw.Core.Integrations.OpenCode;

/// <summary>
/// OpenCode runner implementation.
/// This is an isolated OpenCode-specific runner that implements the generic IAgentRunner interface.
/// </summary>
public sealed class OpenCodeRunner : IAgentRunner
{
    private readonly OpenCodeConfig _config;
    private readonly ILogger<OpenCodeRunner>? _logger;
    private readonly IProviderModelCatalog? _modelCatalog;
    
    public string Kind => "opencode";
    public string DisplayName => "OpenCode";
    public bool IsAvailable => CheckAvailability();
    
    public OpenCodeRunner(
        OpenCodeConfig config,
        ILogger<OpenCodeRunner>? logger = null,
        IProviderModelCatalog? modelCatalog = null)
    {
        _config = config;
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
        catch { return false; }
    }
    
    public async Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var runId = request.RunId ?? Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        
        try
        {
            // Validate OpenCode is available
            if (!IsAvailable)
            {
                throw new InvalidOperationException("OpenCode is not available. Please install OpenCode CLI or configure server.");
            }
            
            // Resolve provider and model
            var provider = request.Provider ?? _config.DefaultProvider ?? "openrouter";
            var model = request.Model ?? _config.DefaultModel ?? "deepseek-v4-pro";
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
                SteerSupported = _config.UseServer // Steering only supported in server mode
            };
            
            // Determine execution path
            if (_config.UseServer && !string.IsNullOrEmpty(_config.ServerUrl))
            {
                return await ExecuteViaServerAsync(request, runId, startedAt, executionMetadata, cancellationToken);
            }
            else
            {
                return await ExecuteViaCliAsync(request, runId, startedAt, executionMetadata, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCode runner execution failed for run {RunId}", runId);
            var finishedAt = DateTimeOffset.UtcNow;
            return new AgentRunResult
            {
                Status = AgentRunStatus.Failed,
                ExitCode = 1,
                Stdout = string.Empty,
                Stderr = ex.Message,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Duration = finishedAt - startedAt,
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
        string runId, 
        DateTimeOffset startedAt, 
        ExecutionMetadata executionMetadata,
        CancellationToken cancellationToken)
    {
        // TODO: Implement OpenCode server API integration
        // For now, fallback to CLI
        _logger?.LogWarning("OpenCode server mode not yet implemented, falling back to CLI");
        return await ExecuteViaCliAsync(request, runId, startedAt, executionMetadata, cancellationToken);
    }
    
    private async Task<AgentRunResult> ExecuteViaCliAsync(
        AgentRunRequest request, 
        string runId, 
        DateTimeOffset startedAt, 
        ExecutionMetadata executionMetadata,
        CancellationToken cancellationToken)
    {
        var cliCommand = _config.CliCommand ?? (TryFindInPath("opencode") ? "opencode" : "oc");
        var workingDir = request.WorktreePath ?? request.WorkspacePath;
        
        // Build CLI arguments using template
        var arguments = BuildCliArguments(request, executionMetadata);
        var commandDisplay = $"{cliCommand} {string.Join(" ", arguments.Select(a => $"\"{a}\""))}";
        
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
        
        using var process = Process.Start(processInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start OpenCode process.");
        }
        
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stdoutBuilder.AppendLine(e.Data);
                request.OnEventHook?.Invoke(new StreamEvent(DateTimeOffset.UtcNow.UtcDateTime, "stdout", e.Data));
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderrBuilder.AppendLine(e.Data);
                request.OnEventHook?.Invoke(new StreamEvent(DateTimeOffset.UtcNow.UtcDateTime, "stderr", e.Data));
            }
        };
        
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync(cancellationToken);
        
        var finishedAt = DateTimeOffset.UtcNow;
        var exitCode = process.ExitCode;
        
        var status = exitCode == 0 ? AgentRunStatus.Completed : AgentRunStatus.Failed;
        
        // Update execution metadata
        executionMetadata.SessionId = null; // CLI mode doesn't have session ID
        executionMetadata.SteerSupported = false;
        
        return new AgentRunResult
        {
            Status = status,
            ExitCode = exitCode,
            Stdout = stdoutBuilder.ToString(),
            Stderr = stderrBuilder.ToString(),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Duration = finishedAt - startedAt,
            RunnerKind = Kind,
            RunId = runId,
            CommandDisplay = commandDisplay,
            ExecutionMetadata = executionMetadata
        };
    }
    
    private List<string> BuildCliArguments(AgentRunRequest request, ExecutionMetadata executionMetadata)
    {
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
        
        // Add prompt from file or directly
        if (!string.IsNullOrEmpty(request.Prompt))
        {
            // For now, pass prompt directly (OpenCode CLI may need a file)
            arguments.Add("--prompt");
            arguments.Add(request.Prompt);
        }
        
        return arguments;
    }
    
    private List<string> ApplyCommandTemplate(AgentRunRequest request, ExecutionMetadata executionMetadata)
    {
        var arguments = new List<string>();
        
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
        
        // TODO: Implement stop for server mode
        // For CLI mode, we can't stop the process after it's started
        // This would require tracking the process ID
        return false;
    }
    
    public async Task<bool> SteerAsync(string runId, string message, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Steer requested for OpenCode run {RunId}: {Message}", runId, message);
        
        // Steering is only supported in server mode
        if (_config.UseServer)
        {
            // TODO: Implement steering via server API
            return false;
        }
        
        // CLI mode doesn't support steering
        return false;
    }
    
    public async Task<AgentRunStatus> GetStatusAsync(string runId, CancellationToken cancellationToken)
    {
        // TODO: Implement status check for server mode
        return AgentRunStatus.Running;
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
    public string? DefaultModel { get; set; } = "deepseek-v4-pro";
    
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
