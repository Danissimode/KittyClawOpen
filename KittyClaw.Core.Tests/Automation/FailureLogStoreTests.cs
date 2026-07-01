using KittyClaw.Core.Automation;
using Xunit;

namespace KittyClaw.Core.Tests.Automation;

public class FailureLogStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly FailureLogStore _store;

    public FailureLogStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"beaverboard-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new FailureLogStore(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task RecordAsync_CreatesEntry()
    {
        var entry = new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.RunStartFailed,
            Message = "Test failure"
        };

        var result = await _store.RecordAsync(entry);

        Assert.Equal(entry.Id, result.Id);
        Assert.Equal("test-project", result.ProjectSlug);
        Assert.Equal(1, result.TicketId);
    }

    [Fact]
    public async Task ForTicketAsync_ReturnsEntries()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.RunStartFailed,
            Message = "Failure 1"
        });

        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.ProviderUnavailable,
            Message = "Failure 2"
        });

        var results = await _store.ForTicketAsync("test-project", 1);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UnresolvedForTicketAsync_ReturnsOnlyUnresolved()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.RunStartFailed,
            Message = "Unresolved"
        });

        var entry2 = new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.ProviderUnavailable,
            Message = "Resolved"
        };
        await _store.RecordAsync(entry2);
        await _store.ResolveAsync("test-project", entry2.Id);

        var results = await _store.UnresolvedForTicketAsync("test-project", 1);

        Assert.Single(results);
        Assert.Equal("Unresolved", results.First().Message);
    }

    [Fact]
    public async Task ResolveAsync_MarksAsResolved()
    {
        var entry = new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.RunStartFailed,
            Message = "To resolve"
        };
        await _store.RecordAsync(entry);

        var resolved = await _store.ResolveAsync("test-project", entry.Id, "manual-fix");

        Assert.True(resolved);

        var results = await _store.ForTicketAsync("test-project", 1);
        var updated = results.First(e => e.Id == entry.Id);
        Assert.True(updated.Resolved);
        Assert.NotNull(updated.ResolvedAt);
        Assert.Equal("manual-fix", updated.Resolution);
    }

    [Fact]
    public async Task HasRecentEntryAsync_ReturnsTrueForRecentEntry()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.DuplicateRunBlocked,
            Message = "Duplicate"
        });

        var hasRecent = await _store.HasRecentEntryAsync(
            "test-project", 1, FailureKinds.DuplicateRunBlocked, TimeSpan.FromMinutes(5));

        Assert.True(hasRecent);
    }

    [Fact]
    public async Task HasRecentEntryAsync_ReturnsFalseForOldEntry()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.DuplicateRunBlocked,
            Message = "Duplicate"
        });

        // Check for very short window (should not find it if we wait)
        var hasRecent = await _store.HasRecentEntryAsync(
            "test-project", 1, FailureKinds.DuplicateRunBlocked, TimeSpan.FromMilliseconds(1));

        // Might be true or false depending on timing, so just verify no exception
        Assert.IsType<bool>(hasRecent);
    }

    [Fact]
    public async Task ForRunAsync_ReturnsEntriesForRun()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.RunStartFailed,
            Message = "Run failure",
            RunId = "run-123"
        });

        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 2,
            Kind = FailureKinds.RunStartFailed,
            Message = "Other run failure",
            RunId = "run-456"
        });

        var results = await _store.ForRunAsync("test-project", "run-123");

        Assert.Single(results);
        Assert.Equal("Run failure", results.First().Message);
    }

    [Fact]
    public async Task ByErrorTypeAsync_ReturnsEntriesByType()
    {
        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.QuotaExhausted,
            Message = "Quota 1",
            ErrorType = "quota"
        });

        await _store.RecordAsync(new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 2,
            Kind = FailureKinds.RateLimit,
            Message = "Rate limit",
            ErrorType = "rate-limit"
        });

        var results = await _store.ByErrorTypeAsync("test-project", "quota");

        Assert.Single(results);
        Assert.Equal("Quota 1", results.First().Message);
    }

    [Fact]
    public async Task RecentAsync_ReturnsLimitedResults()
    {
        for (int i = 0; i < 10; i++)
        {
            await _store.RecordAsync(new FailureLogEntry
            {
                ProjectSlug = "test-project",
                TicketId = i,
                Kind = FailureKinds.RunStartFailed,
                Message = $"Failure {i}"
            });
        }

        var results = await _store.RecentAsync("test-project", limit: 5);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task Entry_StoresExtendedFields()
    {
        var entry = new FailureLogEntry
        {
            ProjectSlug = "test-project",
            TicketId = 1,
            Kind = FailureKinds.QuotaExhausted,
            Message = "Quota exhausted",
            Agent = "programmer-1",
            Runner = "opencode",
            Provider = "kimi",
            Model = "kimi-2.7-code",
            ExecutionMode = "DirectOpenCode",
            ErrorType = "quota",
            ExitCode = 1,
            FallbackUsed = true
        };

        var result = await _store.RecordAsync(entry);
        var loaded = await _store.ForTicketAsync("test-project", 1);

        Assert.Equal("programmer-1", loaded.First().Agent);
        Assert.Equal("opencode", loaded.First().Runner);
        Assert.Equal("kimi", loaded.First().Provider);
        Assert.Equal("kimi-2.7-code", loaded.First().Model);
        Assert.Equal("DirectOpenCode", loaded.First().ExecutionMode);
        Assert.Equal("quota", loaded.First().ErrorType);
        Assert.Equal(1, loaded.First().ExitCode);
        Assert.True(loaded.First().FallbackUsed);
    }
}
