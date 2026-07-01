using KittyClaw.Core.Automation;
using Microsoft.AspNetCore.Mvc;

namespace KittyClaw.Web.Api;

public static class EndpointsRoster
{
    public static void MapRosterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/roster").WithTags("Roster");

        // ── Slots ──────────────────────────────────────────────────────
        group.MapGet("/slots", (RosterStore store) =>
        {
            return Results.Ok(store.Slots.Values.ToList());
        })
        .WithName("ListSlots")
        .WithDescription("List all execution slots");

        group.MapGet("/slots/{id}", (string id, RosterStore store) =>
        {
            var slot = store.GetSlot(id);
            return slot is not null ? Results.Ok(slot) : Results.NotFound();
        })
        .WithName("GetSlot")
        .WithDescription("Get a specific execution slot");

        group.MapPut("/slots/{id}", (string id, ExecutionSlot slot, RosterStore store) =>
        {
            slot.Id = id;
            store.UpsertSlot(slot);
            return Results.Ok(slot);
        })
        .WithName("UpdateSlot")
        .WithDescription("Update an execution slot");

        group.MapDelete("/slots/{id}", (string id, RosterStore store) =>
        {
            store.RemoveSlot(id);
            return Results.NoContent();
        })
        .WithName("DeleteSlot")
        .WithDescription("Delete an execution slot");

        // ── Presets ────────────────────────────────────────────────────
        group.MapGet("/presets", (RosterStore store) =>
        {
            return Results.Ok(store.Presets.Values.ToList());
        })
        .WithName("ListPresets")
        .WithDescription("List all roster presets");

        group.MapGet("/presets/active", (RosterStore store) =>
        {
            var active = store.GetActivePreset();
            return active is not null ? Results.Ok(active) : Results.NotFound();
        })
        .WithName("GetActivePreset")
        .WithDescription("Get the currently active roster preset");

        group.MapGet("/presets/{id}", (string id, RosterStore store) =>
        {
            var preset = store.GetPreset(id);
            return preset is not null ? Results.Ok(preset) : Results.NotFound();
        })
        .WithName("GetPreset")
        .WithDescription("Get a specific roster preset");

        group.MapPut("/presets/{id}", (string id, RosterPreset preset, RosterStore store) =>
        {
            preset.Id = id;
            store.UpsertPreset(preset);
            return Results.Ok(preset);
        })
        .WithName("UpdatePreset")
        .WithDescription("Update a roster preset");

        group.MapPost("/presets/{id}/activate", (string id, RosterStore store) =>
        {
            store.ActivatePreset(id);
            return Results.Ok(new { activated = id });
        })
        .WithName("ActivatePreset")
        .WithDescription("Activate a roster preset");

        // ── Fallbacks ──────────────────────────────────────────────────
        group.MapGet("/fallbacks", (RosterStore store) =>
        {
            return Results.Ok(store.Fallbacks.Values.ToList());
        })
        .WithName("ListFallbacks")
        .WithDescription("List all fallback policies");

        group.MapGet("/fallbacks/{id}", (string id, RosterStore store) =>
        {
            var policy = store.GetFallback(id);
            return policy is not null ? Results.Ok(policy) : Results.NotFound();
        })
        .WithName("GetFallback")
        .WithDescription("Get a specific fallback policy");

        group.MapPut("/fallbacks/{id}", (string id, FallbackPolicy policy, RosterStore store) =>
        {
            policy.Id = id;
            store.UpsertFallback(policy);
            return Results.Ok(policy);
        })
        .WithName("UpdateFallback")
        .WithDescription("Update a fallback policy");

        // ── Resolution preview ─────────────────────────────────────────
        group.MapPost("/resolve/{ticketId}", (
            int ticketId,
            [FromBody] ResolveRequest request,
            RosterStore store,
            Core.Services.TodoDbContext db) =>
        {
            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null) return Results.NotFound($"Ticket {ticketId} not found");

            var profiles = new Dictionary<string, Core.Automation.Runtimes.ModelProfileConfig>();
            // TODO: Load profiles from config

            var resolver = new ExecutionResolver(
                store.Slots.ToDictionary(s => s.Key),
                store.Presets.ToDictionary(p => p.Key),
                store.Fallbacks.ToDictionary(f => f.Key),
                profiles,
                store.ActivePresetId);

            var result = resolver.Resolve(
                ticketId,
                request.SlotId ?? ticket.AssignedSlotId,
                request.OverrideModelProfileId ?? ticket.OverrideModelProfileId,
                request.LockExecutor ?? ticket.LockExecutor);

            return Results.Ok(result);
        })
        .WithName("ResolveExecution")
        .WithDescription("Preview execution resolution for a ticket");

        // ── Ticket slot assignment ──────────────────────────────────────
        group.MapPost("/tickets/{ticketId}/assign", (
            int ticketId,
            [FromBody] AssignSlotRequest request,
            RosterStore store,
            Core.Services.TodoDbContext db) =>
        {
            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null) return Results.NotFound($"Ticket {ticketId} not found");

            // Validate slot exists
            if (!string.IsNullOrEmpty(request.SlotId) && !store.Slots.ContainsKey(request.SlotId))
            {
                return Results.BadRequest($"Slot '{request.SlotId}' not found");
            }

            ticket.AssignedSlotId = request.SlotId;
            ticket.OverrideModelProfileId = request.OverrideModelProfileId;
            ticket.LockExecutor = request.LockExecutor;
            ticket.UpdatedAt = DateTime.UtcNow;
            db.SaveChanges();

            return Results.Ok(new
            {
                ticketId,
                assignedSlotId = ticket.AssignedSlotId,
                overrideModelProfileId = ticket.OverrideModelProfileId,
                lockExecutor = ticket.LockExecutor
            });
        })
        .WithName("AssignSlotToTicket")
        .WithDescription("Assign an execution slot to a ticket");

        group.MapGet("/tickets/{ticketId}/assignment", (
            int ticketId,
            RosterStore store,
            Core.Services.TodoDbContext db) =>
        {
            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null) return Results.NotFound($"Ticket {ticketId} not found");

            return Results.Ok(new
            {
                ticketId,
                assignedSlotId = ticket.AssignedSlotId,
                overrideModelProfileId = ticket.OverrideModelProfileId,
                lockExecutor = ticket.LockExecutor
            });
        })
        .WithName("GetTicketAssignment")
        .WithDescription("Get execution slot assignment for a ticket");

        group.MapDelete("/tickets/{ticketId}/assignment", (
            int ticketId,
            Core.Services.TodoDbContext db) =>
        {
            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null) return Results.NotFound($"Ticket {ticketId} not found");

            ticket.AssignedSlotId = null;
            ticket.OverrideModelProfileId = null;
            ticket.LockExecutor = false;
            ticket.UpdatedAt = DateTime.UtcNow;
            db.SaveChanges();

            return Results.Ok(new { ticketId, cleared = true });
        })
        .WithName("ClearTicketAssignment")
        .WithDescription("Clear execution slot assignment from a ticket");
    }

    public record ResolveRequest(
        string? SlotId,
        string? OverrideModelProfileId,
        bool? LockExecutor
    );

    public record AssignSlotRequest(
        string? SlotId,
        string? OverrideModelProfileId,
        bool LockExecutor = false
    );
}
