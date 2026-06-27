using System.Text;
using System.Text.Json;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Integrations.OpenCode;
using KittyClaw.Core.Services;

namespace KittyClaw.Web.Api;

public static partial class Endpoints
{
    private static readonly JsonSerializerOptions SseJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static void MapRuns(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{slug}/runs", (string slug, AgentRunRegistry reg) =>
            Results.Ok(reg.ActiveForProject(slug).Select(r => new
            {
                r.RunId, r.AgentName, r.SkillFile, r.TicketId, r.ConcurrencyGroup,
                r.StartedAt, r.SessionId, status = r.Status.ToString(),
            })))
            .WithTags("Runs");

        // Get the most recent run metadata for a ticket — used by the ticket drawer to
        // show provider/model/worktree/session info without polling the runs registry.
        api.MapGet("/projects/{slug}/tickets/{id:int}/runs/latest", async (string slug, int id, ITicketExecutionMetadataStore store) =>
        {
            var meta = await store.GetAsync(slug, id);
            return meta is null ? Results.NotFound() : Results.Ok(meta);
        }).WithTags("Runs");

        // Get all run history for a project (filtered client-side by ticket id).
        api.MapGet("/projects/{slug}/tickets/{id:int}/runs", async (string slug, int id, ITicketExecutionMetadataStore store) =>
        {
            var all = await store.GetByProjectAsync(slug);
            var mine = all.Where(m => m.TicketId == id).ToList();
            return Results.Ok(mine);
        }).WithTags("Runs");

        // OpenCode health check — used by the UI to decide whether to surface the runner.
        api.MapGet("/projects/{slug}/opencode/health", (string slug, RunnerRegistry registry) =>
        {
            var opencode = registry.GetRunner("opencode");
            if (opencode is null) return Results.Ok(new { available = false, reason = "not-registered" });
            return Results.Ok(new { available = opencode.IsAvailable, kind = opencode.Kind });
        }).WithTags("OpenCode");

