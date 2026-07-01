namespace KittyClaw.Web.Api;

using KittyClaw.Core.Automation;
using KittyClaw.Core.Services;
using KittyClaw.Web.Services;

public static partial class Endpoints
{
    public static void MapTodoApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        MapColumns(api);
        MapProjects(api);
        MapTickets(api);
        MapProjectLabels(api);
        MapTicketLabels(api);
        MapTicketReorder(api);
        MapMembers(api);
        MapBrowse(api);
        MapSkills(api);
        MapAutomations(api);
        MapRuns(api);
        MapFailureLogbook(api);
        MapRunners(api);
        MapGlobalRuns(api);
        MapChat(api);
        MapTeamChat(api);
        MapImages(api);
        MapDashboard(api);
        app.MapIdeEndpoints();
        app.MapRosterEndpoints();
        app.MapHealthEndpoints();
        app.MapTreeEndpoints();
        app.MapCommandHubEndpoints();
        app.MapTeamRoleEndpoints();

        // SSE endpoint for real-time board updates across all clients
        api.MapGet("/projects/{slug}/events", async (string slug, BoardUpdateNotifier notifier, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("Connection", "keep-alive");
            await foreach (var updatedSlug in notifier.UpdatesAsync(ct))
            {
                if (updatedSlug != slug) continue;
                await ctx.Response.WriteAsync($"data: {{\"type\":\"board-update\",\"slug\":\"{updatedSlug}\"}}\n\n");
                await ctx.Response.Body.FlushAsync();
            }
        }).WithTags("Board").Produces(StatusCodes.Status200OK).RequireCors("LocalOnly");

        // Settings endpoints
        api.MapGet("/settings", async (SettingsService svc) => Results.Ok(await svc.LoadAsync())).WithTags("Settings");
        api.MapPost("/settings", async (SettingsData data, SettingsService svc) =>
        {
            await svc.SaveAsync(data);
            return Results.Ok(data);
        }).WithTags("Settings");
    }
}
