using System;
using System.Collections.Generic;

namespace KittyClaw.Core.AI.Providers.OpenCode;

/// <summary>
/// Configuration for OpenCode provider
/// </summary>
public class OpenCodeConfig
{
    /// <summary>
    /// OpenCode Server URL (e.g., "http://localhost:1234" or "https://opencode.ai")
    /// </summary>
    public string ServerUrl { get; set; } = "http://localhost:1234";
    
    /// <summary>
    /// API key for authentication (if required)
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Default model to use when none specified
    /// </summary>
    public string? DefaultModel { get; set; }
    
    /// <summary>
    /// Timeout for API requests in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;
    
    /// <summary>
    /// Whether to use streaming mode by default
    /// </summary>
    public bool UseStreaming { get; set; } = true;
    
    /// <summary>
    /// Custom headers to include in requests
    /// </summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
    
    /// <summary>
    /// Proxy configuration
    /// </summary>
    public ProxyConfig? Proxy { get; set; }
    
    /// <summary>
    /// Whether to enable debug logging
    /// </summary>
    public bool Debug { get; set; } = false;
    
    /// <summary>
    /// Maximum number of retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Delay between retries in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
}

/// <summary>
/// Proxy configuration
/// </summary>
public class ProxyConfig
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseDefaultCredentials { get; set; } = false;
}

/// <summary>
/// Configuration for a specific provider within OpenCode
/// </summary>
public class ProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string> Options { get; set; } = new();
}

/// <summary>
/// Complete OpenCode settings including multiple providers
/// </summary>
public class OpenCodeSettings
{
    public OpenCodeConfig Server { get; set; } = new();
    public List<ProviderConfig> Providers { get; set; } = new();
    public Dictionary<string, string> ModelAliases { get; set; } = new();
}