        // Trigger a manual run for a ticket via the RunnerRegistry — the runner is
        // selected by ticket.ExecutionModeOverride (or the project default).
        api.MapPost("/projects/{slug}/tickets/{id:int}/run", async (string slug, int id, StartRunRequest req,
            TicketService ts, RunnerRegistry registry, AgentRunRegistry runRegistry, ProjectService ps) =>
        {
            var ticket = await ts.GetTicketAsync(slug, id);
            if (ticket is null) return Results.NotFound();

            // Determine which runner to use: ticket override → project default → Claude (legacy).
            IAgentRunner? runner = null;
            if (!string.IsNullOrEmpty(ticket.ExecutionModeOverride)
                && Enum.TryParse<ExecutionMode>(ticket.ExecutionModeOverride, true, out var mode))
            {
                runner = registry.ResolveRunner(mode);
            }
            runner ??= registry.GetDefaultRunner();

            if (runner is null) return Results.BadRequest(new { error = "No runner available." });

            // Compose a minimal request from the ticket. Real prompt construction lives in
            // the ActionExecutor path; this endpoint is for ad-hoc starts from the drawer.
            var project = await ps.GetProjectAsync(slug);
            var workspacePath = project is not null ? ps.ResolveWorkspacePath(project) : "";
            var request = new AgentRunRequest
            {
                ProjectSlug = slug,
                WorkspacePath = workspacePath,
                AgentName = ticket.AssignedTo ?? req.Author,
                SkillFile = $"{(ticket.AssignedTo ?? req.Author)}/SKILL.md",
                TicketId = ticket.Id,
                TicketTitle = ticket.Title,
                TicketStatus = ticket.Status,
                TicketDescription = ticket.Description,
                Labels = ticket.Labels.Select(l => l.Name).ToList(),
                Assignee = ticket.AssignedTo,
                CurrentColumn = ticket.Status,
                Prompt = ticket.Description,
                ConcurrencyGroup = $"ticket-{ticket.Id}",
                Provider = ticket.ProviderOverride,
                Model = ticket.ModelOverride,
                Profile = ticket.ProfileOverride
            };

            try
            {
                var result = await runner.StartAsync(request, CancellationToken.None);
                return Results.Ok(new { runId = result.RunId, status = result.Status.ToString(), runner = runner.Kind });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, runner = runner.Kind });
            }
        }).WithTags("Runs");

        api.MapGet("/projects/{slug}/runs/{runId}", (string slug, string runId, AgentRunRegistry reg) =>
        {
            var run = reg.Get(runId);
            if (run is null || run.ProjectSlug != slug) return Results.NotFound();
            return Results.Ok(new
            {
                run.RunId, run.AgentName, run.SkillFile, run.TicketId, run.ConcurrencyGroup,
                run.StartedAt, run.EndedAt, run.SessionId, run.ExitCode,
                status = run.Status.ToString(),
                events = run.SnapshotBuffer(),
            });
        }).WithTags("Runs");

        api.MapGet("/projects/{slug}/runs/{runId}/stream", async (string slug, string runId, string? since, HttpContext http, AgentRunRegistry reg, CancellationToken ct) =>
        {
            var run = reg.Get(runId);
            if (run is null || run.ProjectSlug != slug) { http.Response.StatusCode = 404; return; }
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            // Optional ?since=<ISO timestamp> filter: replay only buffer events strictly after that
            // instant. Used when a chat drawer reattaches mid-run and already has all events up to
            // its latest persisted message — without this, the buffered events would re-render as
            // duplicates.
            DateTime? sinceUtc = null;
            if (!string.IsNullOrWhiteSpace(since)
                && DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                sinceUtc = parsed.ToUniversalTime();
            }

            var queue = System.Threading.Channels.Channel.CreateUnbounded<StreamEvent>();
            void handler(StreamEvent ev) => queue.Writer.TryWrite(ev);
            run.OnEvent += handler;

            try
            {
                foreach (var ev in run.SnapshotBuffer())
                {
                    if (sinceUtc is not null && ev.At <= sinceUtc.Value) continue;
                    await WriteSseAsync(http.Response, ev, ct);
                }

                while (!ct.IsCancellationRequested && run.Status == AgentRunStatus.Running)
                {
                    while (queue.Reader.TryRead(out var ev))
                        await WriteSseAsync(http.Response, ev, ct);
                    try { await Task.Delay(200, ct); } catch { break; }
                }
                while (queue.Reader.TryRead(out var ev))
                    await WriteSseAsync(http.Response, ev, ct);
                await WriteSseRawAsync(http.Response, "event: end\ndata: {}\n\n", ct);
            }
            finally { run.OnEvent -= handler; }
        }).WithTags("Runs");

        api.MapPost("/projects/{slug}/runs/{runId}/steer", async (string slug, string runId, SteerRunRequest req, AgentRunRegistry reg, ChatService cs) =>
        {
            var run = reg.Get(runId);
            if (run is null || run.ProjectSlug != slug) return Results.NotFound();
            if (run.Status != AgentRunStatus.Running) return Results.BadRequest(new { error = "Run is not active." });
            await run.SteeringQueue.Writer.WriteAsync(req.Text);
            if (!string.IsNullOrEmpty(run.ChatTarget))
                await cs.AppendAsync(slug, run.ChatTarget, "inject", req.Text);
            return Results.NoContent();
        }).WithTags("Runs");

        api.MapPost("/projects/{slug}/runs/{runId}/stop", (string slug, string runId, AgentRunRegistry reg) =>
        {
            var run = reg.Get(runId);
            if (run is null || run.ProjectSlug != slug) return Results.NotFound();
            run.Cancellation.Cancel();
            return Results.NoContent();
        }).WithTags("Runs");

        api.MapPost("/projects/{slug}/runs/{runId}/retry", async (string slug, string runId,
            AgentRunRegistry reg, ProjectService ps, TicketService ts, ClaudeRunner runner) =>
        {
            var run = reg.Get(runId);
            if (run is null || run.ProjectSlug != slug) return Results.NotFound();
            if (run.Status == AgentRunStatus.Running)
                return Results.BadRequest(new { error = "Run is still active." });
            if (reg.HasActiveInGroup(slug, run.ConcurrencyGroup))
                return Results.BadRequest(new { error = "An agent is already running in this group." });

            var project = await ps.GetProjectAsync(slug);
            if (project is null) return Results.NotFound();

            string? ticketTitle = null, ticketStatus = null;
            if (run.TicketId is int tid)
            {
                var ticket = await ts.GetTicketAsync(slug, tid);
                ticketTitle = ticket?.Title;
                ticketStatus = ticket?.Status;
            }

            var newRunId = Guid.NewGuid().ToString("N");
            var ctx = new ClaudeRunContext
            {
                ProjectSlug = slug,
                WorkspacePath = ps.ResolveWorkspacePath(project),
                AgentName = run.AgentName,
                SkillFile = run.SkillFile,
                TicketId = run.TicketId,
                TicketTitle = ticketTitle,
                TicketStatus = ticketStatus,
                ConcurrencyGroup = run.ConcurrencyGroup,
                Model = run.Model,
                FallbackModel = project.FallbackModel,
                RetryOnResumeFailure = true,
                PresetRunId = newRunId,
            };
            _ = runner.RunAsync(ctx, CancellationToken.None);
            return Results.Ok(new { runId = newRunId });
        }).WithTags("Runs");
    }

    private static async Task WriteSseAsync(HttpResponse res, StreamEvent ev, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(ev, SseJson);
        await WriteSseRawAsync(res, $"data: {payload}\n\n", ct);
    }

    private static async Task WriteSseRawAsync(HttpResponse res, string frame, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        await res.Body.WriteAsync(bytes, ct);
        await res.Body.FlushAsync(ct);
    }
}
