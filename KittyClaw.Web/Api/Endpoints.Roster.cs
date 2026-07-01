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

        // ── OpenCode config generation ──────────────────────────────────
        group.MapGet("/opencode-config", (RosterStore store) =>
        {
            var profiles = new Dictionary<string, Core.Automation.Runtimes.ModelProfileConfig>();
            // TODO: Load profiles from AgentRuntimeProjectConfig
            
            var generator = new Core.Integrations.OpenCode.OpenCodeConfigGenerator(store, profiles);
            var configJson = generator.ToJson();
            return Results.Content(configJson, "application/json");
        })
        .WithName("GenerateOpenCodeConfig")
        .WithDescription("Generate OpenCode agent configuration from active roster");

        group.MapPost("/opencode-config/write", (RosterStore store, IConfiguration config) =>
        {
            var profiles = new Dictionary<string, Core.Automation.Runtimes.ModelProfileConfig>();
            // TODO: Load profiles from AgentRuntimeProjectConfig
            
            var generator = new Core.Integrations.OpenCode.OpenCodeConfigGenerator(store, profiles);
            var configJson = generator.ToJson();
            
            // Write to workspace opencode.json
            var workspacePath = config["WorkspacePath"] ?? Directory.GetCurrentDirectory();
            var opencodeConfigPath = Path.Combine(workspacePath, "opencode.json");
            File.WriteAllText(opencodeConfigPath, configJson);
            
            return Results.Ok(new { path = opencodeConfigPath, written = true });
        })
        .WithName("WriteOpenCodeConfig")
        .WithDescription("Write OpenCode configuration to workspace");

        // ── Resolution preview ─────────────────────────────────────────
        group.MapPost("/resolve/{ticketId}", (
            int ticketId,
            [FromBody] ResolveRequest request,
            RosterStore store,
            TicketSlotAssignmentStore assignmentStore) =>
        {
            var assignment = assignmentStore.Get(ticketId);

            var profiles = new Dictionary<string, Core.Automation.Runtimes.ModelProfileConfig>();
            // TODO: Load profiles from config

            var resolver = new ExecutionResolver(
                store.Slots.ToDictionary(s => s.Key, s => s.Value),
                store.Presets.ToDictionary(p => p.Key, p => p.Value),
                store.Fallbacks.ToDictionary(f => f.Key, f => f.Value),
                profiles,
                store.ActivePresetId);

            var result = resolver.Resolve(
                ticketId,
                request.SlotId ?? assignment?.AssignedSlotId,
                request.OverrideModelProfileId ?? assignment?.OverrideModelProfileId,
                request.LockExecutor ?? assignment?.LockExecutor ?? false);

            return Results.Ok(result);
        })
        .WithName("ResolveExecution")
        .WithDescription("Preview execution resolution for a ticket");

        // ── Ticket slot assignment ──────────────────────────────────────
        group.MapPost("/tickets/{ticketId}/assign", (
            int ticketId,
            [FromBody] AssignSlotRequest request,
            RosterStore store,
            TicketSlotAssignmentStore assignmentStore) =>
        {
            // Validate slot exists
            if (!string.IsNullOrEmpty(request.SlotId) && !store.Slots.ContainsKey(request.SlotId))
            {
                return Results.BadRequest($"Slot '{request.SlotId}' not found");
            }

            var assignment = new TicketSlotAssignment
            {
                TicketId = ticketId,
                AssignedSlotId = request.SlotId ?? "",
                OverrideModelProfileId = request.OverrideModelProfileId,
                LockExecutor = request.LockExecutor,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "api"
            };

            assignmentStore.Upsert(assignment);

            return Results.Ok(new
            {
                ticketId,
                assignedSlotId = assignment.AssignedSlotId,
                overrideModelProfileId = assignment.OverrideModelProfileId,
                lockExecutor = assignment.LockExecutor
            });
        })
        .WithName("AssignSlotToTicket")
        .WithDescription("Assign an execution slot to a ticket");

        group.MapGet("/tickets/{ticketId}/assignment", (
            int ticketId,
            TicketSlotAssignmentStore assignmentStore) =>
        {
            var assignment = assignmentStore.Get(ticketId);
            if (assignment is null) return Results.Ok(new { ticketId, assignedSlotId = (string?)null });

            return Results.Ok(new
            {
                ticketId,
                assignedSlotId = assignment.AssignedSlotId,
                overrideModelProfileId = assignment.OverrideModelProfileId,
                lockExecutor = assignment.LockExecutor
            });
        })
        .WithName("GetTicketAssignment")
        .WithDescription("Get execution slot assignment for a ticket");

        group.MapDelete("/tickets/{ticketId}/assignment", (
            int ticketId,
            TicketSlotAssignmentStore assignmentStore) =>
        {
            assignmentStore.Remove(ticketId);
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
