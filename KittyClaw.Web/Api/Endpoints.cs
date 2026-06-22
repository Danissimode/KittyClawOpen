namespace KittyClaw.Web.Api;

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
        MapChat(api);
        MapImages(api);
        MapDashboard(api);

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
        }).WithTags("Board").Produces(StatusCodes.Status200OK).RequireCors("AllowAll");
    }
}
