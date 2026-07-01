using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Health;

/// <summary>
/// Service for probing quota/token data from OpenCode ecosystem.
/// Supports opencode-quota CLI as optional external quota source.
/// 
/// Principle: quota/status/diagnostics live in telemetry layer,
/// NOT injected into agent prompts (zero context window pollution).
/// </summary>
public sealed class QuotaProbeService
{
    private readonly ProcessEventStore _eventStore;
    private readonly ILogger? _logger;
    private readonly string _opencodeQuotaCommand;

    public QuotaProbeService(
        ProcessEventStore eventStore,
        ILogger? logger = null,
        string opencodeQuotaCommand = "opencode-quota")
    {
        _eventStore = eventStore;
        _logger = logger;
        _opencodeQuotaCommand = opencodeQuotaCommand;
    }

    /// <summary>
    /// Check if opencode-quota CLI is available.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _opencodeQuotaCommand,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            
            var completed = await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            return completed && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Probe quota status from opencode-quota CLI.
    /// Returns normalized quota data or null if unavailable.
    /// </summary>
    public async Task<QuotaProbeResult?> ProbeAsync(string projectSlug, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct))
        {
            await _eventStore.RecordAsync(new ProcessEvent
            {
                ProjectSlug = projectSlug,
                Level = ProcessEventLevels.Info,
                Category = ProcessEventCategories.Quota,
                EventType = ProcessEventTypes.QuotaSourceMissing,
                Title = "opencode-quota CLI not available",
                Message = "Install opencode-quota for quota monitoring: npm install -g opencode-quota",
                Source = "quota-probe",
                SuggestedActionsJson = "[\"install-opencode-quota\",\"configure-quota-source\"]"
            }, ct);
            
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _opencodeQuotaCommand,
                Arguments = "show --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process is null) return null;
            
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode != 0)
            {
                _logger?.LogWarning("opencode-quota failed with exit code {ExitCode}: {Error}", 
                    process.ExitCode, stderr);
                
                await _eventStore.RecordAsync(new ProcessEvent
                {
                    ProjectSlug = projectSlug,
                    Level = ProcessEventLevels.Warning,
                    Category = ProcessEventCategories.Quota,
                    EventType = ProcessEventTypes.QuotaProbeFailed,
                    Title = "opencode-quota probe failed",
                    Message = $"Exit code {process.ExitCode}: {stderr}",
                    Source = "quota-probe",
                    RawPayload = stderr
                }, ct);
                
                return null;
            }

            return ParseQuotaOutput(projectSlug, stdout);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to probe quota");
            return null;
        }
    }

    /// <summary>
    /// Parse opencode-quota JSON output into normalized format.
    /// </summary>
    private QuotaProbeResult? ParseQuotaOutput(string projectSlug, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var result = new QuotaProbeResult
            {
                ProjectSlug = projectSlug,
                ProbedAt = DateTimeOffset.UtcNow,
                Providers = new List<QuotaProviderStatus>()
            };

            if (root.TryGetProperty("providers", out var providers))
            {
                foreach (var provider in providers.EnumerateArray())
                {
                    var status = new QuotaProviderStatus
                    {
                        Provider = provider.GetProperty("provider").GetString() ?? "",
                        Account = provider.TryGetProperty("account", out var acc) ? acc.GetString() : null,
                        ModelFamily = provider.TryGetProperty("model_family", out var fam) ? fam.GetString() : null,
                        RemainingPercent = provider.TryGetProperty("remaining_percent", out var rem) ? rem.GetInt32() : null,
                        RemainingBalance = provider.TryGetProperty("remaining_balance", out var bal) ? bal.GetString() : null,
                        Window = provider.TryGetProperty("window", out var win) ? win.GetString() : null,
                        Status = provider.TryGetProperty("status", out var stat) ? stat.GetString() : "unknown",
                        LastCheckedAt = DateTimeOffset.UtcNow
                    };
                    
                    result.Providers.Add(status);
                    
                    // Check for threshold warnings
                    if (status.RemainingPercent.HasValue && status.RemainingPercent.Value < 15)
                    {
                        _ = _eventStore.RecordAsync(new ProcessEvent
                        {
                            ProjectSlug = projectSlug,
                            Level = status.RemainingPercent.Value < 5 
                                ? ProcessEventLevels.Critical 
                                : ProcessEventLevels.Warning,
                            Category = ProcessEventCategories.Quota,
                            EventType = ProcessEventTypes.QuotaThresholdReached,
                            Title = $"{status.Provider} quota below {status.RemainingPercent}%",
                            Message = $"Account: {status.Account}, Window: {status.Window}",
                            Provider = status.Provider,
                            Source = "quota-probe",
                            SuggestedActionsJson = "[\"switch-executor\",\"lower-max-turns\"]"
                        }).ConfigureAwait(false);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse quota output");
            return null;
        }
    }
}

/// <summary>
/// Normalized quota probe result.
/// </summary>
public sealed class QuotaProbeResult
{
    public string ProjectSlug { get; init; } = "";
    public DateTimeOffset ProbedAt { get; init; }
    public List<QuotaProviderStatus> Providers { get; init; } = new();
}

/// <summary>
/// Quota status for a single provider.
/// </summary>
public sealed class QuotaProviderStatus
{
    public string Provider { get; init; } = "";
    public string? Account { get; init; }
    public string? ModelFamily { get; init; }
    public int? RemainingPercent { get; init; }
    public string? RemainingBalance { get; init; }
    public string? Window { get; init; }
    public string Status { get; init; } = "unknown";
    public DateTimeOffset LastCheckedAt { get; init; }
}
