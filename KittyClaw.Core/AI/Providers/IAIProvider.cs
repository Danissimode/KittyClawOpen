using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KittyClaw.Core.AI.Providers;

/// <summary>
/// Base interface for all AI providers
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "opencode", "claude", "openai")
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Human-readable display name
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Description of the provider
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Whether this provider is available (configured and accessible)
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    
    /// <summary>
    /// List of available models for this provider
    /// </summary>
    Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Execute a chat completion
    /// </summary>
    Task<AIResponse> ChatAsync(AIRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Check if the provider is properly configured
    /// </summary>
    Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents an AI model
/// </summary>
public record AIModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProviderId { get; init; }
    public string? Description { get; init; }
    public string? ContextLength { get; init; }
    public string? Pricing { get; init; }
    public bool IsAvailable { get; init; } = true;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request for AI chat completion
/// </summary>
public record AIRequest
{
    public required string ModelId { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public string? SystemPrompt { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public string? StopSequence { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
}

/// <summary>
/// Response from AI provider
/// </summary>
public record AIResponse
{
    public required string Content { get; init; }
    public required string ModelId { get; init; }
    public required string ProviderId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public string? FinishReason { get; init; }
    public bool IsStreaming { get; init; }
    public Exception? Error { get; init; }
}

/// <summary>
/// Chat message
/// </summary>
public record ChatMessage
{
    public required MessageRole Role { get; init; }
    public required string Content { get; init; }
    public string? Name { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Message role
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// Health status of a provider
/// </summary>
public record ProviderHealthStatus
{
    public required bool IsHealthy { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}
