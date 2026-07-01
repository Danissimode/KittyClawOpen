using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Integrations.OpenCode;
using KittyClaw.Core.Models;
using KittyClaw.Core.Services;
using RunnerRequest = KittyClaw.Core.Automation.Runners.AgentRunRequest;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Automatically starts agent runs when tickets move to InProgress.
///
/// Hooks into <see cref="TicketService.TicketStatusChanged"/> and evaluates each
/// transition. If the target status is "InProgress" and the ticket has a valid
/// execution configuration (DirectOpenCode / LegacyClaude / OpenCodeServer), this
/// service:
///
/// - Blocks duplicate runs (if an active run already exists)
/// - Enforces the plan gate (RequiresPlan → PlanStatus must be Approved)
/// - Checks execution policy
/// - Creates or reuses a per-ticket worktree
/// - Starts the selected runner via <see cref="RunnerRegistry"/>
/// - Persists execution metadata
/// - Records failures with clear comments on the ticket
/// - Handles run completion (InProgress → Review / Blocked / Done)
///
/// Stub modes (CaoGoverned, TeamWorkflow) are not auto-started; they are blocked
/// with a clear failure entry and the ticket is moved to Blocked/NeedsCAO.
///
/// Backward-compatible: tickets without an execution override still use the legacy
/// automation engine path.
/// </summary>
public sealed class TicketAutoRunService : IHostedService, IDisposable
{
    private readonly TicketService _tickets;
    private readonly ProjectService _projects;
    private readonly RunnerRegistry _runnerRegistry;
    private readonly AgentRunRegistry _runRegistry;
    private readonly ITicketExecutionMetadataStore? _metadataStore;
    private readonly IWorktreeService? _worktreeService;
    private readonly IExecutionPolicyService? _policyService;
    private readonly FailureLogStore _failures;
    private readonly AutoRunDeduplicationStore _dedupStore;
    private readonly ILogger<TicketAutoRunService>? _logger;

    private readonly Channel<(string ProjectSlug, int TicketId, string From, string To)> _queue =
        Channel.CreateUnbounded<(string, int, string, string)>();

    private readonly ConcurrentDictionary<string, DateTime> _recentDuplicates = new();
    private const int DuplicateSuppressionSeconds = 30;

    private Task? _loopTask;
    private CancellationTokenSource? _cts;

    public TicketAutoRunService(
        TicketService tickets,
        ProjectService projects,
        RunnerRegistry runnerRegistry,
        AgentRunRegistry runRegistry,
        ITicketExecutionMetadataStore? metadataStore,
        IWorktreeService? worktreeService,
        IExecutionPolicyService? policyService,
        FailureLogStore failures,
        AutoRunDeduplicationStore dedupStore,
        ILogger<TicketAutoRunService>? logger)
    {
        _tickets = tickets;
        _projects = projects;
        _runnerRegistry = runnerRegistry;
        _runRegistry = runRegistry;
        _metadataStore = metadataStore;
        _worktreeService = worktreeService;
        _policyService = policyService;
        _failures = failures;
        _dedupStore = dedupStore;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => ProcessLoop(_cts.Token), _cts.Token);
        _tickets.TicketStatusChanged += OnTicketStatusChanged;
        _logger?.LogInformation("TicketAutoRunService started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _tickets.TicketStatusChanged -= OnTicketStatusChanged;
        _cts?.Cancel();
        if (_loopTask is not null)
            await Task.WhenAny(_loopTask, Task.Delay(Timeout.Infinite, cancellationToken));
        _logger?.LogInformation("TicketAutoRunService stopped");
    }

    private void OnTicketStatusChanged(string projectSlug, int ticketId, string from, string to)
    {
        // Non-blocking: just enqueue, process in background loop
        _queue.Writer.TryWrite((projectSlug, ticketId, from, to));
    }

    private async Task ProcessLoop(CancellationToken ct)
    {
        await foreach (var (slug, ticketId, from, to) in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessTransitionAsync(slug, ticketId, from, to, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TicketAutoRunService: unhandled error processing ticket #{Id}", ticketId);
            }
        }
    }

