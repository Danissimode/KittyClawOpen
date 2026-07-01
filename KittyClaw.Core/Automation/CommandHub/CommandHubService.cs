using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.CommandHub;

/// <summary>
/// Service for the Command Dialogue Hub.
/// Manages conversations, messages, intents, and command plans.
/// </summary>
public sealed class CommandHubService
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;

    public CommandHubService(string dataDir, ILogger? logger = null)
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

    public static async Task EnsureTablesAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CommandConversations (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                Source TEXT NOT NULL,
                ExternalThreadId TEXT,
                ExternalUserId TEXT,
                Status TEXT NOT NULL DEFAULT 'open',
                CreatedAt TEXT NOT NULL,
                LastMessageAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_CmdConv_Project ON CommandConversations (ProjectSlug);
            
            CREATE TABLE IF NOT EXISTS CommandMessages (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                ConversationId TEXT NOT NULL,
                Source TEXT NOT NULL,
                ExternalMessageId TEXT,
                UserId TEXT NOT NULL,
                Text TEXT NOT NULL,
                TargetAgent TEXT,
                IntentType TEXT,
                Role TEXT NOT NULL DEFAULT 'user',
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CmdMsg_Conversation ON CommandMessages (ConversationId);
            
            CREATE TABLE IF NOT EXISTS CommandPlans (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                ConversationId TEXT NOT NULL,
                MessageId TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Description TEXT,
                Risk TEXT NOT NULL DEFAULT 'low',
                Status TEXT NOT NULL DEFAULT 'pending_approval',
                ActionsJson TEXT NOT NULL,
                CreatedBy TEXT NOT NULL,
                ApprovedBy TEXT,
                RejectionReason TEXT,
                ExpiresAt TEXT,
                CreatedAt TEXT NOT NULL,
                ApprovedAt TEXT,
                ExecutedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_CmdPlan_Project ON CommandPlans (ProjectSlug);
            CREATE INDEX IF NOT EXISTS IX_CmdPlan_Status ON CommandPlans (Status);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Conversations ──────────────────────────────────────────────────

    public async Task<CommandConversation> GetOrCreateConversationAsync(
        string projectSlug, string source, string? externalThreadId, string? externalUserId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);

        // Try to find existing open conversation
        if (!string.IsNullOrEmpty(externalThreadId))
        {
            await using var findCmd = conn.CreateCommand();
            findCmd.CommandText = "SELECT * FROM CommandConversations WHERE ProjectSlug = $project AND Source = $source AND ExternalThreadId = $threadId AND Status = 'open' LIMIT 1";
            findCmd.Parameters.AddWithValue("$project", projectSlug);
            findCmd.Parameters.AddWithValue("$source", source);
            findCmd.Parameters.AddWithValue("$threadId", externalThreadId);
            
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return ReadConversation(reader);
            }
        }

        // Create new conversation
        var conversation = new CommandConversation
        {
            ProjectSlug = projectSlug,
            Source = source,
            ExternalThreadId = externalThreadId,
            ExternalUserId = externalUserId
        };

        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO CommandConversations (Id, ProjectSlug, Source, ExternalThreadId, ExternalUserId, Status, CreatedAt)
            VALUES ($id, $project, $source, $threadId, $userId, $status, $createdAt)
        """;
        insertCmd.Parameters.AddWithValue("$id", conversation.Id);
        insertCmd.Parameters.AddWithValue("$project", projectSlug);
        insertCmd.Parameters.AddWithValue("$source", source);
        insertCmd.Parameters.AddWithValue("$threadId", (object?)externalThreadId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$userId", (object?)externalUserId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$status", conversation.Status);
        insertCmd.Parameters.AddWithValue("$createdAt", conversation.CreatedAt.ToString("o"));
        await insertCmd.ExecuteNonQueryAsync(ct);

        return conversation;
    }

    // ── Messages ───────────────────────────────────────────────────────

    public async Task<CommandMessage> SaveMessageAsync(CommandMessage message, CancellationToken ct = default)
    {
        var dbPath = DbPath(message.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CommandMessages (Id, ProjectSlug, ConversationId, Source, ExternalMessageId, UserId, Text, TargetAgent, IntentType, Role, CreatedAt)
            VALUES ($id, $project, $conversation, $source, $extMsgId, $userId, $text, $targetAgent, $intentType, $role, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", message.Id);
        cmd.Parameters.AddWithValue("$project", message.ProjectSlug);
        cmd.Parameters.AddWithValue("$conversation", message.ConversationId);
        cmd.Parameters.AddWithValue("$source", message.Source);
        cmd.Parameters.AddWithValue("$extMsgId", (object?)message.ExternalMessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$userId", message.UserId);
        cmd.Parameters.AddWithValue("$text", message.Text);
        cmd.Parameters.AddWithValue("$targetAgent", (object?)message.TargetAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$intentType", (object?)message.IntentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$role", message.Role);
        cmd.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return message;
    }

    // ── Plans ──────────────────────────────────────────────────────────

    public async Task<CommandPlan> CreatePlanAsync(CommandPlan plan, CancellationToken ct = default)
    {
        var dbPath = DbPath(plan.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CommandPlans (Id, ProjectSlug, ConversationId, MessageId, Summary, Description, Risk, Status, ActionsJson, CreatedBy, ExpiresAt, CreatedAt)
            VALUES ($id, $project, $conversation, $message, $summary, $desc, $risk, $status, $actions, $createdBy, $expiresAt, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", plan.Id);
        cmd.Parameters.AddWithValue("$project", plan.ProjectSlug);
        cmd.Parameters.AddWithValue("$conversation", plan.ConversationId);
        cmd.Parameters.AddWithValue("$message", plan.MessageId);
        cmd.Parameters.AddWithValue("$summary", plan.Summary);
        cmd.Parameters.AddWithValue("$desc", (object?)plan.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$risk", plan.Risk);
        cmd.Parameters.AddWithValue("$status", plan.Status);
        cmd.Parameters.AddWithValue("$actions", plan.ActionsJson);
        cmd.Parameters.AddWithValue("$createdBy", plan.CreatedBy);
        cmd.Parameters.AddWithValue("$expiresAt", (object?)plan.ExpiresAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", plan.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger?.LogInformation("Created command plan {Id}: {Summary}", plan.Id, plan.Summary);
        return plan;
    }

    public async Task<bool> ApprovePlanAsync(string projectSlug, string planId, string approvedBy, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE CommandPlans 
            SET Status = 'approved', ApprovedBy = $approvedBy, ApprovedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project AND Status = 'pending_approval'
        """;
        cmd.Parameters.AddWithValue("$id", planId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$approvedBy", approvedBy);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> RejectPlanAsync(string projectSlug, string planId, string? reason, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE CommandPlans 
            SET Status = 'rejected', RejectionReason = $reason 
            WHERE Id = $id AND ProjectSlug = $project AND Status = 'pending_approval'
        """;
        cmd.Parameters.AddWithValue("$id", planId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<List<CommandPlan>> PendingPlansAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CommandPlans WHERE ProjectSlug = $project AND Status = 'pending_approval' ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CommandPlan>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadPlan(reader));
        return results;
    }

    private static CommandConversation ReadConversation(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        Source = r.GetString(r.GetOrdinal("Source")),
        ExternalThreadId = r.IsDBNull(r.GetOrdinal("ExternalThreadId")) ? null : r.GetString(r.GetOrdinal("ExternalThreadId")),
        ExternalUserId = r.IsDBNull(r.GetOrdinal("ExternalUserId")) ? null : r.GetString(r.GetOrdinal("ExternalUserId")),
        Status = r.GetString(r.GetOrdinal("Status")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        LastMessageAt = r.IsDBNull(r.GetOrdinal("LastMessageAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastMessageAt")))
    };

    private static CommandPlan ReadPlan(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        ConversationId = r.GetString(r.GetOrdinal("ConversationId")),
        MessageId = r.GetString(r.GetOrdinal("MessageId")),
        Summary = r.GetString(r.GetOrdinal("Summary")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        Risk = r.GetString(r.GetOrdinal("Risk")),
        Status = r.GetString(r.GetOrdinal("Status")),
        ActionsJson = r.GetString(r.GetOrdinal("ActionsJson")),
        CreatedBy = r.GetString(r.GetOrdinal("CreatedBy")),
        ApprovedBy = r.IsDBNull(r.GetOrdinal("ApprovedBy")) ? null : r.GetString(r.GetOrdinal("ApprovedBy")),
        RejectionReason = r.IsDBNull(r.GetOrdinal("RejectionReason")) ? null : r.GetString(r.GetOrdinal("RejectionReason")),
        ExpiresAt = r.IsDBNull(r.GetOrdinal("ExpiresAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ExpiresAt"))),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        ApprovedAt = r.IsDBNull(r.GetOrdinal("ApprovedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ApprovedAt"))),
        ExecutedAt = r.IsDBNull(r.GetOrdinal("ExecutedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ExecutedAt")))
    };
}
