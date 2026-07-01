using System.Text.RegularExpressions;

namespace KittyClaw.Core.Automation.CommandHub;

/// <summary>
/// Parses command messages into structured intents.
/// Supports both natural language patterns and deterministic slash commands.
/// </summary>
public sealed partial class CommandParser
{
    private static readonly Dictionary<string, (string IntentType, string Risk, bool RequiresApproval)> IntentMap = new()
    {
        // Read-only commands
        ["status"] = (CommandIntentTypes.Status, CommandRiskLevels.Low, false),
        ["health"] = (CommandIntentTypes.Health, CommandRiskLevels.Low, false),
        ["report"] = (CommandIntentTypes.Report, CommandRiskLevels.Low, false),
        ["backlog"] = (CommandIntentTypes.BacklogNext, CommandRiskLevels.Low, false),
        ["tree"] = (CommandIntentTypes.Tree, CommandRiskLevels.Low, false),
        ["ticket"] = (CommandIntentTypes.TicketDetail, CommandRiskLevels.Low, false),
        ["agents"] = (CommandIntentTypes.AgentStatus, CommandRiskLevels.Low, false),
        
        // Planning commands
        ["decompose"] = (CommandIntentTypes.Decompose, CommandRiskLevels.Medium, true),
        ["split"] = (CommandIntentTypes.Decompose, CommandRiskLevels.Medium, true),
        ["propose"] = (CommandIntentTypes.ProposeTasks, CommandRiskLevels.Low, false),
        ["plan"] = (CommandIntentTypes.ProposeTasks, CommandRiskLevels.Low, false),
        
        // Board mutation commands
        ["move"] = (CommandIntentTypes.MoveTicket, CommandRiskLevels.Medium, true),
        ["assign"] = (CommandIntentTypes.AssignAgent, CommandRiskLevels.Medium, true),
        ["create"] = (CommandIntentTypes.CreateTicket, CommandRiskLevels.Medium, true),
        
        // Execution commands
        ["start"] = (CommandIntentTypes.StartTicket, CommandRiskLevels.High, true),
        ["run"] = (CommandIntentTypes.StartTicket, CommandRiskLevels.High, true),
        ["start-backlog"] = (CommandIntentTypes.StartBacklog, CommandRiskLevels.High, true),
        ["run-ready"] = (CommandIntentTypes.RunReadyChildren, CommandRiskLevels.High, true),
        ["stop"] = (CommandIntentTypes.StopRun, CommandRiskLevels.High, true),
        
        // Recovery commands
        ["restart"] = (CommandIntentTypes.RestartRun, CommandRiskLevels.High, true),
        ["blocker"] = (CommandIntentTypes.CreateBlocker, CommandRiskLevels.Medium, true),
        ["resolve"] = (CommandIntentTypes.ResolveEvent, CommandRiskLevels.Medium, true),
    };

    /// <summary>
    /// Parse a message into an intent.
    /// </summary>
    public CommandIntent Parse(string projectSlug, string messageId, string text)
    {
        var trimmed = text.Trim();
        
        // Try deterministic commands first (slash commands or keyword patterns)
        var deterministic = TryParseDeterministic(projectSlug, messageId, trimmed);
        if (deterministic is not null)
            return deterministic;
        
        // Try natural language patterns
        return ParseNaturalLanguage(projectSlug, messageId, trimmed);
    }

    private CommandIntent? TryParseDeterministic(string projectSlug, string messageId, string text)
    {
        // Remove @mentions
        var cleaned = Regex.Replace(text, @"@\w+", "").Trim();
        
        // Try exact command matches
        foreach (var (keyword, (intentType, risk, requiresApproval)) in IntentMap)
        {
            if (cleaned.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = cleaned[keyword.Length..].Trim();
                var parameters = ParseParameters(remainder);
                
                return new CommandIntent
                {
                    ProjectSlug = projectSlug,
                    MessageId = messageId,
                    Type = intentType,
                    Risk = risk,
                    RequiresApproval = requiresApproval,
                    ParametersJson = parameters,
                    Confidence = 1.0,
                    RawCommand = text
                };
            }
        }
        
        return null;
    }

    private CommandIntent ParseNaturalLanguage(string projectSlug, string messageId, string text)
    {
        var lower = text.ToLowerInvariant();
        
        // Status queries
        if (ContainsAny(lower, "status", "что сейчас", "что происходит", "current state"))
        {
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Status, CommandRiskLevels.Low, false, text, 0.8);
        }
        
