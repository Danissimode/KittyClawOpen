using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.AI.Providers.OpenCode;

/// <summary>
/// OpenCode AI Provider implementation
/// Supports all OpenCode providers: OpenAI, Claude, Minimax, DeepSeek, etc.
/// </summary>
public class OpenCodeProvider : IAIProvider
{
    private readonly ILogger<OpenCodeProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly OpenCodeConfig _config;
    
    // Cache for available models
    private IReadOnlyList<AIModel>? _cachedModels;
    private DateTimeOffset _modelsCacheTime = DateTimeOffset.MinValue;
    private readonly TimeSpan _modelsCacheDuration = TimeSpan.FromHours(1);
    
    // OpenCode Server API endpoints
    private const string ModelsEndpoint = "/api/models";
    private const string ChatEndpoint = "/api/chat";
    private const string HealthEndpoint = "/api/health";
    private const string ProvidersEndpoint = "/api/providers";
    
    public OpenCodeProvider(ILogger<OpenCodeProvider> logger, HttpClient httpClient, OpenCodeConfig config)
    {
        _logger = logger;
        _httpClient = httpClient;
        _config = config;
        
        // Configure HttpClient base address
        if (!string.IsNullOrEmpty(config.ServerUrl))
        {
            _httpClient.BaseAddress = new Uri(config.ServerUrl.TrimEnd('/'));
        }
        
        // Set default headers
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
        
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }
    
