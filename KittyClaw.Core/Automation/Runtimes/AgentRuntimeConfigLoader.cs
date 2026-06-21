using System.Text.Json;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class AgentRuntimeConfigLoader
{
    private readonly string _dataDir;

    public AgentRuntimeConfigLoader(string dataDir)
    {
        _dataDir = dataDir;
    }

    public AgentRuntimeProjectConfig? Load(string projectSlug)
    {
        var paths = new List<string>();
        paths.Add(Path.Combine(_dataDir, "runtimes", $"{projectSlug}.json"));

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<AgentRuntimeProjectConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                    });
                    if (config is not null) return config;
                }
                catch { /* best-effort fallback */ }
            }
        }

        return null;
    }

    public AgentRuntimeProjectConfig? Load(string projectSlug, string workspacePath)
    {
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(workspacePath))
        {
            paths.Add(Path.Combine(workspacePath, ".kittyclaw", "runtimes.json"));
        }
        paths.Add(Path.Combine(_dataDir, "runtimes", $"{projectSlug}.json"));

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<AgentRuntimeProjectConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                    });
                    if (config is not null) return config;
                }
                catch { /* best-effort fallback */ }
            }
        }

        return null;
    }

    public virtual AgentRuntimeProjectConfig CreateDefault(string projectSlug, string workspacePath, string defaultRuntime = "mimo-code")
    {
        return new AgentRuntimeProjectConfig
        {
            ProjectSlug = projectSlug,
            WorkspacePath = workspacePath,
            DefaultRuntime = defaultRuntime,
            HighRiskLabels = new[] { "security", "rls", "payments", "stripe", "critical" },
            RuntimeByMember = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "codex", "mimo-code" },
                { "mimo", "mimo-code" },
                { "opencode", "opencode" },
                { "qa", "script" },
                { "copilot", "github-copilot" },
                { "vibe", "vibe" },
                { "kimi", "kimi-code" },
                { "antigravity", "antigravity" },
            },
            Runtimes = new Dictionary<string, AgentRuntimeConfig>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "mimo-code", new AgentRuntimeConfig
                    {
                        Id = "mimo-code",
                        Enabled = true,
                        Command = "mimo",
                        Args = new[] { "run" },
                        PromptMode = PromptMode.Argument,
                        OutputFormat = "json",
                        TimeoutSeconds = 1800,
                        DangerouslySkipPermissions = false,
                    }
                },
                {
                    "opencode", new AgentRuntimeConfig
                    {
                        Id = "opencode",
                        Enabled = false,
                        Command = "opencode",
                        Args = new[] { "run" },
                        PromptMode = PromptMode.Argument,
                        TimeoutSeconds = 1800,
                        Experimental = true,
                    }
                },
                {
                    "codex", new AgentRuntimeConfig
                    {
                        Id = "codex",
                        Enabled = false,
                        Command = "codex",
                        Args = new[] { "exec" },
                        PromptMode = PromptMode.Argument,
                        TimeoutSeconds = 1800,
                        Experimental = true,
                    }
                },
                {
                    "vibe", new AgentRuntimeConfig
                    {
                        Id = "vibe",
                        Enabled = false,
                        Command = "vibe",
                        Args = new[] { "--prompt" },
                        PromptMode = PromptMode.Argument,
                        OutputFormat = "json",
                        Agent = "plan",
                        TimeoutSeconds = 1800,
                        AllowAutoApprove = false,
                        Experimental = true,
                    }
                },
                {
                    "kimi-code", new AgentRuntimeConfig
                    {
                        Id = "kimi-code",
                        Enabled = false,
                        Command = "kimi",
                        Args = new[] { "-p" },
                        PromptMode = PromptMode.Argument,
                        OutputFormat = "stream-json",
                        TimeoutSeconds = 1800,
                        AllowAutoApprove = false,
                        Experimental = true,
                    }
                },
                {
                    "github-copilot", new AgentRuntimeConfig
                    {
                        Id = "github-copilot",
                        Enabled = false,
                        Command = "copilot",
                        Args = new[] { "--prompt" },
                        PromptMode = PromptMode.Argument,
                        TimeoutSeconds = 1800,
                        Experimental = true,
                    }
                },
                {
                    "antigravity", new AgentRuntimeConfig
                    {
                        Id = "antigravity",
                        Enabled = false,
                        Command = "agy",
                        Args = Array.Empty<string>(),
                        PromptMode = PromptMode.Argument,
                        TimeoutSeconds = 1800,
                        Experimental = true,
                    }
                },
                {
                    "script", new AgentRuntimeConfig
                    {
                        Id = "script",
                        Enabled = true,
                        Command = "pnpm",
                        Args = new[] { "governance:verify" },
                        PromptMode = PromptMode.None,
                        TimeoutSeconds = 1800,
                    }
                },
            }
        };
    }

    public void Save(AgentRuntimeProjectConfig config)
    {
        var dir = Path.Combine(_dataDir, "runtimes");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{config.ProjectSlug}.json");
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        File.WriteAllText(path, json);
    }
}