        // Health queries
        if (ContainsAny(lower, "health", "здоровье", "ошибки", "сломано", "broken", "issues"))
        {
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Health, CommandRiskLevels.Low, false, text, 0.8);
        }
        
        // Report queries
        if (ContainsAny(lower, "report", "отчёт", "отчет", "summary", "сводка"))
        {
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Report, CommandRiskLevels.Low, false, text, 0.8);
        }
        
        // Backlog queries
        if (ContainsAny(lower, "backlog", "backlog задач", "из бэклога", "from backlog"))
        {
            var count = ExtractCount(lower);
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.BacklogNext, CommandRiskLevels.Low, false, text, 0.8,
                $"{{\"count\":{count}}}");
        }
        
        // Tree queries
        if (ContainsAny(lower, "tree", "дерево", "поддерево", "subtree"))
        {
            var ticketId = ExtractTicketId(lower);
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Tree, CommandRiskLevels.Low, false, text, 0.8,
                ticketId.HasValue ? $"{{\"ticketId\":{ticketId}}}" : null);
        }
        
        // Start/run commands
        if (ContainsAny(lower, "запусти", "запустить", "start", "run", "выполни"))
        {
            var ticketId = ExtractTicketId(lower);
            if (ticketId.HasValue)
            {
                return CreateIntent(projectSlug, messageId, CommandIntentTypes.StartTicket, CommandRiskLevels.High, true, text, 0.8,
                    $"{{\"ticketId\":{ticketId}}}");
            }
            
            // Check for "backlog" pattern
            if (ContainsAny(lower, "backlog", "из бэклога", "из backlog"))
            {
                var count = ExtractCount(lower);
                return CreateIntent(projectSlug, messageId, CommandIntentTypes.StartBacklog, CommandRiskLevels.High, true, text, 0.8,
                    $"{{\"count\":{count}}}");
            }
        }
        
        // Decompose/split commands
        if (ContainsAny(lower, "разбей", "разделить", "decompose", "split", "subtasks", "подзадачи"))
        {
            var ticketId = ExtractTicketId(lower);
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Decompose, CommandRiskLevels.Medium, true, text, 0.7,
                ticketId.HasValue ? $"{{\"ticketId\":{ticketId}}}" : null);
        }
        
        // Schedule commands
        if (ContainsAny(lower, "запланируй", "schedule", "запусти завтра", "tomorrow"))
        {
            var ticketId = ExtractTicketId(lower);
            var time = ExtractTime(lower);
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.Schedule, CommandRiskLevels.Medium, true, text, 0.7,
                $"{{\"ticketId\":{ticketId},\"scheduledAt\":\"{time}\"}}");
        }
        
        // Stop commands
        if (ContainsAny(lower, "останови", "stop", "остановить"))
        {
            return CreateIntent(projectSlug, messageId, CommandIntentTypes.StopRun, CommandRiskLevels.High, true, text, 0.8);
        }
        
        // Default: low confidence unknown
        return CreateIntent(projectSlug, messageId, "unknown", CommandRiskLevels.Low, false, text, 0.3);
    }

    private CommandIntent CreateIntent(
        string projectSlug, string messageId, string type, string risk, 
        bool requiresApproval, string text, double confidence, string? parameters = null)
    {
        return new CommandIntent
        {
            ProjectSlug = projectSlug,
            MessageId = messageId,
            Type = type,
            Risk = risk,
            RequiresApproval = requiresApproval,
            ParametersJson = parameters,
            Confidence = confidence,
            RawCommand = text
        };
    }

    private string? ParseParameters(string remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder)) return null;
        
        var ticketId = ExtractTicketId(remainder);
        var count = ExtractCount(remainder);
        
        var parts = new List<string>();
        if (ticketId.HasValue) parts.Add($"\"ticketId\":{ticketId}");
        if (count > 0) parts.Add($"\"count\":{count}");
        
        return parts.Count > 0 ? $"{{{string.Join(",", parts)}}}" : null;
    }

    private static int? ExtractTicketId(string text)
    {
        var match = TicketIdPattern().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
            return id;
        return null;
    }

    private static int ExtractCount(string text)
    {
        var match = CountPattern().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            return count;
        return 1;
    }

    private static string ExtractTime(string text)
    {
        // Simple time extraction - can be enhanced
        if (text.Contains("tomorrow") || text.Contains("завтра"))
            return DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd HH:mm");
        return DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm");
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?:ticket|тикет|задача|#)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TicketIdPattern();

    [GeneratedRegex(@"(\d+)\s*(?:задач|tasks|items)?", RegexOptions.IgnoreCase)]
    private static partial Regex CountPattern();
}
