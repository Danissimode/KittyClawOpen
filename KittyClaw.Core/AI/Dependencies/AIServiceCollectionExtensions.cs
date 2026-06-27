using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using KittyClaw.Core.AI.Providers;
using KittyClaw.Core.AI.Providers.OpenCode;
using KittyClaw.Core.AI.Services;

namespace KittyClaw.Core.AI.Dependencies;

/// <summary>
/// Extension methods for registering AI services in DI container
/// </summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Register all AI provider services
    /// </summary>
    public static IServiceCollection AddAIProviders(this IServiceCollection services)
    {
        return services
            .AddAIProviderFactory()
            .AddAIProviderService()
            .AddOpenCodeProvider();
    }
    
    /// <summary>
    /// Register AI provider factory
    /// </summary>
    public static IServiceCollection AddAIProviderFactory(this IServiceCollection services)
    {
        services.AddSingleton<IAIProviderFactory, AIProviderFactory>();
        return services;
    }
    
    /// <summary>
    /// Register AI provider service
    /// </summary>
    public static IServiceCollection AddAIProviderService(this IServiceCollection services)
    {
        services.AddSingleton<IAIProviderService, AIProviderService>();
        return services;
    }
    
    /// <summary>
    /// Register OpenCode provider
    /// </summary>
    public static IServiceCollection AddOpenCodeProvider(this IServiceCollection services)
    {
        // Register HttpClient for OpenCode
        services.AddHttpClient<OpenCodeProvider>((sp, client) =>
        {
            var config = sp.GetService<IOptions<OpenCodeConfig>>()?.Value ?? new OpenCodeConfig();
            if (!string.IsNullOrEmpty(config.ServerUrl))
            {
                client.BaseAddress = new Uri(config.ServerUrl.TrimEnd('/'));
            }
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        });
        
        // Register OpenCode config
        services.Configure<OpenCodeConfig>(config =>
        {
            // Default configuration
            config.ServerUrl = "http://localhost:1234";
            config.TimeoutSeconds = 600;
            config.UseStreaming = true;
        });
        
        return services;
    }
    
    /// <summary>
    /// Register OpenCode provider with custom configuration
    /// </summary>
    public static IServiceCollection AddOpenCodeProvider(
        this IServiceCollection services, 
        Action<OpenCodeConfig> configure)
    {
        services.Configure(configure);
        return services.AddOpenCodeProvider();
    }
    
    /// <summary>
    /// Register OpenCode Claude adapter for compatibility with existing ClaudeRunner
    /// </summary>
    public static IServiceCollection AddOpenCodeClaudeAdapter(this IServiceCollection services)
    {
        services.AddSingleton<OpenCodeClaudeAdapter>();
        return services;
    }
}
