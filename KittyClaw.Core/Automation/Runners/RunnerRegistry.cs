using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runners;

/// <summary>
/// Registry for managing agent runners.
/// This is a stable extension point that allows adding new runners without modifying core logic.
/// </summary>
public class RunnerRegistry
{
    private readonly Dictionary<string, IAgentRunner> _runners = new();
    private readonly ILogger<RunnerRegistry>? _logger;
    private IAgentRunner? _defaultRunner;
    
    public RunnerRegistry(ILogger<RunnerRegistry>? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Register a runner with the registry
    /// </summary>
    public void RegisterRunner(IAgentRunner runner)
    {
        _runners[runner.Kind] = runner;
        _logger?.LogInformation("Registered runner: {RunnerKind} - {DisplayName}", runner.Kind, runner.DisplayName);
        
        // Set as default if it's the first runner or if it's explicitly marked as default
        if (_defaultRunner is null)
        {
            _defaultRunner = runner;
        }
    }
    
    /// <summary>
    /// Set the default runner explicitly
    /// </summary>
    public void SetDefaultRunner(string runnerKind)
    {
        if (_runners.TryGetValue(runnerKind, out var runner))
        {
            _defaultRunner = runner;
            _logger?.LogInformation("Set default runner: {RunnerKind}", runnerKind);
        }
        else
        {
            _logger?.LogWarning("Cannot set default runner: {RunnerKind} not found", runnerKind);
        }
    }
    
    /// <summary>
    /// Get a runner by kind
    /// </summary>
    public IAgentRunner? GetRunner(string runnerKind)
    {
        _runners.TryGetValue(runnerKind, out var runner);
        return runner;
    }
    
    /// <summary>
    /// Get the default runner
    /// </summary>
    public IAgentRunner GetDefaultRunner()
    {
        if (_defaultRunner is null)
        {
            throw new InvalidOperationException("No default runner registered. Please register at least one runner.");
        }
        return _defaultRunner;
    }
    
    /// <summary>
    /// Get all registered runners
    /// </summary>
    public IEnumerable<IAgentRunner> GetAllRunners() => _runners.Values.ToList();
    
    /// <summary>
    /// Get available runners (those that are available and properly configured)
    /// </summary>
    public IEnumerable<IAgentRunner> GetAvailableRunners() => 
        _runners.Values.Where(r => r.IsAvailable).ToList();
    
    /// <summary>
    /// Resolve the appropriate runner for an execution mode.
    /// Honors IsAvailable: if the mode-mapped runner is registered but not currently
    /// available (e.g. OpenCode server down), fall back to the default runner.
    /// </summary>
    public virtual IAgentRunner ResolveRunner(ExecutionMode executionMode)
    {
        return executionMode switch
        {
            ExecutionMode.LegacyClaude => GetAvailable("claude"),
            ExecutionMode.DirectOpenCode => GetAvailable("opencode"),
            ExecutionMode.CaoGoverned => GetAvailable("cao"),
            ExecutionMode.TeamWorkflow => GetAvailable("team"),
            ExecutionMode.Manual => GetAvailable("manual"),
            _ => GetDefaultRunner()
        };
    }

    private IAgentRunner GetAvailable(string kind)
    {
        var runner = GetRunner(kind);
        if (runner is not null && runner.IsAvailable) return runner;
        return GetDefaultRunner();
    }
    
    /// <summary>
    /// Resolve runner by explicit kind or execution mode
    /// </summary>
    public IAgentRunner ResolveRunner(string? runnerKind, ExecutionMode executionMode)
    {
        if (!string.IsNullOrEmpty(runnerKind))
        {
            var runner = GetRunner(runnerKind);
            if (runner is not null && runner.IsAvailable)
            {
                return runner;
            }
        }
        
        return ResolveRunner(executionMode);
    }
}
