using KittyClaw.Core.Automation.CommandHub;
using Microsoft.AspNetCore.Mvc;

namespace KittyClaw.Web.Api;

public static class EndpointsCommandHub
{
    public static void MapCommandHubEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/command-hub").WithTags("Command Hub");

        // ── Send command ────────────────────────────────────────────────
        group.MapPost("/projects/{slug}/command", async (
            string slug,
            [FromBody] SendCommandRequest request,
            CommandHubService hub,
            CommandParser parser) =>
        {
            // Get or create conversation
            var conversation = await hub.GetOrCreateConversationAsync(
                slug, "internal", null, request.UserId);

            // Save user message
            var userMessage = await hub.SaveMessageAsync(new CommandMessage
            {
                ProjectSlug = slug,
                ConversationId = conversation.Id,
                Source = "internal",
                UserId = request.UserId ?? "owner",
                Text = request.Text,
                TargetAgent = ExtractTargetAgent(request.Text)
            });

            // Parse intent
            var intent = parser.Parse(slug, userMessage.Id, request.Text);

            // Save intent type to message
            // (In production, update message with parsed intent)

            return Results.Ok(new
            {
                conversationId = conversation.Id,
                messageId = userMessage.Id,
                intent = new
                {
                    type = intent.Type,
                    risk = intent.Risk,
                    requiresApproval = intent.RequiresApproval,
                    confidence = intent.Confidence,
                    parameters = intent.ParametersJson
                }
            });
        })
        .WithName("SendCommand")
        .WithDescription("Send a command message");

        // ── Get conversation history ────────────────────────────────────
        group.MapGet("/projects/{slug}/conversations/{conversationId}/messages", async (
            string slug, string conversationId, CommandHubService hub) =>
        {
            // TODO: Implement message retrieval
            return Results.Ok(new List<object>());
        })
        .WithName("GetConversationMessages")
        .WithDescription("Get messages in a conversation");

        // ── Plans ───────────────────────────────────────────────────────
        group.MapGet("/projects/{slug}/plans/pending", async (string slug, CommandHubService hub) =>
        {
            var plans = await hub.PendingPlansAsync(slug);
            return Results.Ok(plans);
        })
        .WithName("GetPendingPlans")
        .WithDescription("Get plans pending approval");

        group.MapPost("/projects/{slug}/plans/{planId}/approve", async (
            string slug, string planId, [FromBody] ApproveRequest request, CommandHubService hub) =>
        {
            var result = await hub.ApprovePlanAsync(slug, planId, request.ApprovedBy);
            return result ? Results.Ok(new { approved = true }) : Results.NotFound();
        })
        .WithName("ApprovePlan")
        .WithDescription("Approve a command plan");

        group.MapPost("/projects/{slug}/plans/{planId}/reject", async (
            string slug, string planId, [FromBody] RejectRequest request, CommandHubService hub) =>
        {
            var result = await hub.RejectPlanAsync(slug, planId, request.Reason);
            return result ? Results.Ok(new { rejected = true }) : Results.NotFound();
        })
        .WithName("RejectPlan")
        .WithDescription("Reject a command plan");
    }

    private static string? ExtractTargetAgent(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("@orchestrator")) return "orchestrator";
        if (lower.Contains("@planner")) return "planner";
        if (lower.Contains("@health")) return "health";
        if (lower.Contains("@scheduler")) return "scheduler";
        if (lower.Contains("@reviewer")) return "reviewer";
        return null;
    }

    public record SendCommandRequest(string Text, string? UserId);
    public record ApproveRequest(string ApprovedBy);
    public record RejectRequest(string? Reason);
}
