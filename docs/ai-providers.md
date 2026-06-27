# AI Providers Integration

KittyClaw now supports multiple AI providers through the OpenCode ecosystem, allowing you to use various LLM providers (OpenAI, Claude, Minimax, DeepSeek, etc.) with a unified interface.

## Overview

The AI provider system allows you to:

- **Choose different providers** for different projects, tickets, or agents
- **Select specific models** from each provider
- **Configure provider settings** at different levels (global, project, ticket, agent)
- **Seamlessly integrate** with OpenCode Server for access to multiple providers

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    KittyClaw Application                        │
├─────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─────────────────┐    ┌─────────────────┐    ┌───────────┐ │
│  │  AIProvider      │    │  AIProvider      │    │  Claude   │ │
│  │  Factory         │    │  Service         │    │  Runner   │ │
│  └────────┬────────┘    └────────┬────────┘    └─────┬─────┘ │
│           │                      │                      │        │
│           ▼                      ▼                      ▼        │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    AI Providers                            │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌───────────────────┐  │ │
│  │  │ OpenCode    │  │ Claude Code  │  │ Future Providers  │  │ │
│  │  │ Provider    │  │ Provider     │  │ (OpenAI, etc.)    │  │ │
│  │  └─────────────┘  └─────────────┘  └───────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
│                              │                                  │
│                              ▼                                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    OpenCode Server                         │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌───────────────────┐  │ │
│  │  │ OpenAI      │  │ Claude      │  │ Minimax           │  │ │
│  │  └─────────────┘  └─────────────┘  └───────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Setup

### 1. Install OpenCode Server

First, you need to install and configure OpenCode Server:

```bash
# Install OpenCode CLI
npm install -g @opencode-ai/cli

# Or using curl
curl -fsSL https://opencode.ai/install | sh
```

### 2. Configure OpenCode Server

Create a configuration file `~/.opencode/config.json`:

```json
{
  "server": {
    "port": 1234,
    "host": "localhost",
    "apiKey": "your-api-key"
  },
  "providers": {
    "openai": {
      "apiKey": "your-openai-api-key",
      "baseUrl": "https://api.openai.com/v1"
    },
    "claude": {
      "apiKey": "your-claude-api-key",
      "baseUrl": "https://api.anthropic.com"
    },
    "minimax": {
      "apiKey": "your-minimax-api-key",
      "baseUrl": "https://api.minimax.chat/v1"
    }
  }
}
```

Start the OpenCode Server:

```bash
opencode server
```

### 3. Configure KittyClaw

In your KittyClaw `appsettings.json`, add the OpenCode configuration:

```json
{
  "OpenCode": {
    "ServerUrl": "http://localhost:1234",
    "TimeoutSeconds": 600,
    "UseStreaming": true,
    "DefaultModel": "gpt-4o"
  }
}
```

## Configuration Levels

KittyClaw supports AI provider configuration at multiple levels, with the following priority:

1. **Agent-level** (highest priority)
2. **Ticket-level**
3. **Project-level**
4. **Global default** (lowest priority)

### Project-level Configuration

Set the default provider and model for all tickets and agents in a project.

### Ticket-level Configuration

Override the project settings for a specific ticket. Useful when a particular ticket requires a different model or provider.

### Agent-level Configuration

Override settings for a specific agent. Useful when certain agents work better with specific models.

## Using Different Providers

### OpenCode Provider

The OpenCode provider supports all providers configured in your OpenCode Server:

- **OpenAI**: GPT-4, GPT-3.5, etc.
- **Claude**: Claude 3, Claude 2, etc.
- **Minimax**: MiniMax models
- **DeepSeek**: DeepSeek models
- **And many more**

### Example: Using Claude 3 via OpenCode

1. Configure Claude in your OpenCode Server
2. In KittyClaw, select "OpenCode" as the provider
3. Select "claude-3-sonnet-20240229" as the model

### Example: Using GPT-4 via OpenCode

1. Configure OpenAI in your OpenCode Server
2. In KittyClaw, select "OpenCode" as the provider
3. Select "gpt-4" as the model

## UI Components

### AI Provider Selector

The `AIProviderSelector` component allows users to select a provider and model:

```razor
<AIProviderSelector 
    ProjectSlug="my-project" 
    SelectedProviderId="@SelectedProvider" 
    SelectedProviderIdChanged="SelectedProvider = $event"
    SelectedModelId="@SelectedModel" 
    SelectedModelIdChanged="SelectedModel = $event" />
```

### Project AI Configuration

The `AIProjectConfig` component provides project-level AI settings:

```razor
<AIProjectConfig ProjectSlug="my-project" OnConfigSaved="HandleConfigSaved" />
```

### Ticket AI Configuration

