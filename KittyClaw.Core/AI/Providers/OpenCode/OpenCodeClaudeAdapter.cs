using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KittyClaw.Core.Automation;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.AI.Providers.OpenCode;

/// <summary>
/// Adapter to integrate OpenCode provider with existing ClaudeRunner infrastructure
/// This allows gradual migration from Claude CLI to OpenCode
/// </summary>
public class OpenCodeClaudeAdapter
{
    private readonly OpenCodeProvider _openCodeProvider;
    private readonly ILogger<OpenCodeClaudeAdapter> _logger;
    
    public OpenCodeClaudeAdapter(OpenCodeProvider openCodeProvider, ILogger<OpenCodeClaudeAdapter> logger)
    {
        _openCodeProvider = openCodeProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// Convert ClaudeRunContext to AIRequest for OpenCode
    /// </summary>
    private AIRequest ConvertToAIRequest(ClaudeRunContext ctx, string systemPrompt, List<ChatMessage> messages)
    {
        return new AIRequest
        {
            ModelId = ctx.Model ?? "gpt-4o", // Default to gpt-4o if not specified
            Messages = messages,
            SystemPrompt = systemPrompt,
            Temperature = 0.7,
            MaxTokens = ctx.MaxTurns * 100, // Approximate conversion
            StopSequences = new[] { "<|im_end|>", "<|im_start|>" }
        };
    }
    
    /// <summary>
    /// Build prompt from ClaudeRunContext (similar to ClaudeRunner.BuildPromptAsync)
    /// </summary>
    public async Task<string> BuildPromptAsync(ClaudeRunContext ctx, CancellationToken ct)
    {
        var sb = new StringBuilder();
        
        // Add system prompt
        if (!string.IsNullOrEmpty(ctx.InlineSkillContent))
        {
            sb.AppendLine(ctx.InlineSkillContent);
        }
        else if (ctx.SkillFile != null && File.Exists(ctx.SkillFile))
        {
            sb.AppendLine(await File.ReadAllTextAsync(ctx.SkillFile, ct));
        }
        
        // Add extra context
        if (!string.IsNullOrEmpty(ctx.ExtraContext))
        {
            sb.AppendLine();
            sb.AppendLine("### Additional Context:");
            sb.AppendLine(ctx.ExtraContext);
        }
        
        // Add ticket context
        if (ctx.TicketId.HasValue && !string.IsNullOrEmpty(ctx.TicketTitle))
        {
            sb.AppendLine();
            sb.AppendLine($"### Current Ticket: #{ctx.TicketId} - {ctx.TicketTitle}");
            if (!string.IsNullOrEmpty(ctx.TicketStatus))
            {
                sb.AppendLine($"Status: {ctx.TicketStatus}");
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Execute a chat completion using OpenCode instead of Claude CLI
    /// </summary>
    public async Task<AgentRun> RunAsync(ClaudeRunContext ctx, CancellationToken ct)
    {
        var run = new AgentRun
        {
            RunId = ctx.PresetRunId ?? Guid.NewGuid().ToString("N"),
            ProjectSlug = ctx.ProjectSlug,
            TicketId = ctx.TicketId,
            AgentName = ctx.AgentName,
            SkillFile = ctx.SkillFile,
            Status = AgentRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        
        try
        {
            // Build system prompt
            var systemPrompt = await BuildPromptAsync(ctx, ct);
            
            // Build messages
            var messages = new List<ChatMessage>();
            
            // Add system message
            messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = systemPrompt
            });
            
            // Add user message (extra context or default)
            if (!string.IsNullOrEmpty(ctx.ExtraContext))
            {
                messages.Add(new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = ctx.ExtraContext
                });
            }
            else
            {
                messages.Add(new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = "Please assist with the current task."
                });
            }
            
            // Create request
            var request = ConvertToAIRequest(ctx, systemPrompt, messages);
            
            // Execute
            var response = await _openCodeProvider.ChatAsync(request, ct);
            
            if (response.Error != null)
            {
                run.Status = AgentRunStatus.Failed;
                run.ExitCode = -1;
                run.ErrorMessage = response.Error.Message;
                throw response.Error;
            }
            
            // Process response
            run.Status = AgentRunStatus.Completed;
            run.ExitCode = 0;
            run.Output = response.Content;
            run.CompletedAt = DateTimeOffset.UtcNow;
            
            // Add token usage if available
            if (response.InputTokens.HasValue || response.OutputTokens.HasValue)
            {
                run.TokenUsage = new AgentTokenUsage
                {
                    InputTokens = response.InputTokens ?? 0,
                    OutputTokens = response.OutputTokens ?? 0,
                    TotalTokens = (response.InputTokens ?? 0) + (response.OutputTokens ?? 0)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode execution failed");
            run.Status = AgentRunStatus.Failed;
            run.ExitCode = -1;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            throw;
        }
        
        return run;
    }
    
    /// <summary>
    /// Stream chat completion using OpenCode
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> RunStreamAsync(
        ClaudeRunContext ctx, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        var run = new AgentRun
        {
            RunId = ctx.PresetRunId ?? Guid.NewGuid().ToString("N"),
            ProjectSlug = ctx.ProjectSlug,
            TicketId = ctx.TicketId,
            AgentName = ctx.AgentName,
            SkillFile = ctx.SkillFile,
            Status = AgentRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        
        try
        {
            // Build system prompt
            var systemPrompt = await BuildPromptAsync(ctx, ct);
            
            // Build messages
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = MessageRole.System, Content = systemPrompt },
                new ChatMessage { 
                    Role = MessageRole.User, 
                    Content = !string.IsNullOrEmpty(ctx.ExtraContext) ? ctx.ExtraContext : "Please assist with the current task." 
                }
            };
            
            // Create request
            var request = ConvertToAIRequest(ctx, systemPrompt, messages);
            
            // Stream responses
            var isFirst = true;
            await foreach (var response in _openCodeProvider.ChatStreamAsync(request, ct))
            {
                if (isFirst)
                {
                    yield return new StreamEvent
                    {
                        Kind = "start",
                        RunId = run.RunId,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    isFirst = false;
                }
                
                if (!string.IsNullOrEmpty(response.Content))
                {
                    yield return new StreamEvent
                    {
                        Kind = "text",
                        RunId = run.RunId,
                        Content = response.Content,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                }
                
                if (response.Error != null)
                {
                    yield return new StreamEvent
                    {
                        Kind = "error",
                        RunId = run.RunId,
                        Content = response.Error.Message,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    break;
                }
            }
            
            run.Status = AgentRunStatus.Completed;
            run.ExitCode = 0;
            run.CompletedAt = DateTimeOffset.UtcNow;
            
            yield return new StreamEvent
            {
                Kind = "end",
                RunId = run.RunId,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode streaming execution failed");
            run.Status = AgentRunStatus.Failed;
            run.ExitCode = -1;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            
            yield return new StreamEvent
            {
                Kind = "error",
                RunId = run.RunId,
                Content = ex.Message,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }
}