    public string Id => "opencode";
    public string DisplayName => "OpenCode";
    public string Description => "OpenCode AI Provider - Supports multiple LLM providers (OpenAI, Claude, Minimax, DeepSeek, etc.)";
    
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var health = await CheckHealthAsync(ct);
            return health.IsHealthy;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_config.ServerUrl))
            {
                return new ProviderHealthStatus
                {
                    IsHealthy = false,
                    ErrorMessage = "OpenCode server URL is not configured"
                };
            }
            
            var response = await _httpClient.GetAsync(HealthEndpoint, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var healthData = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                return new ProviderHealthStatus
                {
                    IsHealthy = true,
                    Details = healthData?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "")
                };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                return new ProviderHealthStatus
                {
                    IsHealthy = false,
                    ErrorMessage = $"Health check failed: {response.StatusCode} - {errorContent}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode health check failed");
            return new ProviderHealthStatus
            {
                IsHealthy = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // Return cached models if still valid
        if (_cachedModels != null && (DateTimeOffset.UtcNow - _modelsCacheTime) < _modelsCacheDuration)
        {
            return _cachedModels;
        }
        
        try
        {
            var response = await _httpClient.GetAsync(ModelsEndpoint, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var modelsData = await response.Content.ReadFromJsonAsync<JsonArray>(ct);
                var models = new List<AIModel>();
                
                if (modelsData != null)
                {
                    foreach (var modelNode in modelsData)
                    {
                        if (modelNode?["id"]?.GetValue<string>() is string modelId)
                        {
                            models.Add(new AIModel
                            {
                                Id = modelId,
                                Name = modelNode["name"]?.GetValue<string>() ?? modelId,
                                ProviderId = modelNode["provider"]?.GetValue<string>() ?? "unknown",
                                Description = modelNode["description"]?.GetValue<string>(),
                                ContextLength = modelNode["context_length"]?.GetValue<string>(),
                                Pricing = modelNode["pricing"]?.GetValue<string>(),
                                IsAvailable = modelNode["available"]?.GetValue<bool>() ?? true,
                                Metadata = modelNode["metadata"]?.AsObject()?.ToDictionary(
                                    kvp => kvp.Key, 
                                    kvp => kvp.Value?.ToString() ?? "")
                            });
                        }
                    }
                }
                
                // Also try to get models from providers endpoint
                if (models.Count == 0)
                {
                    var providersResponse = await _httpClient.GetAsync(ProvidersEndpoint, ct);
                    if (providersResponse.IsSuccessStatusCode)
                    {
                        var providersData = await providersResponse.Content.ReadFromJsonAsync<JsonArray>(ct);
                        if (providersData != null)
                        {
                            foreach (var providerNode in providersData)
                            {
                                if (providerNode?["models"] is JsonArray providerModels)
                                {
                                    foreach (var modelNode in providerModels)
                                    {
                                        if (modelNode?["id"]?.GetValue<string>() is string modelId)
                                        {
                                            models.Add(new AIModel
                                            {
                                                Id = modelId,
                                                Name = modelNode["name"]?.GetValue<string>() ?? modelId,
                                                ProviderId = providerNode["id"]?.GetValue<string>() ?? "unknown",
                                                Description = modelNode["description"]?.GetValue<string>(),
                                                ContextLength = modelNode["context_length"]?.GetValue<string>(),
                                                IsAvailable = modelNode["available"]?.GetValue<bool>() ?? true
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                _cachedModels = models;
                _modelsCacheTime = DateTimeOffset.UtcNow;
                
                _logger.LogInformation("Loaded {Count} models from OpenCode server", models.Count);
                return models;
            }
            else
            {
                _logger.LogWarning("Failed to load models from OpenCode: {StatusCode}", response.StatusCode);
                return Array.Empty<AIModel>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load OpenCode models");
            return Array.Empty<AIModel>();
        }
    }
    
    public async Task<AIResponse> ChatAsync(AIRequest request, CancellationToken ct = default)
    {
        try
        {
            // Build the request payload
            var payload = new
            {
                model = request.ModelId,
                messages = request.Messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Content,
                    name = m.Name
                }).ToArray(),
                system = request.SystemPrompt,
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stop = request.StopSequences ?? (request.StopSequence != null ? new[] { request.StopSequence } : null),
                stream = false // We'll handle streaming separately if needed
            };
            
            var startTime = DateTimeOffset.UtcNow;
            
            var response = await _httpClient.PostAsJsonAsync(ChatEndpoint, payload, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                
                var content = responseData?["message"]?["content"]?.GetValue<string>() ?? "";
                var modelId = responseData?["model"]?.GetValue<string>() ?? request.ModelId;
                var inputTokens = responseData?["usage"]?["prompt_tokens"]?.GetValue<long>();
                var outputTokens = responseData?["usage"]?["completion_tokens"]?.GetValue<long>();
                var finishReason = responseData?["finish_reason"]?.GetValue<string>();
                
                return new AIResponse
                {
                    Content = content,
                    ModelId = modelId,
                    ProviderId = Id,
                    GeneratedAt = startTime,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    FinishReason = finishReason,
                    IsStreaming = false
                };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("OpenCode chat failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                
                return new AIResponse
                {
                    Content = "",
                    ModelId = request.ModelId,
                    ProviderId = Id,
                    GeneratedAt = startTime,
                    Error = new Exception($"OpenCode API error: {response.StatusCode} - {errorContent}")
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode chat failed");
            return new AIResponse
            {
                Content = "",
                ModelId = request.ModelId,
                ProviderId = Id,
                GeneratedAt = DateTimeOffset.UtcNow,
                Error = ex
            };
        }
    }
    
    /// <summary>
    /// Stream chat completion (for real-time responses)
    /// </summary>
    public async IAsyncEnumerable<AIResponse> ChatStreamAsync(
        AIRequest request, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            // Build the request payload for streaming
            var payload = new
            {
                model = request.ModelId,
                messages = request.Messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Content,
                    name = m.Name
                }).ToArray(),
                system = request.SystemPrompt,
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stop = request.StopSequences ?? (request.StopSequence != null ? new[] { request.StopSequence } : null),
                stream = true
            };
            
            var requestContent = new StringContent(
                JsonSerializer.Serialize(payload), 
                Encoding.UTF8, 
                "application/json");
            
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint)
            {
                Content = requestContent
            };
            
            // Copy headers from HttpClient
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
                var accumulatedContent = new StringBuilder();
                var startTime = DateTimeOffset.UtcNow;
                
                while (!ct.IsCancellationRequested)
                {
                    var bytesRead = await reader.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break;
                    
                    var chunk = new string(buffer, 0, bytesRead);
                    accumulatedContent.Append(chunk);
                    
                    // Try to parse SSE or JSON chunks
                    // OpenCode typically returns JSON objects, possibly with SSE formatting
                    var lines = chunk.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("data:"))
                        {
                            var jsonData = line.StartsWith("data:") ? line[5..].Trim() : line.Trim();
                            
                            if (!string.IsNullOrEmpty(jsonData) && jsonData != "[DONE]")
                            {
                                try
                                {
                                    var jsonNode = JsonNode.Parse(jsonData);
                                    if (jsonNode != null)
                                    {
                                        var content = jsonNode["message"]?["content"]?.GetValue<string>() 
                                            ?? jsonNode["choices"]?[0]?["delta"]?["content"]?.GetValue<string>()
                                            ?? jsonNode["content"]?.GetValue<string>()
                                            ?? "";
                                        
                                        if (!string.IsNullOrEmpty(content))
                                        {
                                            yield return new AIResponse
                                            {
                                                Content = content,
                                                ModelId = request.ModelId,
                                                ProviderId = Id,
                                                GeneratedAt = startTime,
                                                IsStreaming = true
                                            };
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse OpenCode stream chunk");
                                }
                            }
                        }
                    }
                }
                
                // Final response with accumulated content
                yield return new AIResponse
                {
                    Content = accumulatedContent.ToString(),
                    ModelId = request.ModelId,
                    ProviderId = Id,
                    GeneratedAt = startTime,
                    IsStreaming = false,
                    FinishReason = "stop"
                };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("OpenCode stream failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                
                yield return new AIResponse
                {
                    Content = "",
                    ModelId = request.ModelId,
                    ProviderId = Id,
                    GeneratedAt = DateTimeOffset.UtcNow,
                    Error = new Exception($"OpenCode API error: {response.StatusCode} - {errorContent}")
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode stream failed");
            yield return new AIResponse
            {
                Content = "",
                ModelId = request.ModelId,
                ProviderId = Id,
                GeneratedAt = DateTimeOffset.UtcNow,
                Error = ex
            };
        }
    }
}
