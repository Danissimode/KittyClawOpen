using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.TeamRoles;

/// <summary>
/// SQLite-backed store for Role Inbox, Inbox Messages, and Assignment Claims.
/// </summary>
public sealed class RoleInboxStore
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;

    public RoleInboxStore(string dataDir, ILogger? logger = null)
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
            CREATE TABLE IF NOT EXISTS RoleInboxes (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                RoleId TEXT NOT NULL,
                Name TEXT NOT NULL,
                ChatAddress TEXT NOT NULL,
                BaseSkillsJson TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RoleInboxes_Project ON RoleInboxes (ProjectSlug);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_RoleInboxes_Role ON RoleInboxes (ProjectSlug, RoleId);
            
            CREATE TABLE IF NOT EXISTS InboxMessages (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                RoleInboxId TEXT NOT NULL,
                TicketId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                PostedBy TEXT NOT NULL,
                RequiredSkillsJson TEXT,
                Status TEXT NOT NULL DEFAULT 'pending',
                ClaimedByAgentId TEXT,
                ClaimedAt TEXT,
                ExpiresAt TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_InboxMessages_Inbox ON InboxMessages (RoleInboxId);
            CREATE INDEX IF NOT EXISTS IX_InboxMessages_Status ON InboxMessages (Status);
            CREATE INDEX IF NOT EXISTS IX_InboxMessages_Ticket ON InboxMessages (TicketId);
            
            CREATE TABLE IF NOT EXISTS AssignmentClaims (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                RoleInboxId TEXT NOT NULL,
                AgentProfileId TEXT NOT NULL,
                TicketId INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'pending',
                SelectionReason TEXT,
                SessionId TEXT,
                ClaimedAt TEXT NOT NULL,
                CompletedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_AssignmentClaims_Inbox ON AssignmentClaims (RoleInboxId);
            CREATE INDEX IF NOT EXISTS IX_AssignmentClaims_Agent ON AssignmentClaims (AgentProfileId);
            CREATE INDEX IF NOT EXISTS IX_AssignmentClaims_Ticket ON AssignmentClaims (TicketId);
            CREATE INDEX IF NOT EXISTS IX_AssignmentClaims_Status ON AssignmentClaims (Status);
            
            CREATE TABLE IF NOT EXISTS TeamMemberSessions (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                RoleId TEXT NOT NULL,
                AgentProfileId TEXT NOT NULL,
                TicketId INTEGER NOT NULL,
                RunId TEXT,
                OpencodeSessionId TEXT,
                ExecutionProfileId TEXT,
                State TEXT NOT NULL DEFAULT 'joined',
                StatusMessage TEXT,
                JoinedAt TEXT NOT NULL,
                LastActivityAt TEXT,
                StartedRunAt TEXT,
                CompletedAt TEXT,
                LeftAt TEXT,
                Summary TEXT,
                EvidenceJson TEXT,
                ExitStatus TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_TeamMemberSessions_Project ON TeamMemberSessions (ProjectSlug);
            CREATE INDEX IF NOT EXISTS IX_TeamMemberSessions_Agent ON TeamMemberSessions (AgentProfileId);
            CREATE INDEX IF NOT EXISTS IX_TeamMemberSessions_Ticket ON TeamMemberSessions (TicketId);
            CREATE INDEX IF NOT EXISTS IX_TeamMemberSessions_State ON TeamMemberSessions (State);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Inboxes ────────────────────────────────────────────────────────

    public async Task<RoleInbox> UpsertInboxAsync(RoleInbox inbox, CancellationToken ct = default)
    {
        var dbPath = DbPath(inbox.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO RoleInboxes 
            (Id, ProjectSlug, RoleId, Name, ChatAddress, BaseSkillsJson, Enabled, CreatedAt)
            VALUES ($id, $project, $roleId, $name, $chatAddress, $skills, $enabled, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", inbox.Id);
        cmd.Parameters.AddWithValue("$project", inbox.ProjectSlug);
        cmd.Parameters.AddWithValue("$roleId", inbox.RoleId);
        cmd.Parameters.AddWithValue("$name", inbox.Name);
        cmd.Parameters.AddWithValue("$chatAddress", inbox.ChatAddress);
        cmd.Parameters.AddWithValue("$skills", (object?)inbox.BaseSkillsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", inbox.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", inbox.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return inbox;
    }

    public async Task<List<RoleInbox>> GetInboxesAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RoleInboxes WHERE ProjectSlug = $project AND Enabled = 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RoleInbox>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadInbox(reader));
        return results;
    }

    // ── Messages ───────────────────────────────────────────────────────

    public async Task<InboxMessage> PostMessageAsync(InboxMessage message, CancellationToken ct = default)
    {
        var dbPath = DbPath(message.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO InboxMessages 
            (Id, ProjectSlug, RoleInboxId, TicketId, Text, PostedBy, RequiredSkillsJson, Status, ExpiresAt, CreatedAt)
            VALUES ($id, $project, $inbox, $ticket, $text, $postedBy, $skills, $status, $expiresAt, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", message.Id);
        cmd.Parameters.AddWithValue("$project", message.ProjectSlug);
        cmd.Parameters.AddWithValue("$inbox", message.RoleInboxId);
        cmd.Parameters.AddWithValue("$ticket", message.TicketId);
        cmd.Parameters.AddWithValue("$text", message.Text);
        cmd.Parameters.AddWithValue("$postedBy", message.PostedBy);
        cmd.Parameters.AddWithValue("$skills", (object?)message.RequiredSkillsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", message.Status);
        cmd.Parameters.AddWithValue("$expiresAt", (object?)message.ExpiresAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return message;
    }

    public async Task<List<InboxMessage>> PendingMessagesAsync(string projectSlug, string inboxId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM InboxMessages 
            WHERE ProjectSlug = $project AND RoleInboxId = $inbox AND Status = 'pending'
            AND (ExpiresAt IS NULL OR ExpiresAt > $now)
            ORDER BY CreatedAt ASC
        """;
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$inbox", inboxId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<InboxMessage>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadMessage(reader));
        return results;
    }

    public async Task<bool> ClaimMessageAsync(string projectSlug, string messageId, string agentId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE InboxMessages 
            SET Status = 'claimed', ClaimedByAgentId = $agentId, ClaimedAt = $now 
            WHERE Id = $id AND ProjectSlug = $project AND Status = 'pending'
        """;
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    // ── Claims ─────────────────────────────────────────────────────────

    public async Task<AssignmentClaim> CreateClaimAsync(AssignmentClaim claim, CancellationToken ct = default)
    {
        var dbPath = DbPath(claim.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AssignmentClaims 
            (Id, ProjectSlug, RoleInboxId, AgentProfileId, TicketId, Status, SelectionReason, ClaimedAt)
            VALUES ($id, $project, $inbox, $agent, $ticket, $status, $reason, $claimedAt)
        """;
        cmd.Parameters.AddWithValue("$id", claim.Id);
        cmd.Parameters.AddWithValue("$project", claim.ProjectSlug);
        cmd.Parameters.AddWithValue("$inbox", claim.RoleInboxId);
        cmd.Parameters.AddWithValue("$agent", claim.AgentProfileId);
        cmd.Parameters.AddWithValue("$ticket", claim.TicketId);
        cmd.Parameters.AddWithValue("$status", claim.Status);
        cmd.Parameters.AddWithValue("$reason", (object?)claim.SelectionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$claimedAt", claim.ClaimedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return claim;
    }

    // ── Sessions ───────────────────────────────────────────────────────

    public async Task<TeamMemberSession> CreateSessionAsync(TeamMemberSession session, CancellationToken ct = default)
    {
        var dbPath = DbPath(session.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TeamMemberSessions 
            (Id, ProjectSlug, RoleId, AgentProfileId, TicketId, RunId, OpencodeSessionId, ExecutionProfileId, 
             State, StatusMessage, JoinedAt)
            VALUES ($id, $project, $roleId, $agentId, $ticketId, $runId, $ocSessionId, $execProfileId,
                    $state, $statusMsg, $joinedAt)
        """;
        cmd.Parameters.AddWithValue("$id", session.Id);
        cmd.Parameters.AddWithValue("$project", session.ProjectSlug);
        cmd.Parameters.AddWithValue("$roleId", session.RoleId);
        cmd.Parameters.AddWithValue("$agentId", session.AgentProfileId);
        cmd.Parameters.AddWithValue("$ticketId", session.TicketId);
        cmd.Parameters.AddWithValue("$runId", (object?)session.RunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ocSessionId", (object?)session.OpencodeSessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$execProfileId", (object?)session.ExecutionProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", session.State);
        cmd.Parameters.AddWithValue("$statusMsg", (object?)session.StatusMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$joinedAt", session.JoinedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return session;
    }

    public async Task<bool> UpdateSessionStateAsync(string projectSlug, string sessionId, string state, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE TeamMemberSessions 
            SET State = $state, LastActivityAt = $now 
            WHERE Id = $id AND ProjectSlug = $project
        """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<List<TeamMemberSession>> ActiveSessionsAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM TeamMemberSessions 
            WHERE ProjectSlug = $project AND State IN ('joined', 'assigned', 'running', 'waiting', 'blocked')
            ORDER BY JoinedAt DESC
        """;
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TeamMemberSession>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSession(reader));
        return results;
    }

    public async Task<List<TeamMemberSession>> SessionsForTicketAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TeamMemberSessions WHERE ProjectSlug = $project AND TicketId = $ticket ORDER BY JoinedAt DESC";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$ticket", ticketId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TeamMemberSession>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSession(reader));
        return results;
    }

    // ── Read helpers ───────────────────────────────────────────────────

    private static RoleInbox ReadInbox(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        RoleId = r.GetString(r.GetOrdinal("RoleId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        ChatAddress = r.GetString(r.GetOrdinal("ChatAddress")),
        BaseSkillsJson = r.IsDBNull(r.GetOrdinal("BaseSkillsJson")) ? null : r.GetString(r.GetOrdinal("BaseSkillsJson")),
        Enabled = r.GetInt32(r.GetOrdinal("Enabled")) == 1,
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };

    private static InboxMessage ReadMessage(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        RoleInboxId = r.GetString(r.GetOrdinal("RoleInboxId")),
        TicketId = r.GetInt32(r.GetOrdinal("TicketId")),
        Text = r.GetString(r.GetOrdinal("Text")),
        PostedBy = r.GetString(r.GetOrdinal("PostedBy")),
        RequiredSkillsJson = r.IsDBNull(r.GetOrdinal("RequiredSkillsJson")) ? null : r.GetString(r.GetOrdinal("RequiredSkillsJson")),
        Status = r.GetString(r.GetOrdinal("Status")),
        ClaimedByAgentId = r.IsDBNull(r.GetOrdinal("ClaimedByAgentId")) ? null : r.GetString(r.GetOrdinal("ClaimedByAgentId")),
        ClaimedAt = r.IsDBNull(r.GetOrdinal("ClaimedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ClaimedAt"))),
        ExpiresAt = r.IsDBNull(r.GetOrdinal("ExpiresAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ExpiresAt"))),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };

    private static TeamMemberSession ReadSession(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        RoleId = r.GetString(r.GetOrdinal("RoleId")),
        AgentProfileId = r.GetString(r.GetOrdinal("AgentProfileId")),
        TicketId = r.GetInt32(r.GetOrdinal("TicketId")),
        RunId = r.IsDBNull(r.GetOrdinal("RunId")) ? null : r.GetString(r.GetOrdinal("RunId")),
        OpencodeSessionId = r.IsDBNull(r.GetOrdinal("OpencodeSessionId")) ? null : r.GetString(r.GetOrdinal("OpencodeSessionId")),
        ExecutionProfileId = r.IsDBNull(r.GetOrdinal("ExecutionProfileId")) ? null : r.GetString(r.GetOrdinal("ExecutionProfileId")),
        State = r.GetString(r.GetOrdinal("State")),
        StatusMessage = r.IsDBNull(r.GetOrdinal("StatusMessage")) ? null : r.GetString(r.GetOrdinal("StatusMessage")),
        JoinedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("JoinedAt"))),
        LastActivityAt = r.IsDBNull(r.GetOrdinal("LastActivityAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastActivityAt"))),
        StartedRunAt = r.IsDBNull(r.GetOrdinal("StartedRunAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("StartedRunAt"))),
        CompletedAt = r.IsDBNull(r.GetOrdinal("CompletedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CompletedAt"))),
        LeftAt = r.IsDBNull(r.GetOrdinal("LeftAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LeftAt"))),
        Summary = r.IsDBNull(r.GetOrdinal("Summary")) ? null : r.GetString(r.GetOrdinal("Summary")),
        EvidenceJson = r.IsDBNull(r.GetOrdinal("EvidenceJson")) ? null : r.GetString(r.GetOrdinal("EvidenceJson")),
        ExitStatus = r.IsDBNull(r.GetOrdinal("ExitStatus")) ? null : r.GetString(r.GetOrdinal("ExitStatus"))
    };

    // ── Conversation Policy ────────────────────────────────────────────

    public async Task<ConversationPolicy?> GetPolicyAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsurePolicyTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ConversationPolicies WHERE ProjectSlug = $project LIMIT 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPolicy(reader) : null;
    }

    public async Task<ConversationPolicy> UpsertPolicyAsync(ConversationPolicy policy, CancellationToken ct = default)
    {
        var dbPath = DbPath(policy.ProjectSlug);
        await EnsurePolicyTableAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO ConversationPolicies 
            (Id, ProjectSlug, ReplyPolicy, AllowDirectAgentMentions, AutoSummarizeRoleResponses, 
             ShowCriticalEventsInMain, AlwaysVisibleRolesJson, UpdatedAt)
            VALUES ($id, $project, $replyPolicy, $allowDirect, $autoSummarize, 
                    $showCritical, $alwaysVisible, $updatedAt)
        """;
        cmd.Parameters.AddWithValue("$id", policy.Id);
        cmd.Parameters.AddWithValue("$project", policy.ProjectSlug);
        cmd.Parameters.AddWithValue("$replyPolicy", policy.ReplyPolicy.ToString());
        cmd.Parameters.AddWithValue("$allowDirect", policy.AllowDirectAgentMentions ? 1 : 0);
        cmd.Parameters.AddWithValue("$autoSummarize", policy.AutoSummarizeRoleResponses ? 1 : 0);
        cmd.Parameters.AddWithValue("$showCritical", policy.ShowCriticalEventsInMain ? 1 : 0);
        cmd.Parameters.AddWithValue("$alwaysVisible", (object?)policy.AlwaysVisibleRolesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updatedAt", policy.UpdatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return policy;
    }

    private async Task EnsurePolicyTableAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ConversationPolicies (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL UNIQUE,
                ReplyPolicy TEXT NOT NULL DEFAULT 'mediated_roles',
                AllowDirectAgentMentions INTEGER NOT NULL DEFAULT 0,
                AutoSummarizeRoleResponses INTEGER NOT NULL DEFAULT 1,
                ShowCriticalEventsInMain INTEGER NOT NULL DEFAULT 1,
                AlwaysVisibleRolesJson TEXT,
                UpdatedAt TEXT NOT NULL
            );
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static ConversationPolicy ReadPolicy(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        ReplyPolicy = Enum.Parse<ReplyPolicy>(r.GetString(r.GetOrdinal("ReplyPolicy")), true),
        AllowDirectAgentMentions = r.GetInt32(r.GetOrdinal("AllowDirectAgentMentions")) == 1,
        AutoSummarizeRoleResponses = r.GetInt32(r.GetOrdinal("AutoSummarizeRoleResponses")) == 1,
        ShowCriticalEventsInMain = r.GetInt32(r.GetOrdinal("ShowCriticalEventsInMain")) == 1,
        AlwaysVisibleRolesJson = r.IsDBNull(r.GetOrdinal("AlwaysVisibleRolesJson")) ? null : r.GetString(r.GetOrdinal("AlwaysVisibleRolesJson")),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("UpdatedAt")))
    };
}
