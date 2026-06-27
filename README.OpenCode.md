# KittyClawOpen - OpenCode Integration

KittyClawOpen extends [KittyClaw](https://github.com/Ekioo/KittyClaw) with **upstream-friendly OpenCode integration**, enabling direct execution of OpenCode agents from tickets while maintaining full compatibility with the official repository.

## 🎯 Key Features

### ✅ Upstream-Friendly Architecture
- **No hard fork** - Regularly sync with official KittyClaw
- **Isolated integration** - OpenCode-specific code in separate namespace
- **Generic extension points** - Core-safe abstractions for all runners
- **Backward compatible** - Existing Claude workflows unchanged

### ✅ OpenCode Integration
- **Direct OpenCode execution** - Run OpenCode agents directly from tickets
- **Provider/model selection** - Choose from OpenAI, Anthropic, OpenRouter, Ollama, Mistral, Gemini, DeepSeek
- **OpenCode agents** - Use specialized agents (build, plan, review, test)
- **Worktree per card** - Isolated execution environment for each ticket
- **Execution metadata** - Track provider, model, session, worktree, branch
- **Policy gates** - Control execution modes based on risk level

### ✅ Execution Modes
- **LegacyClaude** - Existing ClaudeRunner behavior (default)
- **DirectOpenCode** - Direct OpenCode CLI/server execution
- **CaoGoverned** - CAO-governed execution (future)
- **TeamWorkflow** - Team-based decomposition (future)
- **Manual** - Manual execution mode

## 🏗️ Architecture

### Three-Zone Design

```
┌─────────────────────────────────────────┐
│  Zone A: Core-Safe Abstractions          │
│  (KittyClaw.Core/Automation/Runners/)    │
│  - IAgentRunner                         │
│  - RunnerRegistry                       │
│  - ITicketExecutionMetadataStore        │
│  - IExecutionPolicyService               │
│  - IProviderModelCatalog                 │
│  - IWorktreeService                      │
└─────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────┐
│  Zone B: OpenCode Integration            │
│  (KittyClaw.Core/Integrations/OpenCode/) │
│  - OpenCodeRunner                        │
│  - OpenCodeConfig                        │
│  - OpenCodeProviderModelCatalog          │
│  - OpenCodeExecutionPolicyService        │
│  - WorktreeService                       │
└─────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────┐
│  Zone C: CAO-Specific (Future)            │
│  (KittyClaw.Core/Integrations/Cao/)       │
│  - CaoRunner                             │
│  - CaoConfig                             │
└─────────────────────────────────────────┘
```

### Data Flow

```
KittyClaw UI
  ↓
Ticket / Card Execution Panel
  ↓
AutomationEngine
  ↓
RunnerRegistry (Zone A)
  ├── ClaudeRunnerAdapter → ClaudeRunner (existing)
  └── OpenCodeRunner → OpenCode CLI/Server (new)
  ↓
WorktreeService
  ↓
OpenCode session / CLI / server
  ↓
Run stream → KittyClaw run drawer
  ↓
Ticket metadata / comments / activity
```

## 🚀 Quick Start

### 1. Install OpenCode

```bash
# Install OpenCode CLI
npm install -g @opencode-ai/cli
# or
pnpm add -g @opencode-ai/cli
```

### 2. Configure Provider

```bash
# Authenticate with your provider
opencode auth login
# Select your provider (OpenRouter, Anthropic, etc.)
```

### 3. Create Automation

```json
{
  "type": "runAgent",
  "agent": "programmer",
  "executionMode": "DirectOpenCode",
  "provider": "openrouter",
  "model": "qwen/qwen3.5-coder",
  "opencodeAgent": "build",
  "useWorktree": true
}
```

### 4. Run It!

- Create a ticket
- Move to InProgress
- OpenCode will execute in a per-card worktree
- Output streams to the run drawer

## 📋 Configuration

### Project Configuration

```json
{
  "opencode": {
    "useServer": false,
    "cliCommand": "opencode",
    "defaultProvider": "openrouter",
    "defaultModel": "qwen/qwen3.5-coder",
    "defaultAgent": "build",
    "timeoutSeconds": 3600
  },
  "worktree": {
    "worktreeRoot": null,
    "autoCreate": true,
    "autoCleanup": false,
    "branchTemplate": "kc/KC-{ticketId}"
  }
}
```

### Supported Providers

| Provider | Tools | Vision | Local | Cost |
|----------|-------|--------|-------|------|
| OpenAI | ✅ | ✅ | ❌ | High |
| Anthropic | ✅ | ✅ | ❌ | High |
| OpenRouter | ✅ | ✅ | ❌ | Medium |
| Ollama | ❌ | ❌ | ✅ | Low |
| Mistral | ✅ | ❌ | ❌ | Medium |
| Gemini | ✅ | ✅ | ❌ | Medium |
| DeepSeek | ✅ | ❌ | ❌ | Medium |

## 🔧 Upstream Sync

KittyClawOpen is designed to **easily sync with official KittyClaw**:

```bash
# Add upstream remote
git remote add upstream https://github.com/Ekioo/KittyClaw.git

# Fetch and merge upstream changes
git fetch upstream
git checkout main
git merge upstream/main

# Resolve conflicts (should be minimal)
git add .
git commit -m "Merge upstream changes"

# Push to your fork
git push origin main
```

### Isolation Rules

✅ **Allowed**:
- Adding new interfaces in `Runners/` namespace
- Adding new files in `Integrations/OpenCode/`
- Adding new configuration classes
- Adding new services that implement generic interfaces

❌ **Not Allowed**:
- Adding OpenCode-specific logic to core classes
- Modifying existing project templates
- Changing existing schema without migrations
- Removing existing functionality

## 📚 Documentation

- [OpenCode Integration Guide](docs/OpenCode-Integration.md) - Detailed integration guide
- [Upstream Sync Policy](docs/Upstream-Sync-Policy.md) - How to maintain upstream compatibility
- [Architecture Overview](docs/Architecture.md) - Technical architecture details

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run specific test projects
dotnet test KittyClaw.Core.Tests

# Run OpenCode-specific tests
dotnet test KittyClaw.Core.Tests --filter "OpenCode"
```

## 📊 Execution Modes Comparison

| Feature | LegacyClaude | DirectOpenCode | CaoGoverned |
|---------|--------------|----------------|-------------|
| Backward Compatible | ✅ | ✅ | ❌ |
| Provider Selection | ❌ | ✅ | ✅ |
| Model Selection | ❌ | ✅ | ✅ |
| OpenCode Agents | ❌ | ✅ | ✅ |
| Worktree per Card | ❌ | ✅ | ✅ |
| Steering Support | ✅ | ✅ (server) | ✅ |
| CAO Closeout | ❌ | ❌ | ✅ |
| Done Gate | ❌ | ✅ | ✅ |
| Status | ✅ Stable | ✅ Stable | 🚧 Future |

## 🔮 Roadmap

### ✅ Completed (PR-4)
- Generic runner architecture (Zone A)
- OpenCode integration package (Zone B)
- RunnerRegistry integration with AutomationEngine
- Basic worktree support
- Provider/model catalog
- Execution policies
- Tests and documentation

### 🚧 Next Steps

#### PR-5: Worktree per Card Integration
- Full git worktree support
- Branch creation and management
- Merge and cleanup workflows
- Worktree isolation

#### PR-6: OpenCode Provider/Model Catalog
- Dynamic catalog refresh from OpenCode
- Provider health checks
- Model availability detection
- Auth status monitoring

#### PR-7: Steering Compatibility
- Server mode steering implementation
- CLI mode fallback behavior
- UI state management (supported/not-supported)

#### PR-8: Policies and Done Gate
- Full policy implementation
- Done gate enforcement
- Failure logbook
- Risk-based routing

#### PR-9: CAO Compatibility Mode
- CaoRunner implementation
- CAO task/run/evidence integration
- Closeout process

#### PR-10: Orchestration Center
- Centralized orchestration UI
- Role routing configuration
- Skills matrix
- Health monitoring dashboard

## 🤝 Contributing

### For OpenCode Integration

1. Add new files in `KittyClaw.Core/Integrations/OpenCode/`
2. Implement generic interfaces from `KittyClaw.Core/Automation/Runners/`
3. Register services in `Program.cs`
4. Test with both Claude and OpenCode runners

### For Core Changes

1. Only add generic interfaces in `Runners/` namespace
2. Don't add provider-specific logic
3. Maintain backward compatibility
4. Test with existing Claude functionality

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/Danissimode/KittyClawOpen/issues)
- **Discussions**: [GitHub Discussions](https://github.com/Danissimode/KittyClawOpen/discussions)
- **OpenCode**: [OpenCode Documentation](https://github.com/opencode-ai/opencode)

## 📄 License

This project extends KittyClaw and inherits its license. See [LICENSE](LICENSE) for details.

---

**KittyClawOpen** - The upstream-friendly OpenCode integration for KittyClaw

*Built with ❤️ for agentic development workflows*
