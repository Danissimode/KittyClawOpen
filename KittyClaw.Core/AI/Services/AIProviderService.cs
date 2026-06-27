using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KittyClaw.Core.AI.Providers;
using KittyClaw.Core.AI.Providers.OpenCode;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.AI.Services;

/// <summary>
/// Service for managing AI provider configurations at different levels
/// </summary>
public interface IAIProviderService
{
    /// <summary>
    /// Get the effective provider configuration for a specific context
    /// </summary>
    Task<AIProviderConfig> GetEffectiveConfigAsync(
        string projectSlug, 
        int? ticketId = null, 
        string? agentName = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get available providers for a project
    /// </summary>
    Task<IReadOnlyList<ProviderInfo>> GetAvailableProvidersAsync(
        string projectSlug, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Get available models for a specific provider
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(
        string projectSlug, 
        string providerId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Set provider configuration for a project
    /// </summary>
    Task SetProjectProviderConfigAsync(
        string projectSlug, 
        ProjectAIConfig config,
        CancellationToken ct = default);
    
    /// <summary>
    /// Set provider configuration for a specific ticket
    /// </summary>
    Task SetTicketProviderConfigAsync(
        string projectSlug, 
        int ticketId,
        TicketAIConfig config,
        CancellationToken ct = default);
    
    /// <summary>
    /// Set provider configuration for a specific agent
    /// </summary>
    Task SetAgentProviderConfigAsync(
        string projectSlug, 
        string agentName,
        AgentAIConfig config,
        CancellationToken ct = default);
}

/// <summary>
/// AI Provider Configuration
/// </summary>
public record AIProviderConfig
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public string? CustomModelId { get; init; }
    public Dictionary<string, string> Options { get; init; } = new();
}

/// <summary>
/// Project-level AI configuration
/// </summary>
public record ProjectAIConfig
{
    public string? DefaultProviderId { get; init; }
    public string? DefaultModelId { get; init; }
    public Dictionary<string, string> ProviderOptions { get; init; } = new();
}

/// <summary>
/// Ticket-level AI configuration
/// </summary>
public record TicketAIConfig
{
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public bool OverrideProjectSettings { get; init; } = false;
}

/// <summary>
/// Agent-level AI configuration
/// </summary>
public record AgentAIConfig
{
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public bool OverrideProjectSettings { get; init; } = false;
}

/// <summary>
/// Provider information
/// </summary>
public record ProviderInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsConfigured { get; init; }
}

/// <summary>
/// Model information
/// </summary>
public record ModelInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProviderId { get; init; }
    public string? Description { get; init; }
    public bool IsAvailable { get; init; }
}

/// <summary>
/// Implementation of AI provider service
/// </summary>
public class AIProviderService : IAIProviderService
{
    private readonly IAIProviderFactory _providerFactory;
    private readonly ILogger<AIProviderService> _logger;
    
    // In-memory storage for now (can be replaced with database storage)
    private readonly Dictionary<string, ProjectAIConfig> _projectConfigs = new();
    private readonly Dictionary<(string, int), TicketAIConfig> _ticketConfigs = new();
    private readonly Dictionary<(string, string), AgentAIConfig> _agentConfigs = new();
    
    public AIProviderService(IAIProviderFactory providerFactory, ILogger<AIProviderService> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }
    
    public async Task<AIProviderConfig> GetEffectiveConfigAsync(
        string projectSlug, 
        int? ticketId = null, 
        string? agentName = null,
        CancellationToken ct = default)
    {
        // Priority order: Agent > Ticket > Project > Global Default
        
        // 1. Check agent-specific config
        if (agentName != null)
        {
            var agentKey = (projectSlug, agentName);
            if (_agentConfigs.TryGetValue(agentKey, out var agentConfig) && agentConfig.OverrideProjectSettings)
            {
                return new AIProviderConfig
                {
                    ProviderId = agentConfig.ProviderId ?? "opencode",
                    ModelId = agentConfig.ModelId ?? "gpt-4o",
                    Options = new Dictionary<string, string>()
                };
            }
        }
        
        // 2. Check ticket-specific config
        if (ticketId.HasValue)
        {
            var ticketKey = (projectSlug, ticketId.Value);
            if (_ticketConfigs.TryGetValue(ticketKey, out var ticketConfig) && ticketConfig.OverrideProjectSettings)
            {
                return new AIProviderConfig
                {
                    ProviderId = ticketConfig.ProviderId ?? "opencode",
                    ModelId = ticketConfig.ModelId ?? "gpt-4o",
                    Options = new Dictionary<string, string>()
                };
            }
        }
        
        // 3. Check project-specific config
        if (_projectConfigs.TryGetValue(projectSlug, out var projectConfig))
        {
            return new AIProviderConfig
            {
                ProviderId = projectConfig.DefaultProviderId ?? "opencode",
                ModelId = projectConfig.DefaultModelId ?? "gpt-4o",
                Options = projectConfig.ProviderOptions
            };
        }
        
        // 4. Return global default (OpenCode with default model)
        return new AIProviderConfig
        {
            ProviderId = "opencode",
            ModelId = "gpt-4o",
            Options = new Dictionary<string, string>()
        };
    }
    
    public async Task<IReadOnlyList<ProviderInfo>> GetAvailableProvidersAsync(
        string projectSlug, 
        CancellationToken ct = default)
    {
        var providers = _providerFactory.GetAllProviders();
        var result = new List<ProviderInfo>();
        
        foreach (var provider in providers)
        {
            var isAvailable = await provider.IsAvailableAsync(ct);
            
            result.Add(new ProviderInfo
            {
                Id = provider.Id,
                DisplayName = provider.DisplayName,
                Description = provider.Description,
                IsAvailable = isAvailable,
                IsConfigured = true // For now, all registered providers are considered configured
            });
        }
        
        return result;
    }
    
    public async Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(
        string projectSlug, 
        string providerId,
        CancellationToken ct = default)
    {
        try
        {
            var provider = _providerFactory.GetProvider(providerId);
            var models = await provider.GetAvailableModelsAsync(ct);
            
            return models.Select(m => new ModelInfo
            {
                Id = m.Id,
                Name = m.Name,
                ProviderId = m.ProviderId,
                Description = m.Description,
                IsAvailable = m.IsAvailable
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get models for provider {ProviderId}", providerId);
            return Array.Empty<ModelInfo>();
        }
    }
    
    public Task SetProjectProviderConfigAsync(
        string projectSlug, 
        ProjectAIConfig config,
        CancellationToken ct = default)
    {
        _projectConfigs[projectSlug] = config;
        _logger.LogInformation("Set AI provider config for project {ProjectSlug}", projectSlug);
        return Task.CompletedTask;
    }
    
    public Task SetTicketProviderConfigAsync(
        string projectSlug, 
        int ticketId,
        TicketAIConfig config,
        CancellationToken ct = default)
    {
        _ticketConfigs[(projectSlug, ticketId)] = config;
        _logger.LogInformation("Set AI provider config for ticket {TicketId} in project {ProjectSlug}", ticketId, projectSlug);
        return Task.CompletedTask;
    }
    
    public Task SetAgentProviderConfigAsync(
        string projectSlug, 
        string agentName,
        AgentAIConfig config,
        CancellationToken ct = default)
    {
        _agentConfigs[(projectSlug, agentName)] = config;
        _logger.LogInformation("Set AI provider config for agent {AgentName} in project {ProjectSlug}", agentName, projectSlug);
        return Task.CompletedTask;
    }
}