    internal async Task ProcessTransitionAsync(
        string projectSlug, int ticketId, string from, string to, CancellationToken ct)
    {
        if (!string.Equals(to, "InProgress", StringComparison.OrdinalIgnoreCase))
            return;

        _logger?.LogInformation("TicketAutoRunService: processing transition #{Id} {From} → {To}",
            ticketId, from, to);

        // Generate trigger fingerprint for deduplication
        var triggerFingerprint = $"{projectSlug}:{ticketId}:status-change:{from}->{to}";

        // 0. Persistent idempotency guard: try to acquire lock
        var lockResult = await _dedupStore.TryAcquireLockAsync(
            projectSlug,
            ticketId,
            triggerType: "status-change",
            triggerFingerprint: triggerFingerprint,
            targetStatus: to,
            ttl: TimeSpan.FromMinutes(5),
            ct: ct);

        if (lockResult is not null)
        {
            // Lock already exists - duplicate detected
            _logger?.LogInformation("TicketAutoRunService: ticket #{Id} suppressed by persistent dedup lock {LockId}", 
                ticketId, lockResult.Id);
            return;
        }

        // 1. No-duplicate check with time-based suppression (in-memory fast path)
        if (_runRegistry.ActiveForTicket(projectSlug, ticketId).Any())
        {
            var dedupKey = $"{projectSlug}:{ticketId}";
            var now = DateTime.UtcNow;

            // Only log failure if we haven't logged one recently
            if (_recentDuplicates.TryGetValue(dedupKey, out var lastDuplicate)
                && (now - lastDuplicate).TotalSeconds < DuplicateSuppressionSeconds)
            {
                _logger?.LogDebug("TicketAutoRunService: ticket #{Id} duplicate suppressed", ticketId);
                return;
            }

            _recentDuplicates[dedupKey] = now;
            _logger?.LogInformation("TicketAutoRunService: ticket #{Id} already has an active run — skipping", ticketId);
            await _failures.RecordAsync(new FailureLogEntry
            {
                ProjectSlug = projectSlug,
                TicketId = ticketId,
                Kind = FailureKinds.DuplicateRunBlocked,
                Message = $"Skipped: ticket #{ticketId} already has an active run.",
                RequiredAction = "Wait for the current run to finish, or click Stop first.",
            });
            return;
        }

        // Clean up old dedup entries
        CleanupDuplicateSuppression();

        // 2. Load ticket
        var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
        if (ticket is null) return;

        // 3. Parse execution mode
        var executionMode = ParseExecutionMode(ticket.ExecutionModeOverride);

        // 4. Stub modes: block with clear failure
        if (executionMode is ExecutionMode.CaoGoverned or ExecutionMode.TeamWorkflow)
        {
            await BlockWithFailureAsync(projectSlug, ticketId, FailureKinds.CaoNotImplemented,
                $"{executionMode} mode is not yet implemented for auto-run. " +
                "Use the manual Start Run button or configure a DirectOpenCode / LegacyClaude mode.",
                "Use DirectOpenCode or LegacyClaude execution mode",
                addComment: true, moveToBlocked: true, ct: ct);
            return;
        }

        // 5. Manual mode: do nothing
        if (executionMode == ExecutionMode.Manual)
        {
            _logger?.LogInformation("TicketAutoRunService: ticket #{Id} is Manual mode — no auto-run", ticketId);
            return;
        }

        // 6. Plan gate
        if (ticket.RequiresPlan && !string.Equals(ticket.PlanStatus, PlanStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            await BlockWithFailureAsync(projectSlug, ticketId, FailureKinds.PlanNotApproved,
                $"Execution blocked: RequiresApprovedPlan=true but plan status is '{ticket.PlanStatus}' (expected 'approved'). " +
                "Draft and approve a plan before running.",
                "Draft and approve a plan",
                addComment: true, moveToBlocked: false, ct: ct);
            return;
        }

        // 7. Policy check
        if (_policyService is not null)
        {
            var labels = ticket.Labels.Select(l => l.Name).ToList();
            var decision = await _policyService.CanExecuteDirectAsync(
                projectSlug, ticketId, executionMode,
                ticket.ProviderOverride, ticket.ModelOverride, labels, ct);

            if (!decision.Allowed)
            {
                await BlockWithFailureAsync(projectSlug, ticketId, FailureKinds.PolicyDenied,
                    $"Execution policy denied: {decision.Reason}",
                    decision.RequiredAction,
                    addComment: true, moveToBlocked: true, ct: ct);
                return;
            }
        }

        // 8. Resolve runner
        var runner = _runnerRegistry.ResolveRunner(executionMode);
        if (runner is null || !runner.IsAvailable)
        {
            await BlockWithFailureAsync(projectSlug, ticketId, FailureKinds.RunnerUnavailable,
                $"Runner for {executionMode} is not available. Check OpenCode CLI installation or configure the runner.",
                "Ensure runner is installed and available",
                addComment: true, moveToBlocked: true, ct: ct);
            return;
        }

        // 9. Worktree
        string? worktreePath = null;
        string? branchName = null;
        if (ticket.UseWorktree && _worktreeService is not null)
        {
            try
            {
                var context = new TicketExecutionContext
                {
                    ProjectSlug = projectSlug,
                    WorkspacePath = _projects.ResolveWorkspacePath(
                        await _projects.GetProjectAsync(projectSlug) ?? throw new InvalidOperationException()),
                    TicketId = ticketId,
                    TicketTitle = ticket.Title,
                    ExecutionMode = executionMode,
                    ForceWorktree = true
                };
                var wt = await _worktreeService.EnsureForTicketAsync(context, ct);
                worktreePath = wt.WorktreePath;
                branchName = wt.BranchName;
                _logger?.LogInformation("Worktree ready: {Path} (branch {Branch})", worktreePath, branchName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Worktree creation failed for ticket #{Id}", ticketId);
                await BlockWithFailureAsync(projectSlug, ticketId, FailureKinds.WorktreeCreateFailed,
                    $"Failed to create worktree for ticket #{ticketId}: {ex.Message}",
                    "Check disk space and workspace permissions",
                    addComment: true, moveToBlocked: true, ct: ct);
                return;
            }
        }

        // 10. Build request
        var project = await _projects.GetProjectAsync(projectSlug);
        var workspacePath = project is not null ? _projects.ResolveWorkspacePath(project) : "";
        var runId = Guid.NewGuid().ToString("N");

        var request = new RunnerRequest
        {
            ProjectSlug = projectSlug,
            WorkspacePath = workspacePath,
            AgentName = ticket.AssignedTo ?? "automation",
            SkillFile = $"{(ticket.AssignedTo ?? "automation")}/SKILL.md",
            TicketId = ticketId,
            TicketTitle = ticket.Title,
            TicketStatus = ticket.Status,
            TicketDescription = ticket.Description,
            Labels = ticket.Labels.Select(l => l.Name).ToList(),
            Assignee = ticket.AssignedTo,
            CurrentColumn = to,
            Prompt = ticket.PlanBody ?? ticket.Description,
            ConcurrencyGroup = $"ticket-{ticketId}",
            Provider = ticket.ProviderOverride,
            Model = ticket.ModelOverride,
            Profile = ticket.ProfileOverride,
            ExecutionMode = executionMode,
            WorktreePath = worktreePath,
            BranchName = branchName,
            RunId = runId,
            ExecutionMetadata = new ExecutionMetadata
            {
                Mode = executionMode.ToString(),
                Runner = runner.Kind,
                Provider = ticket.ProviderOverride,
                Model = ticket.ModelOverride,
                Profile = ticket.ProfileOverride,
                OpenCodeAgent = ticket.OpenCodeAgent,
                WorktreePath = worktreePath,
                BranchName = branchName,
                TicketId = ticketId.ToString(),
                ProjectId = projectSlug,
            }
        };

        // 11. Register run
        var run = new AgentRun
        {
            RunId = runId,
            ProjectSlug = projectSlug,
            TicketId = ticketId,
            AgentName = ticket.AssignedTo ?? "automation",
            SkillFile = request.SkillFile,
            ConcurrencyGroup = request.ConcurrencyGroup,
            StartedAt = DateTime.UtcNow,
            RuntimeId = runner.Kind,
        };
        _runRegistry.Register(run);

        // 12. Activity comment
        try
        {
            var modeLabel = executionMode == ExecutionMode.LegacyClaude ? "LegacyClaude" : "OpenCode";
            var details = new List<string> { $"mode: {modeLabel}" };
            if (!string.IsNullOrEmpty(ticket.ProviderOverride)) details.Add($"provider: {ticket.ProviderOverride}");
            if (!string.IsNullOrEmpty(ticket.ModelOverride)) details.Add($"model: {ticket.ModelOverride}");
            if (!string.IsNullOrEmpty(ticket.ProfileOverride)) details.Add($"profile: {ticket.ProfileOverride}");
            if (!string.IsNullOrEmpty(worktreePath)) details.Add($"worktree: {Path.GetFileName(worktreePath)}");
            if (!string.IsNullOrEmpty(branchName)) details.Add($"branch: {branchName}");
            await _tickets.AddActivityAsync(projectSlug, ticketId,
                $"Auto-execution started: {string.Join(", ", details)}", "automation");
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "AddActivity failed (non-blocking) for ticket #{Id}", ticketId); }

        // 13. Start runner (fire-and-forget, handle completion in continuation)
        _ = StartAndHandleCompletionAsync(request, runner, run, ct);

        _logger?.LogInformation("TicketAutoRunService: started run {RunId} for ticket #{Id} via {Runner}",
            runId, ticketId, runner.Kind);
    }

    private async Task StartAndHandleCompletionAsync(
        RunnerRequest request,
        IAgentRunner runner,
        AgentRun run,
        CancellationToken ct)
    {
        try
        {
            var result = await runner.StartAsync(request, ct);
            await HandleRunCompletionAsync(request, run, result, ct);
        }
        catch (OperationCanceledException)
        {
            _runRegistry.Complete(run.RunId, AgentRunStatus.Stopped, null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Runner {Runner} crashed for ticket #{Id}", runner.Kind, request.TicketId);
            _runRegistry.Complete(run.RunId, AgentRunStatus.Failed, -1);
            await RecordAndCommentFailureAsync(request.ProjectSlug, request.TicketId ?? 0,
                FailureKinds.RunStartFailed, $"Runner crashed: {ex.Message}", run.RunId, ct);
        }
    }

    private async Task HandleRunCompletionAsync(
        RunnerRequest request,
        AgentRun run,
        AgentRunResult result,
        CancellationToken ct)
    {
        _runRegistry.Complete(run.RunId, result.Status, result.ExitCode);

        // Save execution metadata
        if (_metadataStore is not null && request.TicketId is int tid)
        {
            try
            {
                var meta = new TicketExecutionMetadata
                {
                    ProjectSlug = request.ProjectSlug,
                    TicketId = tid,
                    RunId = run.RunId,
                    ExecutionMode = request.ExecutionMode.ToString(),
                    RunnerKind = result.RunnerKind,
                    Provider = result.ExecutionMetadata?.Provider ?? request.Provider,
                    Model = result.ExecutionMetadata?.Model ?? request.Model,
                    Profile = result.ExecutionMetadata?.Profile ?? request.Profile,
                    OpenCodeAgent = result.ExecutionMetadata?.OpenCodeAgent ?? request.ExecutionMetadata?.OpenCodeAgent,
                    SessionId = result.SessionId,
                    WorktreePath = result.ExecutionMetadata?.WorktreePath ?? request.WorktreePath,
                    BranchName = result.ExecutionMetadata?.BranchName ?? request.BranchName,
                    Status = result.Status,
                    StartedAt = result.StartedAt,
                    FinishedAt = result.FinishedAt,
                    LastError = result.ExecutionMetadata?.LastError ?? result.Stderr,
                };
                await _metadataStore.SaveAsync(meta, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save execution metadata for run {RunId}", run.RunId);
            }
        }

        if (request.TicketId is not int ticketId2) return;

        // Auto status transition
        try
        {
            if (result.Status == AgentRunStatus.Completed)
            {
                // Always go to Review (not auto-Done — safety gate)
                await _tickets.MoveTicketAsync(request.ProjectSlug, ticketId2, "Review", "automation");
                await _tickets.AddActivityAsync(request.ProjectSlug, ticketId2, "Auto-run completed → Review", "automation");
            }
            else if (result.Status == AgentRunStatus.Failed)
            {
                await _tickets.MoveTicketAsync(request.ProjectSlug, ticketId2, "Blocked", "automation");
                await _tickets.AddActivityAsync(request.ProjectSlug, ticketId2, "Auto-run failed → Blocked", "automation");
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    var brief = result.Stderr.Length > 300 ? result.Stderr[..300] + "…" : result.Stderr;
                    await _tickets.AddCommentAsync(request.ProjectSlug, ticketId2,
                        $"Run failed (exit {result.ExitCode}):\n```\n{brief}\n```", "automation");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Post-run status transition failed for ticket #{Id}", ticketId2);
        }
    }

    private async Task BlockWithFailureAsync(
        string projectSlug,
        int ticketId,
        string failureKind,
        string message,
        string? requiredAction,
        bool addComment,
        bool moveToBlocked,
        CancellationToken ct)
    {
        await _failures.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = projectSlug,
            TicketId = ticketId,
            Kind = failureKind,
            Message = message,
            RequiredAction = requiredAction,
        });

        if (addComment)
        {
            try
            {
                await _tickets.AddCommentAsync(projectSlug, ticketId,
                    $"⚠️ **Auto-run blocked** [{failureKind}]\n\n{message}" +
                    (requiredAction is not null ? $"\n\n→ {requiredAction}" : ""),
                    "automation");
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "AddComment failed (non-blocking) for ticket #{Id}", ticketId); }
        }

        if (moveToBlocked)
        {
            try
            {
                var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
                if (ticket is not null && !string.Equals(ticket.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
                {
                    await _tickets.MoveTicketAsync(projectSlug, ticketId, "Blocked", "automation");
                }
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "MoveTicket failed (non-blocking) for ticket #{Id}", ticketId); }
        }

        _logger?.LogWarning("TicketAutoRunService: blocked ticket #{Id} [{Kind}] — {Message}",
            ticketId, failureKind, message);
    }

    private async Task RecordAndCommentFailureAsync(
        string projectSlug,
        int ticketId,
        string failureKind,
        string message,
        string? runId,
        CancellationToken ct)
    {
        await _failures.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = projectSlug,
            TicketId = ticketId,
            Kind = failureKind,
            Message = message,
            RunId = runId,
        });

        try
        {
            await _tickets.AddCommentAsync(projectSlug, ticketId,
                $"⚠️ **Auto-run failed** [{failureKind}]\n\n{message}", "automation");
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "AddComment failed (non-blocking) for ticket #{Id}", ticketId); }
    }

    private static ExecutionMode ParseExecutionMode(string? override_) =>
        !string.IsNullOrEmpty(override_)
        && Enum.TryParse<ExecutionMode>(override_, ignoreCase: true, out var m)
            ? m
            : ExecutionMode.LegacyClaude;

    private void CleanupDuplicateSuppression()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-DuplicateSuppressionSeconds * 2);
        var keysToRemove = _recentDuplicates
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
            _recentDuplicates.TryRemove(key, out _);
    }

    public void Dispose() => _cts?.Dispose();
}