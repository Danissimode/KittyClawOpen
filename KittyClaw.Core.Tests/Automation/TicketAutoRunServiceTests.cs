using System.Collections.Concurrent;
using System.Threading;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Integrations.OpenCode;
using KittyClaw.Core.Models;
using KittyClaw.Core.Services;
using Xunit;

namespace KittyClaw.Core.Tests.Automation;

public sealed class TicketAutoRunServiceTests : IDisposable
{
    private readonly string _tmp;
    private readonly ProjectService _projects;
    private readonly TicketService _tickets;
    private readonly FailureLogStore _failures;
    private readonly AgentRunRegistry _runRegistry;
    private readonly TestRunnerRegistry _registry;
    private readonly TicketAutoRunService _svc;

    public TicketAutoRunServiceTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kc-autorun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _projects = new ProjectService(_tmp);
        _tickets = new TicketService(_projects, new MemberService(_projects));
        _failures = new FailureLogStore();
        _runRegistry = new AgentRunRegistry();
        _registry = new TestRunnerRegistry();

        _svc = new TicketAutoRunService(
            _tickets, _projects, _registry, _runRegistry,
            metadataStore: null, worktreeService: null, policyService: null,
            failures: _failures, logger: null);

        // Create a test project
        _projects.CreateProjectAsync("test-proj").Wait();
    }

    public void Dispose()
    {
        _svc.Dispose();
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private async Task<Ticket> MakeTicket(string execMode = "LegacyClaude",
        string planStatus = "none", bool requiresPlan = false,
        bool useWorktree = false, string status = "Todo")
    {
        var ticket = await _tickets.CreateTicketAsync(
            "test-proj", "Test ticket", "description",
            executionModeOverride: execMode,
            useWorktree: useWorktree);
        if (planStatus != "none" || requiresPlan)
            await _tickets.UpdateTicketAsync("test-proj", ticket.Id, planStatus: planStatus, requiresPlan: requiresPlan);
        if (status != "Todo")
            await _tickets.MoveTicketAsync("test-proj", ticket.Id, status);
        return ticket;
    }

    // ── Scenario 1: approved plan → starts runner ──

    [Fact]
    public async Task ApprovedPlan_MovesToInProgress_StartsRunner()
    {
        var ticket = await MakeTicket("DirectOpenCode", planStatus: "approved",
            useWorktree: false, status: "Todo");

        _registry.NextResult = new AgentRunResult
        {
            Status = AgentRunStatus.Completed, ExitCode = 0,
            StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            Duration = TimeSpan.FromMinutes(1), RunnerKind = "opencode"
        };

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        var runs = _runRegistry.AllForTicket("test-proj", ticket.Id).ToList();
        Assert.Single(runs);
        Assert.Equal(AgentRunStatus.Completed, runs[0].Status);
        Assert.Empty(_failures.ForTicket("test-proj", ticket.Id));
    }

    // ── Scenario 2: RequiresPlan=true but not approved → blocked ──

    [Fact]
    public async Task PlanNotApproved_BlocksWithFailureEntry()
    {
        var ticket = await MakeTicket("DirectOpenCode", planStatus: "drafting",
            requiresPlan: true, status: "Todo");

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        var failures = _failures.ForTicket("test-proj", ticket.Id);
        Assert.Single(failures);
        Assert.Equal(FailureKinds.PlanNotApproved, failures[0].Kind);
        Assert.False(_runRegistry.ActiveForTicket("test-proj", ticket.Id).Any());
    }

    // ── Scenario 3: active run exists → no duplicate ──

    [Fact]
    public async Task ActiveRunExists_DoesNotStartDuplicate()
    {
        var ticket = await MakeTicket("DirectOpenCode", planStatus: "approved",
            useWorktree: false, status: "Todo");

        // Register a fake active run
        _runRegistry.Register(new AgentRun
        {
            RunId = "existing",
            ProjectSlug = "test-proj",
            TicketId = ticket.Id,
            AgentName = "a",
            SkillFile = "a/SKILL.md",
            ConcurrencyGroup = $"ticket-{ticket.Id}",
            StartedAt = DateTime.UtcNow,
        });

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        // Still only the original run
        var all = _runRegistry.AllForTicket("test-proj", ticket.Id).ToList();
        Assert.Single(all);
        Assert.Equal("existing", all[0].RunId);

        var failures = _failures.ForTicket("test-proj", ticket.Id);
        Assert.Contains(failures, f => f.Kind == FailureKinds.DuplicateRunBlocked);
    }

    // ── Scenario 4: CaoGoverned → blocked, moved to Blocked ──

    [Fact]
    public async Task CaoGoverned_BlockedWithNeedsCao()
    {
        var ticket = await MakeTicket("CaoGoverned", planStatus: "approved",
            useWorktree: false, status: "Todo");

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        var failures = _failures.ForTicket("test-proj", ticket.Id);
        Assert.Single(failures);
        Assert.Equal(FailureKinds.CaoNotImplemented, failures[0].Kind);

        // Ticket should be in Blocked
        var updated = await _tickets.GetTicketAsync("test-proj", ticket.Id);
        Assert.Equal("Blocked", updated!.Status);
    }

    // ── Scenario 5: Manual mode → no-op ──

    [Fact]
    public async Task ManualMode_DoesNothing()
    {
        var ticket = await MakeTicket("Manual", status: "Todo");

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        Assert.Empty(_failures.ForTicket("test-proj", ticket.Id));
        Assert.False(_runRegistry.ActiveForTicket("test-proj", ticket.Id).Any());
    }

    // ── Scenario 6: Non-InProgress transition → no-op ──

    [Fact]
    public async Task NonInProgressTransition_DoesNothing()
    {
        var ticket = await MakeTicket("DirectOpenCode", planStatus: "approved",
            useWorktree: false, status: "Todo");

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "Review", CancellationToken.None);

        Assert.Empty(_failures.ForTicket("test-proj", ticket.Id));
        Assert.False(_runRegistry.ActiveForTicket("test-proj", ticket.Id).Any());
    }

    // ── Scenario 7: Run completes → ticket moves to Review ──

    [Fact]
    public async Task RunCompletes_TicketMovesToReview()
    {
        var ticket = await MakeTicket("LegacyClaude", planStatus: "approved",
            useWorktree: false, status: "Todo");

        _registry.NextResult = new AgentRunResult
        {
            Status = AgentRunStatus.Completed, ExitCode = 0,
            StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            Duration = TimeSpan.FromSeconds(1), RunnerKind = "claude"
        };

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);

        // Let the fire-and-forget continuation run
        await Task.Delay(200);

        var updated = await _tickets.GetTicketAsync("test-proj", ticket.Id);
        Assert.Equal("Review", updated!.Status);
    }

    // ── Scenario 8: Run fails → ticket moves to Blocked ──

    [Fact]
    public async Task RunFails_TicketMovesToBlocked()
    {
        var ticket = await MakeTicket("LegacyClaude", planStatus: "approved",
            useWorktree: false, status: "Todo");

        _registry.NextResult = new AgentRunResult
        {
            Status = AgentRunStatus.Failed, ExitCode = 1,
            StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            Duration = TimeSpan.FromSeconds(1), RunnerKind = "claude",
            Stderr = "something went wrong"
        };

        await _svc.ProcessTransitionAsync("test-proj", ticket.Id, "Todo", "InProgress", CancellationToken.None);
        await Task.Delay(200);

        var updated = await _tickets.GetTicketAsync("test-proj", ticket.Id);
        Assert.Equal("Blocked", updated!.Status);
    }

    // ── Test double: controllable RunnerRegistry ──

    private sealed class TestRunnerRegistry : RunnerRegistry
    {
        public AgentRunResult? NextResult { get; set; }

        public TestRunnerRegistry() : base() { }

        public override IAgentRunner ResolveRunner(ExecutionMode executionMode)
        {
            return new ControlledRunner(NextResult ?? new AgentRunResult
            {
                Status = AgentRunStatus.Completed, ExitCode = 0,
                StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow,
                Duration = TimeSpan.Zero, RunnerKind = "controlled"
            });
        }
    }

    private sealed class ControlledRunner : IAgentRunner
    {
        private readonly AgentRunResult _result;

        public string Kind => "controlled";
        public string DisplayName => "Controlled";
        public bool IsAvailable => true;

        public ControlledRunner(AgentRunResult result) => _result = result;

        public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken ct) =>
            Task.FromResult(_result);

        public Task<bool> StopAsync(string runId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SteerAsync(string runId, string message, CancellationToken ct) => Task.FromResult(true);
        public Task<AgentRunStatus> GetStatusAsync(string runId, CancellationToken ct) => Task.FromResult(AgentRunStatus.Running);
    }
}