using KittyClaw.Core.Automation.Health;
using Microsoft.AspNetCore.Mvc;

namespace KittyClaw.Web.Api;

public static class EndpointsHealth
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/health-center").WithTags("Health Center");

        // ── Events CRUD ─────────────────────────────────────────────────
        group.MapGet("/events/{slug}", async (
            string slug,
            string? status,
            string? category,
            string? level,
            string? agentId,
            int? ticketId,
            int limit,
            ProcessEventStore store) =>
        {
            var events = await store.RecentAsync(slug, limit > 0 ? limit : 50);
            
            if (!string.IsNullOrEmpty(status))
                events = events.Where(e => e.Status == status).ToList();
            if (!string.IsNullOrEmpty(category))
                events = events.Where(e => e.Category == category).ToList();
            if (!string.IsNullOrEmpty(level))
                events = events.Where(e => e.Level == level).ToList();
            if (!string.IsNullOrEmpty(agentId))
                events = events.Where(e => e.AgentId == agentId).ToList();
            if (ticketId.HasValue)
                events = events.Where(e => e.TicketId == ticketId.Value || e.ParentTicketId == ticketId.Value).ToList();
            
            return Results.Ok(events);
        })
        .WithName("ListEvents")
        .WithDescription("List process events with filters");

        group.MapGet("/events/{slug}/open", async (string slug, ProcessEventStore store) =>
        {
            var events = await store.OpenEventsAsync(slug);
            return Results.Ok(events);
        })
        .WithName("ListOpenEvents")
        .WithDescription("List open process events");

        group.MapGet("/events/{slug}/by-category/{category}", async (string slug, string category, ProcessEventStore store) =>
        {
            var events = await store.ByCategoryAsync(slug, category);
            return Results.Ok(events);
        })
        .WithName("ListEventsByCategory")
        .WithDescription("List events by category");

        group.MapGet("/events/{slug}/ticket/{ticketId}", async (string slug, int ticketId, ProcessEventStore store) =>
        {
            var events = await store.ForTicketAsync(slug, ticketId);
            return Results.Ok(events);
        })
        .WithName("ListEventsForTicket")
        .WithDescription("List events for a specific ticket");

        group.MapGet("/events/{slug}/agent/{agentId}", async (string slug, string agentId, ProcessEventStore store) =>
        {
            var events = await store.ForAgentAsync(slug, agentId);
            return Results.Ok(events);
        })
        .WithName("ListEventsForAgent")
        .WithDescription("List events for a specific agent");

        // ── Event actions ───────────────────────────────────────────────
        group.MapPost("/events/{slug}/{eventId}/acknowledge", async (string slug, string eventId, ProcessEventStore store) =>
        {
            var result = await store.AcknowledgeAsync(slug, eventId);
            return result ? Results.Ok(new { acknowledged = true }) : Results.NotFound();
        })
        .WithName("AcknowledgeEvent")
        .WithDescription("Acknowledge an event");

        group.MapPost("/events/{slug}/{eventId}/resolve", async (
            string slug, string eventId,
            [FromBody] ResolveEventRequest request,
            ProcessEventStore store) =>
        {
            var result = await store.ResolveAsync(slug, eventId, request.Resolution, request.ResolvedBy, request.ResolutionNote);
            return result ? Results.Ok(new { resolved = true }) : Results.NotFound();
        })
        .WithName("ResolveEvent")
        .WithDescription("Resolve an event with resolution note");

        group.MapPost("/events/{slug}/{eventId}/dismiss", async (string slug, string eventId, ProcessEventStore store) =>
        {
            var result = await store.DismissAsync(slug, eventId);
            return result ? Results.Ok(new { dismissed = true }) : Results.NotFound();
        })
        .WithName("DismissEvent")
        .WithDescription("Dismiss an event");

        // ── Overview / Stats ────────────────────────────────────────────
        group.MapGet("/overview/{slug}", async (string slug, ProcessEventStore store) =>
        {
            var allEvents = await store.RecentAsync(slug, 1000);
            var openEvents = allEvents.Where(e => e.Status == "open").ToList();
            
            return Results.Ok(new
            {
                totalEvents = allEvents.Count,
                openCount = openEvents.Count,
                criticalCount = openEvents.Count(e => e.Level == "critical"),
                errorCount = openEvents.Count(e => e.Level == "error"),
                warningCount = openEvents.Count(e => e.Level == "warning"),
                needsHumanCount = openEvents.Count(e => e.Level == "needs_human"),
                byCategory = openEvents.GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key, g => g.Count()),
                byLevel = openEvents.GroupBy(e => e.Level)
                    .ToDictionary(g => g.Key, g => g.Count()),
                recentCritical = openEvents
                    .Where(e => e.Level is "critical" or "needs_human")
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(5)
                    .Select(e => new { e.Id, e.Title, e.Category, e.EventType, e.CreatedAt })
            });
        })
        .WithName("GetOverview")
        .WithDescription("Get health center overview stats");

        // ── Watchdogs ───────────────────────────────────────────────────
        group.MapGet("/watchdogs/{slug}", async (string slug, ProcessEventStore store) =>
        {
            var events = await store.ByCategoryAsync(slug, "watchdog");
            return Results.Ok(events.Where(e => e.Status == "open").ToList());
        })
        .WithName("ListWatchdogEvents")
        .WithDescription("List watchdog events (stuck/silent runs)");

        // ── Failures ────────────────────────────────────────────────────
        group.MapGet("/failures/{slug}", async (string slug, ProcessEventStore store) =>
        {
            var events = await store.ByCategoryAsync(slug, "runner");
            var failures = await store.ByCategoryAsync(slug, "failure");
            var all = events.Concat(failures).Where(e => e.Status == "open").ToList();
            return Results.Ok(all);
        })
        .WithName("ListFailureEvents")
        .WithDescription("List failure events");

        // ── System ──────────────────────────────────────────────────────
        group.MapGet("/system/{slug}", async (string slug, ProcessEventStore store) =>
        {
            var events = await store.ByCategoryAsync(slug, "system");
            return Results.Ok(events.Where(e => e.Status == "open").ToList());
        })
        .WithName("ListSystemEvents")
        .WithDescription("List system events");
    }

    public record ResolveEventRequest(
        string? Resolution,
        string? ResolvedBy,
        string? ResolutionNote
    );
}
