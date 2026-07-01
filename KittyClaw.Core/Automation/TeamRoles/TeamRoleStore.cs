using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.TeamRoles;

/// <summary>
/// SQLite-backed store for team roles, agent profiles, execution profiles, and policies.
/// </summary>
public sealed class TeamRoleStore
{
    private readonly string _dataDir;
    private readonly ILogger? _logger;

    public TeamRoleStore(string dataDir, ILogger? logger = null)
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
            CREATE TABLE IF NOT EXISTS TeamRoles (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                Slug TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT,
                DefaultExecutionProfileId TEXT,
                CapabilitiesJson TEXT,
                RiskLimit TEXT NOT NULL DEFAULT 'medium',
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TeamRoles_Project ON TeamRoles (ProjectSlug);
            
            CREATE TABLE IF NOT EXISTS AgentProfiles (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                RoleId TEXT NOT NULL,
                ExecutionProfileId TEXT,
                Status TEXT NOT NULL DEFAULT 'idle',
                MaxConcurrentRuns INTEGER NOT NULL DEFAULT 1,
                CurrentRunCount INTEGER NOT NULL DEFAULT 0,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AgentProfiles_Project ON AgentProfiles (ProjectSlug);
            CREATE INDEX IF NOT EXISTS IX_AgentProfiles_Role ON AgentProfiles (RoleId);
            
            CREATE TABLE IF NOT EXISTS ExecutionProfiles (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                Name TEXT NOT NULL,
                Runtime TEXT NOT NULL DEFAULT 'opencode',
                Provider TEXT,
                Model TEXT,
                OpencodeModel TEXT,
                PermissionsJson TEXT,
                WorktreeRequired INTEGER NOT NULL DEFAULT 0,
                MaxTurns INTEGER NOT NULL DEFAULT 20,
                TimeoutMinutes INTEGER NOT NULL DEFAULT 45,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ExecutionProfiles_Project ON ExecutionProfiles (ProjectSlug);
            
            CREATE TABLE IF NOT EXISTS TicketRoleAssignments (
                TicketId INTEGER NOT NULL,
                ProjectSlug TEXT NOT NULL,
                AssignedRoleId TEXT,
                AssignedAgentId TEXT,
                ReviewerRoleId TEXT,
                ReviewerAgentId TEXT,
                ArchitectRequired INTEGER NOT NULL DEFAULT 0,
                ValidatorRequired INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (TicketId, ProjectSlug)
            );
            
            CREATE TABLE IF NOT EXISTS RolePolicies (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectSlug TEXT NOT NULL,
                RoleId TEXT NOT NULL,
                Action TEXT NOT NULL,
                Effect TEXT NOT NULL,
                ConditionJson TEXT,
                Reason TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePolicies_Project_Role ON RolePolicies (ProjectSlug, RoleId);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Roles ──────────────────────────────────────────────────────────

    public async Task<TeamRole> UpsertRoleAsync(TeamRole role, CancellationToken ct = default)
    {
        var dbPath = DbPath(role.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO TeamRoles 
            (Id, ProjectSlug, Slug, Name, Description, DefaultExecutionProfileId, CapabilitiesJson, RiskLimit, Enabled, CreatedAt)
            VALUES ($id, $project, $slug, $name, $desc, $execProfile, $caps, $risk, $enabled, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", role.Id);
        cmd.Parameters.AddWithValue("$project", role.ProjectSlug);
        cmd.Parameters.AddWithValue("$slug", role.Slug);
        cmd.Parameters.AddWithValue("$name", role.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)role.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$execProfile", (object?)role.DefaultExecutionProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$caps", (object?)role.CapabilitiesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$risk", role.RiskLimit);
        cmd.Parameters.AddWithValue("$enabled", role.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", role.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return role;
    }

    public async Task<List<TeamRole>> GetRolesAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TeamRoles WHERE ProjectSlug = $project AND Enabled = 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TeamRole>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadRole(reader));
        return results;
    }

    public async Task<TeamRole?> GetRoleBySlugAsync(string projectSlug, string slug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TeamRoles WHERE ProjectSlug = $project AND Slug = $slug LIMIT 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$slug", slug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRole(reader) : null;
    }

    // ── Agents ─────────────────────────────────────────────────────────

    public async Task<AgentProfile> UpsertAgentAsync(AgentProfile agent, CancellationToken ct = default)
    {
        var dbPath = DbPath(agent.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO AgentProfiles
            (Id, ProjectSlug, DisplayName, RoleId, ExecutionProfileId, Status, MaxConcurrentRuns, CurrentRunCount, Enabled, CreatedAt)
            VALUES ($id, $project, $name, $roleId, $execProfile, $status, $maxConcurrent, $currentRun, $enabled, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", agent.Id);
        cmd.Parameters.AddWithValue("$project", agent.ProjectSlug);
        cmd.Parameters.AddWithValue("$name", agent.DisplayName);
        cmd.Parameters.AddWithValue("$roleId", agent.RoleId);
        cmd.Parameters.AddWithValue("$execProfile", (object?)agent.ExecutionProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", agent.Status);
        cmd.Parameters.AddWithValue("$maxConcurrent", agent.MaxConcurrentRuns);
        cmd.Parameters.AddWithValue("$currentRun", agent.CurrentRunCount);
        cmd.Parameters.AddWithValue("$enabled", agent.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", agent.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return agent;
    }

    public async Task<List<AgentProfile>> GetAgentsAsync(string projectSlug, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentProfiles WHERE ProjectSlug = $project AND Enabled = 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentProfile>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadAgent(reader));
        return results;
    }

    public async Task<List<AgentProfile>> GetAgentsByRoleAsync(string projectSlug, string roleId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentProfiles WHERE ProjectSlug = $project AND RoleId = $roleId AND Enabled = 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$roleId", roleId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentProfile>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadAgent(reader));
        return results;
    }

    // ── Execution Profiles ─────────────────────────────────────────────

    public async Task<ExecutionProfile> UpsertExecutionProfileAsync(ExecutionProfile profile, CancellationToken ct = default)
    {
        var dbPath = DbPath(profile.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO ExecutionProfiles
            (Id, ProjectSlug, Name, Runtime, Provider, Model, OpencodeModel, PermissionsJson, WorktreeRequired, MaxTurns, TimeoutMinutes, Enabled, CreatedAt)
            VALUES ($id, $project, $name, $runtime, $provider, $model, $opencodeModel, $perms, $worktree, $maxTurns, $timeout, $enabled, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", profile.Id);
        cmd.Parameters.AddWithValue("$project", profile.ProjectSlug);
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$runtime", profile.Runtime);
        cmd.Parameters.AddWithValue("$provider", (object?)profile.Provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)profile.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$opencodeModel", (object?)profile.OpencodeModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$perms", (object?)profile.PermissionsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$worktree", profile.WorktreeRequired ? 1 : 0);
        cmd.Parameters.AddWithValue("$maxTurns", profile.MaxTurns);
        cmd.Parameters.AddWithValue("$timeout", profile.TimeoutMinutes);
        cmd.Parameters.AddWithValue("$enabled", profile.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", profile.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return profile;
    }

    // ── Ticket Assignments ─────────────────────────────────────────────

    public async Task<TicketRoleAssignment?> GetAssignmentAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TicketRoleAssignments WHERE ProjectSlug = $project AND TicketId = $ticket";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$ticket", ticketId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAssignment(reader) : null;
    }

    public async Task UpsertAssignmentAsync(TicketRoleAssignment assignment, CancellationToken ct = default)
    {
        var dbPath = DbPath(assignment.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO TicketRoleAssignments
            (TicketId, ProjectSlug, AssignedRoleId, AssignedAgentId, ReviewerRoleId, ReviewerAgentId, ArchitectRequired, ValidatorRequired)
            VALUES ($ticket, $project, $roleId, $agentId, $reviewerRoleId, $reviewerAgentId, $archReq, $valReq)
        """;
        cmd.Parameters.AddWithValue("$ticket", assignment.TicketId);
        cmd.Parameters.AddWithValue("$project", assignment.ProjectSlug);
        cmd.Parameters.AddWithValue("$roleId", (object?)assignment.AssignedRoleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$agentId", (object?)assignment.AssignedAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reviewerRoleId", (object?)assignment.ReviewerRoleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reviewerAgentId", (object?)assignment.ReviewerAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$archReq", assignment.ArchitectRequired ? 1 : 0);
        cmd.Parameters.AddWithValue("$valReq", assignment.ValidatorRequired ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Policies ───────────────────────────────────────────────────────

    public async Task<RolePolicy> UpsertPolicyAsync(RolePolicy policy, CancellationToken ct = default)
    {
        var dbPath = DbPath(policy.ProjectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO RolePolicies
            (Id, ProjectSlug, RoleId, Action, Effect, ConditionJson, Reason, Enabled, CreatedAt)
            VALUES ($id, $project, $roleId, $action, $effect, $condition, $reason, $enabled, $createdAt)
        """;
        cmd.Parameters.AddWithValue("$id", policy.Id);
        cmd.Parameters.AddWithValue("$project", policy.ProjectSlug);
        cmd.Parameters.AddWithValue("$roleId", policy.RoleId);
        cmd.Parameters.AddWithValue("$action", policy.Action);
        cmd.Parameters.AddWithValue("$effect", policy.Effect);
        cmd.Parameters.AddWithValue("$condition", (object?)policy.ConditionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", (object?)policy.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", policy.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", policy.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return policy;
    }

    public async Task<List<RolePolicy>> GetPoliciesAsync(string projectSlug, string roleId, CancellationToken ct = default)
    {
        var dbPath = DbPath(projectSlug);
        await EnsureTablesAsync(dbPath);

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RolePolicies WHERE ProjectSlug = $project AND RoleId = $roleId AND Enabled = 1";
        cmd.Parameters.AddWithValue("$project", projectSlug);
        cmd.Parameters.AddWithValue("$roleId", roleId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RolePolicy>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadPolicy(reader));
        return results;
    }

    // ── Read helpers ───────────────────────────────────────────────────

    private static TeamRole ReadRole(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        Slug = r.GetString(r.GetOrdinal("Slug")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        DefaultExecutionProfileId = r.IsDBNull(r.GetOrdinal("DefaultExecutionProfileId")) ? null : r.GetString(r.GetOrdinal("DefaultExecutionProfileId")),
        CapabilitiesJson = r.IsDBNull(r.GetOrdinal("CapabilitiesJson")) ? null : r.GetString(r.GetOrdinal("CapabilitiesJson")),
        RiskLimit = r.GetString(r.GetOrdinal("RiskLimit")),
        Enabled = r.GetInt32(r.GetOrdinal("Enabled")) == 1,
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };

    private static AgentProfile ReadAgent(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        DisplayName = r.GetString(r.GetOrdinal("DisplayName")),
        RoleId = r.GetString(r.GetOrdinal("RoleId")),
        ExecutionProfileId = r.IsDBNull(r.GetOrdinal("ExecutionProfileId")) ? null : r.GetString(r.GetOrdinal("ExecutionProfileId")),
        Status = r.GetString(r.GetOrdinal("Status")),
        MaxConcurrentRuns = r.GetInt32(r.GetOrdinal("MaxConcurrentRuns")),
        CurrentRunCount = r.GetInt32(r.GetOrdinal("CurrentRunCount")),
        Enabled = r.GetInt32(r.GetOrdinal("Enabled")) == 1,
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };

    private static TicketRoleAssignment ReadAssignment(SqliteDataReader r) => new()
    {
        TicketId = r.GetInt32(r.GetOrdinal("TicketId")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        AssignedRoleId = r.IsDBNull(r.GetOrdinal("AssignedRoleId")) ? null : r.GetString(r.GetOrdinal("AssignedRoleId")),
        AssignedAgentId = r.IsDBNull(r.GetOrdinal("AssignedAgentId")) ? null : r.GetString(r.GetOrdinal("AssignedAgentId")),
        ReviewerRoleId = r.IsDBNull(r.GetOrdinal("ReviewerRoleId")) ? null : r.GetString(r.GetOrdinal("ReviewerRoleId")),
        ReviewerAgentId = r.IsDBNull(r.GetOrdinal("ReviewerAgentId")) ? null : r.GetString(r.GetOrdinal("ReviewerAgentId")),
        ArchitectRequired = r.GetInt32(r.GetOrdinal("ArchitectRequired")) == 1,
        ValidatorRequired = r.GetInt32(r.GetOrdinal("ValidatorRequired")) == 1
    };

    private static RolePolicy ReadPolicy(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        ProjectSlug = r.GetString(r.GetOrdinal("ProjectSlug")),
        RoleId = r.GetString(r.GetOrdinal("RoleId")),
        Action = r.GetString(r.GetOrdinal("Action")),
        Effect = r.GetString(r.GetOrdinal("Effect")),
        ConditionJson = r.IsDBNull(r.GetOrdinal("ConditionJson")) ? null : r.GetString(r.GetOrdinal("ConditionJson")),
        Reason = r.IsDBNull(r.GetOrdinal("Reason")) ? null : r.GetString(r.GetOrdinal("Reason")),
        Enabled = r.GetInt32(r.GetOrdinal("Enabled")) == 1,
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };
}
