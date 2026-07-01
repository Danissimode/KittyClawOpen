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
    }
}
