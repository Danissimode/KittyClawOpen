using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// SQLite-backed store for WorkflowReminder records.
/// Persistent scheduled actions that survive app restarts.
/// </summary>
public sealed class WorkflowReminderStore
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;

    public WorkflowReminderStore(string dataDir, ILogger? logger = null)
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
            CREATE TABLE IF NOT EXISTS WorkflowReminders (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                TicketId INTEGER,
                RunId TEXT,
                AgentId TEXT,
                SessionId TEXT,
                ReminderType TEXT NOT NULL,
                ScheduleType TEXT NOT NULL,
                DueAt TEXT,
                IntervalTicks INTEGER,
                ActionType TEXT NOT NULL,
                ActionPayloadJson TEXT,
                Prompt TEXT,
                Status TEXT NOT NULL DEFAULT 'active',
                CreatedBy TEXT,
                CreatedAt TEXT NOT NULL,
                LastFiredAt TEXT,
                NextFireAt TEXT,
                FireCount INTEGER NOT NULL DEFAULT 0,
                MaxFires INTEGER,
                Description TEXT,
                MetadataJson TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_WorkflowReminders_Project_Status
            ON WorkflowReminders (ProjectSlug, Status);
            CREATE INDEX IF NOT EXISTS IX_WorkflowReminders_Project_Type
            ON WorkflowReminders (ProjectSlug, ReminderType);
            CREATE INDEX IF NOT EXISTS IX_WorkflowReminders_NextFireAt
            ON WorkflowReminders (NextFireAt);
            CREATE INDEX IF NOT EXISTS IX_WorkflowReminders_TicketId
            ON WorkflowReminders (TicketId);
            CREATE INDEX IF NOT EXISTS IX_WorkflowReminders_AgentId
            ON WorkflowReminders (AgentId);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<WorkflowReminder> CreateAsync(WorkflowReminder reminder, CancellationToken ct = default)
    {
        var dbPath = DbPath(reminder.ProjectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WorkflowReminders
            (Id, ProjectSlug, TicketId, RunId, AgentId, SessionId,
             ReminderType, ScheduleType, DueAt, IntervalTicks,
             ActionType, ActionPayloadJson, Prompt,
             Status, CreatedBy, CreatedAt, NextFireAt, MaxFires, Description, MetadataJson)
            VALUES ($id, $project, $ticketId, $runId, $agentId, $sessionId,
                    $reminderType, $scheduleType, $dueAt, $intervalTicks,
                    $actionType, $actionPayload, $prompt,
                    $status, $createdBy, $createdAt, $nextFireAt, $maxFires, $description, $metadata)
        """;
        cmd.Parameters.AddWithValue("$id", reminder.Id);
        cmd.Parameters.AddWithValue("$project", reminder.ProjectSlug);
        cmd.Parameters.AddWithValue("$ticketId", (object?)reminder.TicketId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$runId", (object?)reminder.RunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$agentId", (object?)reminder.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionId", (object?)reminder.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reminderType", reminder.ReminderType);
        cmd.Parameters.AddWithValue("$scheduleType", reminder.ScheduleType);
        cmd.Parameters.AddWithValue("$dueAt", (object?)reminder.DueAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$intervalTicks", (object?)reminder.Interval?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$actionType", reminder.ActionType);
        cmd.Parameters.AddWithValue("$actionPayload", (object?)reminder.ActionPayloadJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prompt", (object?)reminder.Prompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", reminder.Status);
        cmd.Parameters.AddWithValue("$createdBy", (object?)reminder.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", reminder.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$nextFireAt", (object?)reminder.NextFireAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$maxFires", (object?)reminder.MaxFires ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)reminder.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metadata", (object?)reminder.MetadataJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger?.LogInformation("Created reminder {Id} for ticket #{TicketId}", 
            reminder.Id, reminder.TicketId);

        return reminder;
    }

    public async Task<bool> MarkFiredAsync(string projectSlug, string reminderId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE WorkflowReminders 
            SET LastFiredAt = $now, FireCount = FireCount + 1, NextFireAt = $nextFire,
                Status = CASE WHEN MaxFires IS NOT NULL AND FireCount + 1 >= MaxFires THEN 'expired' ELSE Status END
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", reminderId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$nextFire", DBNull.Value); // TODO: calculate next fire time
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> CancelAsync(string projectSlug, string reminderId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE WorkflowReminders SET Status = 'cancelled' WHERE Id = $id AND ProjectSlug = $project";
        cmd.Parameters.AddWithValue("$id", reminderId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<WorkflowReminder>> DueRemindersAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM WorkflowReminders 
            WHERE ProjectSlug = $project AND Status = 'active' 
            AND (NextFireAt IS NULL OR NextFireAt <= $now)
            ORDER BY NextFireAt ASC
        """;
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WorkflowReminder>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadReminder(reader));
        return results;
    }

    public async Task<IReadOnlyList<WorkflowReminder>> ForTicketAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM WorkflowReminders WHERE ProjectSlug = $project AND TicketId = $ticket ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$ticket", ticketId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WorkflowReminder>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadReminder(reader));
        return results;
    }

    public async Task<IReadOnlyList<WorkflowReminder>> ActiveForAgentAsync(string projectSlug, string agentId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM WorkflowReminders WHERE ProjectSlug = $project AND AgentId = $agent AND Status = 'active' ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$agent", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WorkflowReminder>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadReminder(reader));
        return results;
    }

    private static WorkflowReminder ReadReminder(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        TicketId = r.IsDBNull(r.GetOrdinal("TicketId")) ? null : r.GetInt32(r.GetOrdinal("TicketId")),
        RunId = r.IsDBNull(r.GetOrdinal("RunId")) ? null : r.GetString(r.GetOrdinal("RunId")),
        AgentId = r.IsDBNull(r.GetOrdinal("AgentId")) ? null : r.GetString(r.GetOrdinal("AgentId")),
        SessionId = r.IsDBNull(r.GetOrdinal("SessionId")) ? null : r.GetString(r.GetOrdinal("SessionId")),
        ReminderType = r.GetString(r.GetOrdinal("ReminderType")),
        ScheduleType = r.GetString(r.GetOrdinal("ScheduleType")),
        DueAt = r.IsDBNull(r.GetOrdinal("DueAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("DueAt"))),
        IntervalTicks = r.IsDBNull(r.GetOrdinal("IntervalTicks")) ? null : TimeSpan.FromTicks(r.GetInt64(r.GetOrdinal("IntervalTicks"))),
        ActionType = r.GetString(r.GetOrdinal("ActionType")),
        ActionPayloadJson = r.IsDBNull(r.GetOrdinal("ActionPayloadJson")) ? null : r.GetString(r.GetOrdinal("ActionPayloadJson")),
        Prompt = r.IsDBNull(r.GetOrdinal("Prompt")) ? null : r.GetString(r.GetOrdinal("Prompt")),
        Status = r.GetString(r.GetOrdinal("Status")),
        CreatedBy = r.IsDBNull(r.GetOrdinal("CreatedBy")) ? null : r.GetString(r.GetOrdinal("CreatedBy")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        LastFiredAt = r.IsDBNull(r.GetOrdinal("LastFiredAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastFiredAt"))),
        NextFireAt = r.IsDBNull(r.GetOrdinal("NextFireAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("NextFireAt"))),
        FireCount = r.GetInt32(r.GetOrdinal("FireCount")),
        MaxFires = r.IsDBNull(r.GetOrdinal("MaxFires")) ? null : r.GetInt32(r.GetOrdinal("MaxFires")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        MetadataJson = r.IsDBNull(r.GetOrdinal("MetadataJson")) ? null : r.GetString(r.GetOrdinal("MetadataJson"))
    };
}
