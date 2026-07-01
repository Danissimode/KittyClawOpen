using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// SQLite-backed store for ProcessEvent records.
/// The Process Event Ledger - structured, actionable, resolvable events.
/// </summary>
public sealed class ProcessEventStore
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;

    public ProcessEventStore(string dataDir, ILogger? logger = null)
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
            CREATE TABLE IF NOT EXISTS ProcessEvents (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                Level TEXT NOT NULL,
                Category TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Title TEXT NOT NULL,
                Message TEXT,
                TicketId INTEGER,
                RunId TEXT,
                AgentId TEXT,
                Provider TEXT,
                Model TEXT,
                SessionId TEXT,
                Source TEXT NOT NULL,
                RawPayload TEXT,
                Status TEXT NOT NULL DEFAULT 'open',
                Resolution TEXT,
                ResolvedBy TEXT,
                SuggestedActionsJson TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                AcknowledgedAt TEXT,
                ResolvedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_Project_Status
            ON ProcessEvents (ProjectSlug, Status);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_Project_Category
            ON ProcessEvents (ProjectSlug, Category);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_Project_Level
            ON ProcessEvents (ProjectSlug, Level);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_TicketId
            ON ProcessEvents (TicketId);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_RunId
            ON ProcessEvents (RunId);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_AgentId
            ON ProcessEvents (AgentId);
            CREATE INDEX IF NOT EXISTS IX_ProcessEvents_CreatedAt
            ON ProcessEvents (CreatedAt DESC);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ProcessEvent> RecordAsync(ProcessEvent evt, CancellationToken ct = default)
    {
        var dbPath = DbPath(evt.ProjectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ProcessEvents
            (Id, ProjectSlug, Level, Category, EventType, Title, Message,
             TicketId, RunId, AgentId, Provider, Model, SessionId,
             Source, RawPayload, Status, SuggestedActionsJson, CreatedAt, UpdatedAt)
            VALUES ($id, $project, $level, $category, $eventType, $title, $message,
                    $ticketId, $runId, $agentId, $provider, $model, $sessionId,
                    $source, $rawPayload, $status, $suggestedActions, $createdAt, $updatedAt)
        """;
        cmd.Parameters.AddWithValue("$id", evt.Id);
        cmd.Parameters.AddWithValue("$project", evt.ProjectSlug);
        cmd.Parameters.AddWithValue("$level", evt.Level);
        cmd.Parameters.AddWithValue("$category", evt.Category);
        cmd.Parameters.AddWithValue("$eventType", evt.EventType);
        cmd.Parameters.AddWithValue("$title", evt.Title);
        cmd.Parameters.AddWithValue("$message", (object?)evt.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ticketId", (object?)evt.TicketId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$runId", (object?)evt.RunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$agentId", (object?)evt.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$provider", (object?)evt.Provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)evt.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionId", (object?)evt.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", evt.Source);
        cmd.Parameters.AddWithValue("$rawPayload", (object?)evt.RawPayload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", evt.Status);
        cmd.Parameters.AddWithValue("$suggestedActions", (object?)evt.SuggestedActionsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", evt.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", evt.UpdatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger?.LogInformation("[EVENT] {Level} {Category}/{EventType}: {Title}", 
            evt.Level, evt.Category, evt.EventType, evt.Title);

        return evt;
    }

    public async Task<bool> AcknowledgeAsync(string projectSlug, string eventId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ProcessEvents 
            SET Status = 'acknowledged', AcknowledgedAt = $now, UpdatedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", eventId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> ResolveAsync(string projectSlug, string eventId, string? resolution = null, string? resolvedBy = null, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ProcessEvents 
            SET Status = 'resolved', Resolution = $resolution, ResolvedBy = $resolvedBy,
                ResolvedAt = $now, UpdatedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", eventId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$resolution", (object?)resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$resolvedBy", (object?)resolvedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> DismissAsync(string projectSlug, string eventId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ProcessEvents 
            SET Status = 'dismissed', UpdatedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", eventId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<ProcessEvent>> OpenEventsAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProcessEvents WHERE ProjectSlug = $project AND Status = 'open' ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ProcessEvent>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));
        return results;
    }

    public async Task<IReadOnlyList<ProcessEvent>> ByCategoryAsync(string projectSlug, string category, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProcessEvents WHERE ProjectSlug = $project AND Category = $category ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$category", category);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ProcessEvent>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));
        return results;
    }

    public async Task<IReadOnlyList<ProcessEvent>> ForTicketAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProcessEvents WHERE ProjectSlug = $project AND TicketId = $ticket ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$ticket", ticketId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ProcessEvent>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));
        return results;
    }

    public async Task<IReadOnlyList<ProcessEvent>> ForAgentAsync(string projectSlug, string agentId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProcessEvents WHERE ProjectSlug = $project AND AgentId = $agent ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$agent", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ProcessEvent>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));
        return results;
    }

    public async Task<IReadOnlyList<ProcessEvent>> RecentAsync(string projectSlug, int limit = 50, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProcessEvents WHERE ProjectSlug = $project ORDER BY CreatedAt DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ProcessEvent>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));
        return results;
    }

    private static ProcessEvent ReadEvent(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        Level = r.GetString(r.GetOrdinal("Level")),
        Category = r.GetString(r.GetOrdinal("Category")),
        EventType = r.GetString(r.GetOrdinal("EventType")),
        Title = r.GetString(r.GetOrdinal("Title")),
        Message = r.IsDBNull(r.GetOrdinal("Message")) ? null : r.GetString(r.GetOrdinal("Message")),
        TicketId = r.IsDBNull(r.GetOrdinal("TicketId")) ? null : r.GetInt32(r.GetOrdinal("TicketId")),
        RunId = r.IsDBNull(r.GetOrdinal("RunId")) ? null : r.GetString(r.GetOrdinal("RunId")),
        AgentId = r.IsDBNull(r.GetOrdinal("AgentId")) ? null : r.GetString(r.GetOrdinal("AgentId")),
        Provider = r.IsDBNull(r.GetOrdinal("Provider")) ? null : r.GetString(r.GetOrdinal("Provider")),
        Model = r.IsDBNull(r.GetOrdinal("Model")) ? null : r.GetString(r.GetOrdinal("Model")),
        SessionId = r.IsDBNull(r.GetOrdinal("SessionId")) ? null : r.GetString(r.GetOrdinal("SessionId")),
        Source = r.GetString(r.GetOrdinal("Source")),
        RawPayload = r.IsDBNull(r.GetOrdinal("RawPayload")) ? null : r.GetString(r.GetOrdinal("RawPayload")),
        Status = r.GetString(r.GetOrdinal("Status")),
        Resolution = r.IsDBNull(r.GetOrdinal("Resolution")) ? null : r.GetString(r.GetOrdinal("Resolution")),
        ResolvedBy = r.IsDBNull(r.GetOrdinal("ResolvedBy")) ? null : r.GetString(r.GetOrdinal("ResolvedBy")),
        SuggestedActionsJson = r.IsDBNull(r.GetOrdinal("SuggestedActionsJson")) ? null : r.GetString(r.GetOrdinal("SuggestedActionsJson")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("UpdatedAt"))),
        AcknowledgedAt = r.IsDBNull(r.GetOrdinal("AcknowledgedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("AcknowledgedAt"))),
        ResolvedAt = r.IsDBNull(r.GetOrdinal("ResolvedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ResolvedAt")))
    };
}
