using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.AI.Providers.OpenCode;

/// <summary>
/// Client for OpenCode Server API
/// This provides direct access to OpenCode Server endpoints
/// </summary>
public class OpenCodeSdkClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenCodeSdkClient> _logger;
    private readonly OpenCodeConfig _config;
    
    public OpenCodeSdkClient(HttpClient httpClient, ILogger<OpenCodeSdkClient> logger, OpenCodeConfig config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
        
        // Configure base address
        if (!string.IsNullOrEmpty(config.ServerUrl))
        {
            _httpClient.BaseAddress = new Uri(config.ServerUrl.TrimEnd('/'));
        }
        
        // Set timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
    }
    
    /// <summary>
    /// Get list of all available models
    /// </summary>
    public async Task<List<OpenCodeModel>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/models", ct);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var models = JsonSerializer.Deserialize<List<OpenCodeModel>>(content, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return models ?? new List<OpenCodeModel>();
            }
            else
            {
                _logger.LogWarning("Failed to get models: {StatusCode} - {Content}", 
                    response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return new List<OpenCodeModel>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get models from OpenCode server");
            return new List<OpenCodeModel>();
        }
    }
    
    /// <summary>
    /// Get list of configured providers
    /// </summary>
    public async Task<List<OpenCodeProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/providers", ct);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var providers = JsonSerializer.Deserialize<List<OpenCodeProviderInfo>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return providers ?? new List<OpenCodeProviderInfo>();
            }
            else
            {
                _logger.LogWarning("Failed to get providers: {StatusCode}", response.StatusCode);
                return new List<OpenCodeProviderInfo>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get providers from OpenCode server");
            return new List<OpenCodeProviderInfo>();
        }
    }
    
    /// <summary>
    /// Get server health status
    /// </summary>
    public async Task<OpenCodeHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/health", ct);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var health = JsonSerializer.Deserialize<OpenCodeHealthStatus>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return health ?? new OpenCodeHealthStatus { Status = "unhealthy" };
            }
            else
            {
                return new OpenCodeHealthStatus { Status = "unhealthy", Error = response.ReasonPhrase };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health from OpenCode server");
            return new OpenCodeHealthStatus { Status = "unhealthy", Error = ex.Message };
        }
    }
    
    /// <summary>
    /// Send a chat completion request
    /// </summary>
    public async Task<OpenCodeChatResponse> ChatAsync(
        OpenCodeChatRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request, 
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/chat", content, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var chatResponse = JsonSerializer.Deserialize<OpenCodeChatResponse>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return chatResponse ?? new OpenCodeChatResponse { Error = "Invalid response" };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Chat request failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return new OpenCodeChatResponse { Error = errorContent };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat request");
            return new OpenCodeChatResponse { Error = ex.Message };
        }
    }
    
    /// <summary>
    /// Stream a chat completion request
    /// </summary>
    public async IAsyncEnumerable<OpenCodeStreamResponse> ChatStreamAsync(
        OpenCodeChatRequest request, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request, 
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = content
            };
            
            // Copy headers
            foreach (var header in _httpClient.DefaultRequestHeaders)
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            
            var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);
            
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                
                var buffer = new char[4096];
                var accumulatedData = string.Empty;
                
                while (!ct.IsCancellationRequested)
                {
                    var bytesRead = await reader.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break;
                    
                    var chunk = new string(buffer, 0, bytesRead);
                    accumulatedData += chunk;
                    
                    // Process complete JSON objects
                    while (TryExtractJsonObject(accumulatedData, out var jsonObject, out accumulatedData))
                    {
                        try
                        {
                            var streamResponse = JsonSerializer.Deserialize<OpenCodeStreamResponse>(jsonObject,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            if (streamResponse != null)
                            {
                                yield return streamResponse;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize stream chunk");
                        }
                    }
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Stream chat request failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                yield return new OpenCodeStreamResponse { Error = errorContent };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream chat request");
            yield return new OpenCodeStreamResponse { Error = ex.Message };
        }
    }
    
    private bool TryExtractJsonObject(string data, out string jsonObject, out string remaining)
    {
        jsonObject = string.Empty;
        remaining = data;
        
        if (string.IsNullOrEmpty(data))
            return false;
        
        // Simple JSON object extraction (for SSE or NDJSON)
        // This is a simplified approach - in production, use a proper JSON parser
        int openBraces = 0;
        int startIndex = -1;
        
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == '{')
            {
                if (openBraces == 0)
                {
                    startIndex = i;
                }
                openBraces++;
            }
            else if (data[i] == '}')
            {
                openBraces--;
                if (openBraces == 0 && startIndex >= 0)
                {
                    jsonObject = data.Substring(startIndex, i - startIndex + 1);
                    remaining = data.Substring(i + 1);
                    return true;
                }
            }
        }
        
        return false;
    }
}

/// <summary>
/// OpenCode model information
/// </summary>
public record OpenCodeModel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public long? ContextLength { get; set; }
    public string? Pricing { get; set; }
    public bool Available { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// OpenCode provider information
/// </summary>
public record OpenCodeProviderInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool Enabled { get; set; }
    public Dictionary<string, object>? Config { get; set; }
    public List<OpenCodeModel>? Models { get; set; }
}

/// <summary>
/// OpenCode health status
/// </summary>
public record OpenCodeHealthStatus
{
    public string? Status { get; set; }
    public string? Version { get; set; }
    public DateTime? Uptime { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// OpenCode chat request
/// </summary>
public record OpenCodeChatRequest
{
    public string? Model { get; set; }
    public List<OpenCodeMessage>? Messages { get; set; }
    public string? System { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public List<string>? Stop { get; set; }
    public bool Stream { get; set; } = false;
    public Dictionary<string, object>? Options { get; set; }
}

/// <summary>
/// OpenCode message
/// </summary>
public record OpenCodeMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// OpenCode chat response
/// </summary>
public record OpenCodeChatResponse
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public OpenCodeMessage? Message { get; set; }
    public string? FinishReason { get; set; }
    public OpenCodeUsage? Usage { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// OpenCode usage information
/// </summary>
public record OpenCodeUsage
{
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
}

/// <summary>
/// OpenCode stream response
/// </summary>
public record OpenCodeStreamResponse
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public OpenCodeMessage? Message { get; set; }
    public string? FinishReason { get; set; }
    public string? Error { get; set; }
}
