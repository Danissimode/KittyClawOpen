using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KittyClaw.Core.AI.Providers.OpenCode;

namespace KittyClaw.Core.AI.Providers;

/// <summary>
/// Factory for creating AI providers
/// </summary>
public interface IAIProviderFactory
{
    IAIProvider GetProvider(string providerId);
    IReadOnlyList<IAIProvider> GetAllProviders();
    Task<IReadOnlyList<IAIProvider>> GetAvailableProvidersAsync(CancellationToken ct = default);
    Task<IAIProvider?> GetProviderForModelAsync(string modelId, CancellationToken ct = default);
}

/// <summary>
/// Implementation of AI provider factory
/// </summary>
public class AIProviderFactory : IAIProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIProviderFactory> _logger;
    private readonly Dictionary<string, IAIProvider> _providers = new();
    
    public AIProviderFactory(IServiceProvider serviceProvider, ILogger<AIProviderFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        InitializeProviders();
    }
    
    private void InitializeProviders()
    {
        try
        {
            var httpClient = _serviceProvider.GetRequiredService<HttpClient>();
            var openCodeConfig = _serviceProvider.GetService<IOptions<OpenCodeConfig>>()?.Value ?? new OpenCodeConfig();
            var openCodeLogger = _serviceProvider.GetRequiredService<ILogger<OpenCodeProvider>>();
            
            var openCodeProvider = new OpenCodeProvider(openCodeLogger, httpClient, openCodeConfig);
            _providers[openCodeProvider.Id] = openCodeProvider;
            _logger.LogInformation("Registered OpenCode provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AI providers");
        }
    }
    
    public IAIProvider GetProvider(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }
        throw new KeyNotFoundException($
    public IReadOnlyList<IAIProvider> GetAllProviders()
    {
        return _providers.Values.ToList();
    }
    
    public async Task<IReadOnlyList<IAIProvider>> GetAvailableProvidersAsync(CancellationToken ct = default)
    {
        var availableProviders = new List<IAIProvider>();
        
        foreach (var provider in _providers.Values)
        {
            if (await provider.IsAvailableAsync(ct))
            {
                availableProviders.Add(provider);
            }
        }
        
        return availableProviders;
    }
    
    public async Task<IAIProvider?> GetProviderForModelAsync(string modelId, CancellationToken ct = default)
    {
        foreach (var provider in _providers.Values)
        {
            var models = await provider.GetAvailableModelsAsync(ct);
            if (models.Any(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase)))
            {
                return provider;
            }
        }
        
        return null;
    }
}
