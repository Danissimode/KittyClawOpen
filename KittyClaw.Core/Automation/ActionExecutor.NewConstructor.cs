using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Automation.Runtimes;
using KittyClaw.Core.Automation.Triggers;
using KittyClaw.Core.Integrations.OpenCode;
using KittyClaw.Core.Services;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Evaluates automation conditions and executes action sequences.
/// Owns the git semaphore and all Execute*ActionAsync helpers.
/// </summary>
internal sealed partial class ActionExecutor
{
    // New constructor with runner services
    public ActionExecutor(
        TicketService tickets,
        MemberService members,
        LabelService labels,
        SessionRegistry sessions,
        AgentRunRegistry runs,
        IEnumerable<IAgentRuntime> runtimes,
        IAgentPromptBuilder promptBuilder,
        AgentRuntimeConfigLoader configLoader,
        CostTracker cost,
        LocalizationService loc,
        ProjectService projects,
        RunStateManager runState,
        ILogger logger,
        RunnerRegistry? runnerRegistry = null,
        ITicketExecutionMetadataStore? metadataStore = null,
        IExecutionPolicyService? policyService = null,
        IWorktreeService? worktreeService = null)
    {
        _tickets = tickets;
        _members = members;
        _labels = labels;
        _sessions = sessions;
        _runs = runs;
        _runtimes = runtimes;
        _promptBuilder = promptBuilder;
        _configLoader = configLoader;
        _cost = cost;
        _loc = loc;
        _projects = projects;
        _runState = runState;
        _logger = logger;
        
        // Initialize runner services
        InitializeRunnerServices(runnerRegistry, metadataStore, policyService, worktreeService);
    }
}