The `AITicketConfig` component allows per-ticket AI settings:

```razor
<AITicketConfig ProjectSlug="my-project" TicketId="42" OnConfigSaved="HandleConfigSaved" />
```

### Agent AI Configuration

The `AIAgentConfig` component allows per-agent AI settings:

```razor
<AIAgentConfig ProjectSlug="my-project" AgentName="programmer" OnConfigSaved="HandleConfigSaved" />
```

## API Integration

### Using AI Providers in Code

```csharp
// Get the provider factory
var providerFactory = services.GetRequiredService<IAIProviderFactory>();

// Get a specific provider
var openCodeProvider = providerFactory.GetProvider("opencode");

// Check if provider is available
var isAvailable = await openCodeProvider.IsAvailableAsync();

// Get available models
var models = await openCodeProvider.GetAvailableModelsAsync();

// Execute a chat completion
var request = new AIRequest
{
    ModelId = "gpt-4o",
    Messages = new List<ChatMessage>
    {
        new ChatMessage { Role = MessageRole.System, Content = "You are a helpful assistant." },
        new ChatMessage { Role = MessageRole.User, Content = "Hello!" }
    },
    Temperature = 0.7,
    MaxTokens = 1000
};

var response = await openCodeProvider.ChatAsync(request);
```

### Using AI Provider Service

```csharp
// Get the AI provider service
var aiService = services.GetRequiredService<IAIProviderService>();

// Get effective configuration for a context
var config = await aiService.GetEffectiveConfigAsync("my-project", ticketId: 42, agentName: "programmer");

// Get available providers for a project
var providers = await aiService.GetAvailableProvidersAsync("my-project");

// Get available models for a provider
var models = await aiService.GetAvailableModelsAsync("my-project", "opencode");

// Set project-level configuration
await aiService.SetProjectProviderConfigAsync("my-project", new ProjectAIConfig
{
    DefaultProviderId = "opencode",
    DefaultModelId = "gpt-4o"
});

// Set ticket-level configuration
await aiService.SetTicketProviderConfigAsync("my-project", 42, new TicketAIConfig
{
    OverrideProjectSettings = true,
    ProviderId = "opencode",
    ModelId = "claude-3-sonnet-20240229"
});

// Set agent-level configuration
await aiService.SetAgentProviderConfigAsync("my-project", "programmer", new AgentAIConfig
{
    OverrideProjectSettings = true,
    ProviderId = "opencode",
    ModelId = "gpt-4"
});
```

## Migration from Claude CLI

If you're currently using Claude CLI directly, you can gradually migrate to the new provider system:

### Step 1: Install OpenCode Server

Set up OpenCode Server with your Claude API key.

### Step 2: Configure KittyClaw

Update your configuration to use OpenCode:

```json
{
  "OpenCode": {
    "ServerUrl": "http://localhost:1234",
    "DefaultModel": "claude-3-sonnet-20240229"
  }
}
```

### Step 3: Update Automation Configurations

Update your automation configurations to use the new provider system:

```json
{
  "actions": [
    {
      "type": "runAgent",
      "agent": "programmer",
      "provider": "opencode",
      "model": "claude-3-sonnet-20240229"
    }
  ]
}
```

### Step 4: Test and Validate

Test your configurations and ensure everything works as expected.

## Troubleshooting

### OpenCode Server Not Available

**Error**: "OpenCode server is not available"

**Solution**: 
1. Ensure OpenCode Server is running: `opencode server`
2. Check the server URL in your configuration
3. Verify the server is accessible from your KittyClaw instance

### No Models Available

**Error**: "No models available for provider"

**Solution**:
1. Check your OpenCode Server configuration
2. Ensure your provider API keys are valid
3. Verify your providers are properly configured in OpenCode

### Provider Not Found

**Error**: "Provider not found"

**Solution**:
1. Check the provider ID in your configuration
2. Ensure the provider is registered in the AIProviderFactory
3. Verify the provider is available

## Supported Providers

Through OpenCode, KittyClaw supports:

- **OpenAI**: GPT-4, GPT-3.5, etc.
- **Anthropic**: Claude 3, Claude 2, etc.
- **Minimax**: MiniMax models
- **DeepSeek**: DeepSeek models
- **Google**: Gemini models
- **Mistral**: Mistral models
- **And many more**

For a complete list, see the [OpenCode Providers Documentation](https://opencode.ai/docs/providers/).

## Future Enhancements

- **Direct provider integration**: Support for connecting directly to providers without OpenCode Server
- **Load balancing**: Automatic distribution of requests across multiple providers
- **Fallback mechanisms**: Automatic fallback to alternative providers when one fails
- **Cost tracking**: Track API usage and costs across different providers
- **Performance monitoring**: Monitor response times and quality across providers
