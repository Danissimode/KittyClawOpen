using KittyClaw.Core.Automation.TeamRoles;
using Microsoft.AspNetCore.Mvc;

namespace KittyClaw.Web.Api;

public static class EndpointsTeamRoles
{
    public static void MapTeamRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/team").WithTags("Team Roles");

        // ── Roles ───────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/roles", async (string slug, TeamRoleStore store) =>
        {
            var roles = await store.GetRolesAsync(slug);
            return Results.Ok(roles);
        })
        .WithName("ListRoles")
        .WithDescription("List all team roles");

        group.MapGet("/projects/{slug}/roles/{roleId}", async (string slug, string roleId, TeamRoleStore store) =>
        {
            var roles = await store.GetRolesAsync(slug);
            var role = roles.FirstOrDefault(r => r.Id == roleId || r.Slug == roleId);
            return role is not null ? Results.Ok(role) : Results.NotFound();
        })
        .WithName("GetRole")
        .WithDescription("Get a specific role");

        group.MapPut("/projects/{slug}/roles/{roleId}", async (
            string slug, string roleId, TeamRole role, TeamRoleStore store) =>
        {
            var result = await store.UpsertRoleAsync(role with { Id = roleId, ProjectSlug = slug });
            return Results.Ok(result);
        })
        .WithName("UpsertRole")
        .WithDescription("Create or update a role");

        // ── Agents ──────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/agents", async (string slug, TeamRoleStore store) =>
        {
            var agents = await store.GetAgentsAsync(slug);
            return Results.Ok(agents);
        })
        .WithName("ListAgents")
        .WithDescription("List all agent profiles");

        group.MapGet("/projects/{slug}/agents/role/{roleId}", async (string slug, string roleId, TeamRoleStore store) =>
        {
            var agents = await store.GetAgentsByRoleAsync(slug, roleId);
            return Results.Ok(agents);
        })
        .WithName("ListAgentsByRole")
        .WithDescription("List agents for a specific role");

        group.MapPut("/projects/{slug}/agents/{agentId}", async (
            string slug, string agentId, AgentProfile agent, TeamRoleStore store) =>
        {
            var result = await store.UpsertAgentAsync(agent with { Id = agentId, ProjectSlug = slug });
            return Results.Ok(result);
        })
        .WithName("UpsertAgent")
        .WithDescription("Create or update an agent profile");

        // ── Execution Profiles ──────────────────────────────────────────
        group.MapGet("/projects/{slug}/execution-profiles", async (string slug, TeamRoleStore store) =>
        {
            // TODO: Implement list
            return Results.Ok(new List<ExecutionProfile>());
        })
        .WithName("ListExecutionProfiles")
        .WithDescription("List all execution profiles");

        group.MapPut("/projects/{slug}/execution-profiles/{profileId}", async (
            string slug, string profileId, ExecutionProfile profile, TeamRoleStore store) =>
        {
            var result = await store.UpsertExecutionProfileAsync(profile with { Id = profileId, ProjectSlug = slug });
            return Results.Ok(result);
        })
        .WithName("UpsertExecutionProfile")
        .WithDescription("Create or update an execution profile");

        // ── Ticket Assignments ──────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{ticketId}/role", async (
            string slug, int ticketId, TeamRoleStore store) =>
        {
            var assignment = await store.GetAssignmentAsync(slug, ticketId);
            return assignment is not null ? Results.Ok(assignment) : Results.Ok(new { });
        })
        .WithName("GetTicketRoleAssignment")
        .WithDescription("Get role assignment for a ticket");

        group.MapPut("/projects/{slug}/tickets/{ticketId}/role", async (
            string slug, int ticketId, TicketRoleAssignment assignment, TeamRoleStore store) =>
        {
            assignment.TicketId = ticketId;
            assignment.ProjectSlug = slug;
            await store.UpsertAssignmentAsync(assignment);
            return Results.Ok(new { assigned = true });
        })
        .WithName("SetTicketRoleAssignment")
        .WithDescription("Set role assignment for a ticket");

        // ── Policies ────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/roles/{roleId}/policies", async (
            string slug, string roleId, TeamRoleStore store) =>
        {
            var policies = await store.GetPoliciesAsync(slug, roleId);
            return Results.Ok(policies);
        })
        .WithName("GetRolePolicies")
        .WithDescription("Get policies for a role");

        group.MapPut("/projects/{slug}/roles/{roleId}/policies/{policyId}", async (
            string slug, string roleId, string policyId, RolePolicy policy, TeamRoleStore store) =>
        {
            var result = await store.UpsertPolicyAsync(policy with { Id = policyId, ProjectSlug = slug, RoleId = roleId });
            return Results.Ok(result);
        })
        .WithName("UpsertRolePolicy")
        .WithDescription("Create or update a role policy");

        // ── Role Inboxes ────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/inboxes", async (string slug, RoleInboxStore store) =>
        {
            var inboxes = await store.GetInboxesAsync(slug);
            return Results.Ok(inboxes);
        })
        .WithName("ListInboxes")
        .WithDescription("List all role inboxes");

        group.MapGet("/projects/{slug}/inboxes/{inboxId}/pending", async (
            string slug, string inboxId, RoleInboxStore store) =>
        {
            var messages = await store.PendingMessagesAsync(slug, inboxId);
            return Results.Ok(messages);
        })
        .WithName("GetPendingMessages")
        .WithDescription("Get pending messages in an inbox");

        group.MapPost("/projects/{slug}/inboxes/{inboxId}/post", async (
            string slug, string inboxId,
            [FromBody] PostMessageRequest request,
            RoleInboxStore store) =>
        {
            var message = await store.PostMessageAsync(new InboxMessage
            {
                ProjectSlug = slug,
                RoleInboxId = inboxId,
                TicketId = request.TicketId,
                Text = request.Text,
                PostedBy = request.PostedBy ?? "owner",
                RequiredSkillsJson = request.RequiredSkillsJson
            });
            return Results.Ok(message);
        })
        .WithName("PostToInbox")
        .WithDescription("Post a message to a role inbox");

        group.MapPost("/projects/{slug}/inboxes/messages/{messageId}/claim", async (
            string slug, string messageId,
            [FromBody] ClaimMessageRequest request,
            RoleInboxStore store) =>
        {
            var result = await store.ClaimMessageAsync(slug, messageId, request.AgentId);
            return result ? Results.Ok(new { claimed = true }) : Results.NotFound();
        })
        .WithName("ClaimMessage")
        .WithDescription("Agent claims a message from inbox");

        // ── Sessions ────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/sessions/active", async (string slug, RoleInboxStore store) =>
        {
            var sessions = await store.ActiveSessionsAsync(slug);
            return Results.Ok(sessions);
        })
        .WithName("GetActiveSessions")
        .WithDescription("Get all active member sessions");

        group.MapGet("/projects/{slug}/tickets/{ticketId}/sessions", async (
            string slug, int ticketId, RoleInboxStore store) =>
        {
            var sessions = await store.SessionsForTicketAsync(slug, ticketId);
            return Results.Ok(sessions);
        })
        .WithName("GetSessionsForTicket")
        .WithDescription("Get all sessions for a ticket");

        group.MapPost("/projects/{slug}/sessions", async (
            string slug,
            [FromBody] CreateSessionRequest request,
            RoleInboxStore store) =>
        {
            var session = await store.CreateSessionAsync(new TeamMemberSession
            {
                ProjectSlug = slug,
                RoleId = request.RoleId,
                AgentProfileId = request.AgentProfileId,
                TicketId = request.TicketId,
                RunId = request.RunId,
                OpencodeSessionId = request.OpencodeSessionId,
                ExecutionProfileId = request.ExecutionProfileId
            });
            return Results.Ok(session);
        })
        .WithName("CreateSession")
        .WithDescription("Create a new member session");

        group.MapPut("/projects/{slug}/sessions/{sessionId}/state", async (
            string slug, string sessionId,
            [FromBody] UpdateStateRequest request,
            RoleInboxStore store) =>
        {
            var result = await store.UpdateSessionStateAsync(slug, sessionId, request.State);
            return result ? Results.Ok(new { updated = true }) : Results.NotFound();
        })
        .WithName("UpdateSessionState")
        .WithDescription("Update session state");

        // ── Conversation Policy ─────────────────────────────────────────
        group.MapGet("/projects/{slug}/conversation-policy", async (string slug, RoleInboxStore store) =>
        {
            var policy = await store.GetPolicyAsync(slug);
            return policy is not null ? Results.Ok(policy) : Results.Ok(new ConversationPolicy { ProjectSlug = slug });
        })
        .WithName("GetConversationPolicy")
        .WithDescription("Get conversation policy for project");

        group.MapPut("/projects/{slug}/conversation-policy", async (
            string slug,
            [FromBody] ConversationPolicy policy,
            RoleInboxStore store) =>
        {
            policy.ProjectSlug = slug;
            var result = await store.UpsertPolicyAsync(policy);
            return Results.Ok(result);
        })
        .WithName("UpdateConversationPolicy")
        .WithDescription("Update conversation policy");

        // ── Message Routing ─────────────────────────────────────────────
        group.MapPost("/projects/{slug}/messages/route", async (
            string slug,
            [FromBody] RouteMessageRequest request,
            MessageRouter router) =>
        {
            var routed = await router.RouteUserMessageAsync(slug, request.Text, request.UserId);
            return Results.Ok(routed);
        })
        .WithName("RouteMessage")
        .WithDescription("Route a user message through the communication system");

        group.MapGet("/projects/{slug}/activity", async (
            string slug,
            string? visibility,
            int limit,
            RoleInboxStore store) =>
        {
            // TODO: Implement activity feed query
            return Results.Ok(new List<object>());
        })
        .WithName("GetActivity")
        .WithDescription("Get team activity feed");

        group.MapGet("/projects/{slug}/sessions/{sessionId}/messages", async (
            string slug, string sessionId, RoleInboxStore store) =>
        {
            // TODO: Implement session messages query
            return Results.Ok(new List<object>());
        })
        .WithName("GetSessionMessages")
        .WithDescription("Get messages for a specific session");
    }

    public record SendCommandRequest(string Text, string? UserId);
    public record ApproveRequest(string ApprovedBy);
    public record RejectRequest(string? Reason);
    public record PostMessageRequest(int TicketId, string Text, string? PostedBy, string? RequiredSkillsJson);
    public record ClaimMessageRequest(string AgentId);
    public record CreateSessionRequest(
        string RoleId,
        string AgentProfileId,
        int TicketId,
        string? RunId,
        string? OpencodeSessionId,
        string? ExecutionProfileId
    );
    public record UpdateStateRequest(string State);
    public record RouteMessageRequest(string Text, string? UserId);
}
