using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Persistent deduplication store for auto-run attempts.
/// Prevents duplicate agent runs across restarts and race conditions.
/// 
/// Storage: {dataDir}/roster/auto-run-locks.json or project SQLite DB.
/// </summary>
public sealed class AutoRunDeduplicationStore
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AutoRunDeduplicationStore(string dataDir, ILogger? logger = null)
    {
        _dataDir = dataDir;
        _logger = logger;
    }

    private string DbPath(string projectSlug)
    {
        var projectsDir = Path.Combine(_dataDir, "projects");
        Directory.CreateDirectory(projectsDir);
        return Path.Combine(projectsDir, $"{projectSlug}.db");
    }

    public static async Task EnsureTableAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AutoRunLocks (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                TicketId INTEGER NOT NULL,
                AutomationId TEXT,
                TriggerType TEXT NOT NULL,
                TriggerFingerprint TEXT NOT NULL,
                TargetStatus TEXT,
                Assignee TEXT,
                ExecutionProfile TEXT,
                Status TEXT NOT NULL DEFAULT 'pending',
                RunId TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                ExpiresAt TEXT,
                ResolvedAt TEXT,
                Resolution TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_AutoRunLocks_Project_Ticket
            ON AutoRunLocks (ProjectSlug, TicketId);
            CREATE INDEX IF NOT EXISTS IX_AutoRunLocks_Status
            ON AutoRunLocks (Status);
            CREATE INDEX IF NOT EXISTS IX_AutoRunLocks_ExpiresAt
            ON AutoRunLocks (ExpiresAt);
            CREATE INDEX IF NOT EXISTS IX_AutoRunLocks_TriggerFingerprint
            ON AutoRunLocks (TriggerFingerprint);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Try to acquire a lock for an auto-run. Returns null if lock acquired, or the existing lock if duplicate.
    /// </summary>
    public async Task<AutoRunLock?> TryAcquireLockAsync(
        string projectSlug,
        int ticketId,
        string triggerType,
        string triggerFingerprint,
        string? automationId = null,
        string? targetStatus = null,
        string? assignee = null,
        string? executionProfile = null,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);

        // Check for existing active lock with same fingerprint
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = """
            SELECT * FROM AutoRunLocks 
            WHERE ProjectSlug = $project AND TicketId = $ticket 
            AND TriggerFingerprint = $fingerprint 
            AND Status IN ('pending', 'running')
            AND (ExpiresAt IS NULL OR ExpiresAt > $now)
            LIMIT 1
        """;
        checkCmd.Parameters.AddWithValue("$project", projectSlug);
        checkCmd.Parameters.AddWithValue("$ticket", ticketId);
        checkCmd.Parameters.AddWithValue("$fingerprint", triggerFingerprint);
        checkCmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));

        await using var reader = await checkCmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            // Lock already exists - duplicate detected
            var existingLock = ReadLock(reader);
            _logger?.LogInformation("AutoRun dedup: ticket #{TicketId} already has active lock {LockId}", ticketId, existingLock.Id);
            return existingLock;
        }
        await reader.CloseAsync();

        // No active lock - create new one
        var lockId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : now.AddHours(1);

        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO AutoRunLocks
            (Id, ProjectSlug, TicketId, AutomationId, TriggerType, TriggerFingerprint, 
             TargetStatus, Assignee, ExecutionProfile, Status, CreatedAt, UpdatedAt, ExpiresAt)
            VALUES ($id, $project, $ticket, $automation, $trigger, $fingerprint, 
                    $target, $assignee, $profile, 'pending', $now, $now, $expires)
        """;
        insertCmd.Parameters.AddWithValue("$id", lockId);
        insertCmd.Parameters.AddWithValue("$project", projectSlug);
        insertCmd.Parameters.AddWithValue("$ticket", ticketId);
        insertCmd.Parameters.AddWithValue("$automation", (object?)automationId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$trigger", triggerType);
        insertCmd.Parameters.AddWithValue("$fingerprint", triggerFingerprint);
        insertCmd.Parameters.AddWithValue("$target", (object?)targetStatus ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$assignee", (object?)assignee ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$profile", (object?)executionProfile ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$now", now.ToString("o"));
        insertCmd.Parameters.AddWithValue("$expires", expiresAt.ToString("o"));

        await insertCmd.ExecuteNonQueryAsync(ct);

        _logger?.LogInformation("AutoRun dedup: acquired lock {LockId} for ticket #{TicketId}", lockId, ticketId);

        return null; // null = lock acquired successfully
    }

    /// <summary>
    /// Mark a lock as running with the associated run ID.
    /// </summary>
    public async Task MarkRunningAsync(string projectSlug, string lockId, string runId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutoRunLocks 
            SET Status = 'running', RunId = $runId, UpdatedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", lockId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$runId", runId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Mark a lock as completed.
    /// </summary>
    public async Task MarkCompletedAsync(string projectSlug, string lockId, string? resolution = null, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutoRunLocks 
            SET Status = 'completed', ResolvedAt = $now, UpdatedAt = $now, Resolution = $resolution
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", lockId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$resolution", (object?)resolution ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Mark a lock as failed.
    /// </summary>
    public async Task MarkFailedAsync(string projectSlug, string lockId, string? resolution = null, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutoRunLocks 
            SET Status = 'failed', ResolvedAt = $now, UpdatedAt = $now, Resolution = $resolution
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", lockId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$resolution", (object?)resolution ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Cancel a lock (manual override).
    /// </summary>
    public async Task CancelAsync(string projectSlug, string lockId, string? resolution = null, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutoRunLocks 
            SET Status = 'cancelled', ResolvedAt = $now, UpdatedAt = $now, Resolution = $resolution
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", lockId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$resolution", (object?)resolution ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Detect and clean up stale locks (expired or stuck in pending/running).
    /// </summary>
    public async Task<int> CleanupStaleLocksAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutoRunLocks 
            SET Status = 'expired', UpdatedAt = $now, Resolution = 'stale-lock-cleanup'
            WHERE Status IN ('pending', 'running') 
            AND ExpiresAt IS NOT NULL AND ExpiresAt < $now
        """;
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        
        if (affected > 0)
        {
            _logger?.LogInformation("AutoRun dedup: cleaned up {Count} stale locks for {Project}", affected, projectSlug);
        }
        
        return affected;
    }

    /// <summary>
    /// Get all locks for a ticket.
    /// </summary>
    public async Task<IReadOnlyList<AutoRunLock>> GetLocksForTicketAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AutoRunLocks WHERE ProjectSlug = $project AND TicketId = $ticket ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$ticket", ticketId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AutoRunLock>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadLock(reader));
        return results;
    }

    /// <summary>
    /// Get active locks for a ticket.
    /// </summary>
    public async Task<IReadOnlyList<AutoRunLock>> ActiveLocksForTicketAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var all = await GetLocksForTicketAsync(projectSlug, ticketId, ct);
        return all.Where(l => l.Status is "pending" or "running").ToList();
    }

    private static AutoRunLock ReadLock(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        TicketId = r.GetInt32(r.GetOrdinal("TicketId")),
        AutomationId = r.IsDBNull(r.GetOrdinal("AutomationId")) ? null : r.GetString(r.GetOrdinal("AutomationId")),
        TriggerType = r.GetString(r.GetOrdinal("TriggerType")),
        TriggerFingerprint = r.GetString(r.GetOrdinal("TriggerFingerprint")),
        TargetStatus = r.IsDBNull(r.GetOrdinal("TargetStatus")) ? null : r.GetString(r.GetOrdinal("TargetStatus")),
        Assignee = r.IsDBNull(r.GetOrdinal("Assignee")) ? null : r.GetString(r.GetOrdinal("Assignee")),
        ExecutionProfile = r.IsDBNull(r.GetOrdinal("ExecutionProfile")) ? null : r.GetString(r.GetOrdinal("ExecutionProfile")),
        Status = r.GetString(r.GetOrdinal("Status")),
        RunId = r.IsDBNull(r.GetOrdinal("RunId")) ? null : r.GetString(r.GetOrdinal("RunId")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("UpdatedAt"))),
        ExpiresAt = r.IsDBNull(r.GetOrdinal("ExpiresAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ExpiresAt"))),
        ResolvedAt = r.IsDBNull(r.GetOrdinal("ResolvedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ResolvedAt"))),
        Resolution = r.IsDBNull(r.GetOrdinal("Resolution")) ? null : r.GetString(r.GetOrdinal("Resolution"))
    };
}

/// <summary>
/// Represents a deduplication lock for an auto-run attempt.
/// </summary>
public sealed class AutoRunLock
{
    public string Id { get; init; } = "";
    public string ProjectSlug { get; init; } = "";
    public int TicketId { get; init; }
    public string? AutomationId { get; init; }
    public string TriggerType { get; init; } = "";
    public string TriggerFingerprint { get; init; } = "";
    public string? TargetStatus { get; init; }
    public string? Assignee { get; init; }
    public string? ExecutionProfile { get; init; }
    public string Status { get; init; } = "pending"; // pending, running, completed, failed, cancelled, expired
    public string? RunId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? Resolution { get; init; }
}
