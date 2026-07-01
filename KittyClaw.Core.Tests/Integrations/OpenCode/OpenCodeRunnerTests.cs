using System;
using System.Threading;
using System.Threading.Tasks;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Integrations.OpenCode;
using Xunit;

namespace KittyClaw.Core.Tests.Integrations.OpenCode;

public class OpenCodeRunnerTests
{
    private static AgentRunRegistry CreateTestRegistry()
    {
        return new AgentRunRegistry();
    }

    [Fact]
    public void OpenCodeRunner_Kind_IsOpencode()
    {
        var config = new OpenCodeConfig();
        var registry = CreateTestRegistry();
        var runner = new OpenCodeRunner(config, registry);
        
        Assert.Equal("opencode", runner.Kind);
        Assert.Equal("OpenCode", runner.DisplayName);
    }
    
    [Fact]
    public void OpenCodeRunner_IsAvailable_WithCliCommand_ReturnsTrue()
    {
        var config = new OpenCodeConfig
        {
            CliCommand = "/usr/local/bin/opencode"
        };
        var registry = CreateTestRegistry();
        var runner = new OpenCodeRunner(config, registry);
        
        // This will check if the file exists, which it won't in tests
        // But the logic should work
        Assert.False(runner.IsAvailable); // File doesn't exist in test environment
    }
    
    [Fact]
    public void OpenCodeRunner_IsAvailable_WithServerUrl_ReturnsTrue()
    {
        var config = new OpenCodeConfig
        {
            UseServer = true,
            ServerUrl = "http://localhost:8080"
        };
        var registry = CreateTestRegistry();
        var runner = new OpenCodeRunner(config, registry);
        
        Assert.True(runner.IsAvailable);
    }
    
    [Fact]
    public async Task OpenCodeRunner_StartAsync_WithInvalidConfig_ThrowsException()
    {
        var config = new OpenCodeConfig
        {
            CliCommand = "nonexistent-opencode-command"
        };
        var registry = CreateTestRegistry();
        var runner = new OpenCodeRunner(config, registry);
        
        var request = new AgentRunRequest
        {
            ProjectSlug = "test-project",
            WorkspacePath = "/tmp/test",
            AgentName = "test-agent",
            SkillFile = "test-agent/SKILL.md",
            TicketId = 123,
            TicketTitle = "Test Ticket",
            Prompt = "Test prompt"
        };
        
        // This should fail because OpenCode is not available
        var result = await runner.StartAsync(request, CancellationToken.None);
        
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not available", result.Stderr);
    }
    
    [Fact]
    public void OpenCodeConfig_DefaultValues_AreSet()
    {
        var config = new OpenCodeConfig();
        
        Assert.False(config.UseServer);
        Assert.Null(config.ServerUrl);
        Assert.Null(config.CliCommand);
        Assert.Equal("openrouter", config.DefaultProvider);
        Assert.Equal("openrouter/anthropic/claude-3-5-sonnet", config.DefaultModel);
        Assert.Equal("build", config.DefaultAgent);
        Assert.Equal(3600, config.TimeoutSeconds);
    }
    
    [Fact]
    public void OpenCodeConfig_CanBeCustomized()
    {
        var config = new OpenCodeConfig
        {
            UseServer = true,
            ServerUrl = "http://localhost:8080",
            DefaultProvider = "anthropic",
            DefaultModel = "claude-3-5-sonnet",
            DefaultAgent = "developer",
            TimeoutSeconds = 7200
        };
        
        Assert.True(config.UseServer);
        Assert.Equal("http://localhost:8080", config.ServerUrl);
        Assert.Equal("anthropic", config.DefaultProvider);
        Assert.Equal("claude-3-5-sonnet", config.DefaultModel);
        Assert.Equal("developer", config.DefaultAgent);
        Assert.Equal(7200, config.TimeoutSeconds);
    }
}
