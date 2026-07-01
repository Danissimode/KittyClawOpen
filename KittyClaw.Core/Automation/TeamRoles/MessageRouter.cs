using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.TeamRoles;

/// <summary>
/// Routes messages in the Command Dialogue Hub based on reply policy.
/// Ensures orchestrator-led communication by default.
/// </summary>
public sealed class MessageRouter
{
    private readonly RoleInboxStore _inboxStore;
    private readonly ILogger? _logger;

    public MessageRouter(RoleInboxStore inboxStore, ILogger? logger = null)
    {
        _inboxStore = inboxStore;
        _logger = logger;
    }

    /// <summary>
    /// Route a user message to the appropriate target.
    /// </summary>
    public async Task<RoutedMessage> RouteUserMessageAsync(
        string projectSlug,
        string text,
        string userId,
        ConversationPolicy? policy = null,
        CancellationToken ct = default)
    {
        policy ??= new ConversationPolicy { ProjectSlug = projectSlug };

        // Parse @mentions
        var (targetRole, targetAgent, wasDirect) = ParseMention(text);

        // Default: route to orchestrator
        if (string.IsNullOrEmpty(targetRole))
        {
            targetRole = "orchestrator";
        }

        // Check reply policy
        var visibility = DetermineVisibility(targetRole, wasDirect, policy);

        var routed = new RoutedMessage
        {
            ProjectSlug = projectSlug,
            Source = userId,
            TargetRole = targetRole,
            TargetAgentId = targetAgent,
            Text = text,
            Visibility = visibility,
            Mode = ConversationMode.OrchestratorDialogue,
            WasDirectMention = wasDirect
        };

        _logger?.LogInformation(
            "Routed message from {UserId} to {TargetRole} (direct={Direct}, visibility={Visibility})",
            userId, targetRole, wasDirect, visibility);

        return routed;
    }

    /// <summary>
    /// Route a role response back to the appropriate destination.
    /// </summary>
    public async Task<RoutedMessage> RouteRoleResponseAsync(
        string projectSlug,
        string roleId,
        string agentId,
        string text,
        int? ticketId,
        ConversationPolicy? policy = null,
        CancellationToken ct = default)
    {
        policy ??= new ConversationPolicy { ProjectSlug = projectSlug };

        // Orchestrator always goes to user
        if (roleId == "orchestrator")
        {
            return new RoutedMessage
            {
                ProjectSlug = projectSlug,
                Source = agentId,
                TargetRole = roleId,
                Text = text,
                Visibility = MessageVisibility.UserVisible,
                Mode = ConversationMode.OrchestratorDialogue,
                TicketId = ticketId
            };
        }

        // Other roles: check policy
        var visibility = policy.ReplyPolicy switch
        {
            ReplyPolicy.DebugAllAgents => MessageVisibility.UserVisible,
            ReplyPolicy.DirectRolesAllowed => MessageVisibility.UserVisible,
            _ => MessageVisibility.TeamActivity // Default: mediated, not user-visible
        };

        return new RoutedMessage
        {
            ProjectSlug = projectSlug,
            Source = agentId,
            TargetRole = roleId,
            Text = text,
            Visibility = visibility,
            Mode = ConversationMode.TeamActivityLog,
            TicketId = ticketId
        };
    }

    /// <summary>
    /// Check if a role can reply to main dialogue.
    /// </summary>
    public bool CanRoleReplyToMain(string roleId, bool wasDirectMention, bool wasRequestedByOrchestrator, ConversationPolicy policy)
    {
        // Orchestrator always can
        if (roleId == "orchestrator") return true;

        // System always can
        if (roleId == "system") return true;

        // Check policy
        return policy.ReplyPolicy switch
        {
            ReplyPolicy.DebugAllAgents => true,
            ReplyPolicy.DirectRolesAllowed => wasDirectMention,
            ReplyPolicy.MediatedRoles => wasDirectMention || wasRequestedByOrchestrator,
            ReplyPolicy.OrchestratorOnly => false,
            _ => false
        };
    }

    /// <summary>
    /// Parse @mention from text.
    /// </summary>
    private (string? role, string? agent, bool wasDirect) ParseMention(string text)
    {
        // Check for direct agent mention (e.g., @programmer-1)
        var agentMatch = System.Text.RegularExpressions.Regex.Match(text, @"@(\w+-\d+)");
        if (agentMatch.Success)
        {
            var agentName = agentMatch.Groups[1].Value;
            var role = ExtractRoleFromAgent(agentName);
            return (role, agentName, true);
        }

        // Check for role mention (e.g., @programmer)
        var roleMatch = System.Text.RegularExpressions.Regex.Match(text, @"@(orchestrator|planner|architect|programmer|validator|health|scheduler)");
        if (roleMatch.Success)
        {
            return (roleMatch.Groups[1].Value, null, true);
        }

        return (null, null, false);
    }

    private string ExtractRoleFromAgent(string agentName)
    {
        // programmer-1 -> programmer
        // validator-1 -> validator
        var match = System.Text.RegularExpressions.Regex.Match(agentName, @"^(\w+)-\d+$");
        return match.Success ? match.Groups[1].Value : agentName;
    }

    private MessageVisibility DetermineVisibility(string targetRole, bool wasDirectMention, ConversationPolicy policy)
    {
        // Orchestrator replies are always visible
        if (targetRole == "orchestrator")
            return MessageVisibility.UserVisible;

        // Direct mention: visible if policy allows
        if (wasDirectMention)
        {
            return policy.ReplyPolicy switch
            {
                ReplyPolicy.DebugAllAgents => MessageVisibility.UserVisible,
                ReplyPolicy.DirectRolesAllowed => MessageVisibility.UserVisible,
                ReplyPolicy.MediatedRoles => MessageVisibility.OrchestratorSummary,
                _ => MessageVisibility.TeamActivity
            };
        }

        // No direct mention: not visible in main dialogue
        return MessageVisibility.TeamActivity;
    }
}
