using KittyClaw.Core.Models;
using KittyClaw.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace KittyClaw.Web.Api;

public static class EndpointsTree
{
    public static void MapTreeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tree").WithTags("Tree");

        // ── Children ────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{parentId}/children", async (
            string slug, int parentId, TreeService tree) =>
        {
            var children = await tree.GetChildrenAsync(slug, parentId);
            return Results.Ok(children);
        })
        .WithName("GetChildren")
        .WithDescription("Get direct children of a ticket");

        // ── Subtree ─────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{rootId}/subtree", async (
            string slug, int rootId, TreeService tree) =>
        {
            var subtree = await tree.GetSubtreeAsync(slug, rootId);
            return Results.Ok(subtree);
        })
        .WithName("GetSubtree")
        .WithDescription("Get entire subtree rooted at a ticket");

        // ── Progress ────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{parentId}/progress", async (
            string slug, int parentId, TreeService tree) =>
        {
            var progress = await tree.GetProgressAsync(slug, parentId);
            return Results.Ok(progress);
        })
        .WithName("GetTreeProgress")
        .WithDescription("Get child progress for a parent ticket");

        // ── Reparent ────────────────────────────────────────────────────
        group.MapPost("/projects/{slug}/tickets/{ticketId}/reparent", async (
            string slug, int ticketId,
            [FromBody] ReparentRequest request,
            TreeService tree) =>
        {
            // Check for cycle
            if (request.NewParentId.HasValue && await tree.WouldCreateCycleAsync(slug, ticketId, request.NewParentId.Value))
            {
                return Results.BadRequest(new { error = "Reparent would create a cycle" });
            }
            
            var result = await tree.ReparentAsync(slug, ticketId, request.NewParentId);
            return result ? Results.Ok(new { reparented = true }) : Results.NotFound();
        })
        .WithName("ReparentTicket")
        .WithDescription("Reparent a ticket to a new parent");

        // ── Move Subtree ────────────────────────────────────────────────
        group.MapPost("/projects/{slug}/tickets/{rootId}/move-subtree", async (
            string slug, int rootId,
            [FromBody] ReparentRequest request,
            TreeService tree) =>
        {
            var result = await tree.MoveSubtreeAsync(slug, rootId, request.NewParentId);
            return result ? Results.Ok(new { moved = true }) : Results.BadRequest(new { error = "Cannot move subtree (active runs or other constraints)" });
        })
        .WithName("MoveSubtree")
        .WithDescription("Move entire subtree to a new parent");

        // ── Cycle check ─────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{ticketId}/would-cycle/{newParentId}", async (
            string slug, int ticketId, int newParentId, TreeService tree) =>
        {
            var wouldCycle = await tree.WouldCreateCycleAsync(slug, ticketId, newParentId);
            return Results.Ok(new { wouldCycle });
        })
        .WithName("CheckCycle")
        .WithDescription("Check if reparent would create a cycle");

        // ── Dependencies ────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{ticketId}/dependencies", async (
            string slug, int ticketId, DependencyStore store) =>
        {
            var deps = await store.GetDependenciesForAsync(slug, ticketId);
            return Results.Ok(deps);
        })
        .WithName("GetDependencies")
        .WithDescription("Get all dependencies for a ticket");

        group.MapGet("/projects/{slug}/tickets/{ticketId}/blocking", async (
            string slug, int ticketId, DependencyStore store) =>
        {
            var deps = await store.GetBlockingDependenciesAsync(slug, ticketId);
            return Results.Ok(deps);
        })
        .WithName("GetBlockingDependencies")
        .WithDescription("Get dependencies that block this ticket");

        group.MapPost("/projects/{slug}/dependencies", async (
            string slug,
            [FromBody] CreateDependencyRequest request,
            DependencyStore store) =>
        {
            var dep = new TicketDependency
            {
                ProjectSlug = slug,
                FromTicketId = request.FromTicketId,
                ToTicketId = request.ToTicketId,
                DependencyType = request.DependencyType ?? "finish_to_start",
                Scope = request.Scope ?? "same_parent",
                Required = request.Required ?? true
            };
            var result = await store.AddDependencyAsync(dep);
            return Results.Created($"/api/tree/{slug}/dependencies/{result.Id}", result);
        })
        .WithName("CreateDependency")
        .WithDescription("Create a dependency between two tickets");

        // ── Parallel Groups ─────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{parentId}/parallel-groups", async (
            string slug, int parentId, DependencyStore store) =>
        {
            var groups = await store.GetGroupsForParentAsync(slug, parentId);
            return Results.Ok(groups);
        })
        .WithName("GetParallelGroups")
        .WithDescription("Get parallel groups for a parent ticket");

        group.MapPost("/projects/{slug}/parallel-groups", async (
            string slug,
            [FromBody] CreateParallelGroupRequest request,
            DependencyStore store) =>
        {
            var group = new ParallelGroup
            {
                ProjectSlug = slug,
                ParentTicketId = request.ParentTicketId,
                Name = request.Name ?? "Parallel Group",
                JoinPolicy = request.JoinPolicy ?? "all_done",
                MaxConcurrency = request.MaxConcurrency ?? 2,
                OnCompleteTicketId = request.OnCompleteTicketId
            };
            var result = await store.CreateParallelGroupAsync(group);
            return Results.Created($"/api/tree/{slug}/parallel-groups/{result.Id}", result);
        })
        .WithName("CreateParallelGroup")
        .WithDescription("Create a parallel execution group");

        // ── Decomposition ───────────────────────────────────────────────
        group.MapGet("/projects/{slug}/tickets/{ticketId}/can-decompose", async (
            string slug, int ticketId, DecompositionService decomposition) =>
        {
            var (canDecompose, reason) = await decomposition.CanDecomposeAsync(slug, ticketId);
            return Results.Ok(new { canDecompose, reason });
        })
        .WithName("CanDecompose")
        .WithDescription("Check if a ticket can be decomposed");

        group.MapPost("/projects/{slug}/tickets/{ticketId}/decompose/preview", async (
            string slug, int ticketId,
            [FromBody] DecompositionRequest request,
            DecompositionService decomposition) =>
        {
            var preview = await decomposition.PreviewDecompositionAsync(slug, ticketId, request);
            return Results.Ok(preview);
        })
        .WithName("PreviewDecomposition")
        .WithDescription("Preview decomposition without applying");

        group.MapPost("/projects/{slug}/tickets/{ticketId}/decompose/apply", async (
            string slug, int ticketId,
            [FromBody] DecompositionApplyRequest request,
            DecompositionService decomposition) =>
        {
            var preview = await decomposition.PreviewDecompositionAsync(slug, ticketId, request.Request);
            if (!preview.IsValid)
                return Results.BadRequest(new { error = preview.RejectionReason });

            var result = await decomposition.ApplyDecompositionAsync(slug, ticketId, preview, request.DecomposedBy);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ApplyDecomposition")
        .WithDescription("Apply decomposition and create child cards");

        group.MapGet("/projects/{slug}/tickets/{ticketId}/leaves", async (
            string slug, int ticketId, DecompositionService decomposition) =>
        {
            var leaves = await decomposition.GetLeafTasksAsync(slug, ticketId);
            return Results.Ok(leaves);
        })
        .WithName("GetLeafTasks")
        .WithDescription("Get all leaf tasks in subtree");

        group.MapGet("/projects/{slug}/tickets/{ticketId}/leaf-progress", async (
            string slug, int ticketId, DecompositionService decomposition) =>
        {
            var progress = await decomposition.CalculateLeafProgressAsync(slug, ticketId);
            return Results.Ok(progress);
        })
        .WithName("GetLeafProgress")
        .WithDescription("Get leaf task progress for a container");
    }

    public record ReparentRequest(int? NewParentId);
    public record CreateDependencyRequest(
        int FromTicketId,
        int ToTicketId,
        string? DependencyType,
        string? Scope,
        bool? Required
    );
    public record CreateParallelGroupRequest(
        int ParentTicketId,
        string? Name,
        string? JoinPolicy,
        int? MaxConcurrency,
        int? OnCompleteTicketId
    );
    public record DecompositionApplyRequest(
        DecompositionRequest Request,
        string? DecomposedBy
    );
}
