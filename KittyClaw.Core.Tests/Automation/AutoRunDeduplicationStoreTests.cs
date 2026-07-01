using KittyClaw.Core.Automation;
using Xunit;

namespace KittyClaw.Core.Tests.Automation;

public class AutoRunDeduplicationStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly AutoRunDeduplicationStore _store;

    public AutoRunDeduplicationStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"beaverboard-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new AutoRunDeduplicationStore(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task TryAcquireLock_ReturnsNull_WhenFirstAttempt()
    {
        var result = await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        Assert.Null(result);
    }

    [Fact]
    public async Task TryAcquireLock_ReturnsExistingLock_WhenDuplicate()
    {
        // First attempt - should succeed
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        // Second attempt with same fingerprint - should return existing lock
        var result = await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        Assert.NotNull(result);
        Assert.Equal("test-project", result.ProjectSlug);
        Assert.Equal(1, result.TicketId);
    }

    [Fact]
    public async Task TryAcquireLock_AllowsDifferentFingerprints()
    {
        // First fingerprint
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        // Different fingerprint - should succeed
        var result = await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Review->InProgress");

        Assert.Null(result);
    }

    [Fact]
    public async Task MarkRunning_UpdatesStatus()
    {
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        var locks = await _store.GetLocksForTicketAsync("test-project", 1);
        var lockId = locks.First().Id;

        await _store.MarkRunningAsync("test-project", lockId, "run-123");

        var updatedLocks = await _store.GetLocksForTicketAsync("test-project", 1);
        var updatedLock = updatedLocks.First(l => l.Id == lockId);

        Assert.Equal("running", updatedLock.Status);
        Assert.Equal("run-123", updatedLock.RunId);
    }

    [Fact]
    public async Task MarkCompleted_UpdatesStatus()
    {
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        var locks = await _store.GetLocksForTicketAsync("test-project", 1);
        var lockId = locks.First().Id;

        await _store.MarkCompletedAsync("test-project", lockId, "success");

        var updatedLocks = await _store.GetLocksForTicketAsync("test-project", 1);
        var updatedLock = updatedLocks.First(l => l.Id == lockId);

        Assert.Equal("completed", updatedLock.Status);
        Assert.NotNull(updatedLock.ResolvedAt);
        Assert.Equal("success", updatedLock.Resolution);
    }

    [Fact]
    public async Task CleanupStaleLocks_ExpiresOldLocks()
    {
        // Create a lock with short TTL
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress",
            ttl: TimeSpan.FromMilliseconds(1)); // 1ms TTL

        // Wait for it to expire
        await Task.Delay(50);

        var cleaned = await _store.CleanupStaleLocksAsync("test-project");

        Assert.Equal(1, cleaned);
    }

    [Fact]
    public async Task ActiveLocksForTicket_ReturnsOnlyActive()
    {
        // Create two locks
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Review->InProgress");

        // Complete first lock
        var locks = await _store.GetLocksForTicketAsync("test-project", 1);
        await _store.MarkCompletedAsync("test-project", locks.First().Id);

        // Should only return one active lock
        var activeLocks = await _store.ActiveLocksForTicketAsync("test-project", 1);

        Assert.Single(activeLocks);
    }

    [Fact]
    public async Task CancelAsync_UpdatesStatus()
    {
        await _store.TryAcquireLockAsync(
            projectSlug: "test-project",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "test:1:status-change:Backlog->InProgress");

        var locks = await _store.GetLocksForTicketAsync("test-project", 1);
        var lockId = locks.First().Id;

        await _store.CancelAsync("test-project", lockId, "manual-cancel");

        var updatedLocks = await _store.GetLocksForTicketAsync("test-project", 1);
        var updatedLock = updatedLocks.First(l => l.Id == lockId);

        Assert.Equal("cancelled", updatedLock.Status);
        Assert.Equal("manual-cancel", updatedLock.Resolution);
    }

    [Fact]
    public async Task DifferentProjects_AreIsolated()
    {
        await _store.TryAcquireLockAsync(
            projectSlug: "project-a",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "a:1:status-change:Backlog->InProgress");

        await _store.TryAcquireLockAsync(
            projectSlug: "project-b",
            ticketId: 1,
            triggerType: "status-change",
            triggerFingerprint: "b:1:status-change:Backlog->InProgress");

        var locksA = await _store.GetLocksForTicketAsync("project-a", 1);
        var locksB = await _store.GetLocksForTicketAsync("project-b", 1);

        Assert.Single(locksA);
        Assert.Single(locksB);
    }
}
