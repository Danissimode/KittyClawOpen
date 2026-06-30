using System.Text.Json;
using System.Text.Json.Serialization;
using KittyClaw.Core.Automation;
using KittyClaw.Core.Automation.Runners;
using KittyClaw.Core.Integrations.OpenCode;
using KittyClaw.Core.Automation.Runtimes;
using KittyClaw.Core.Services;
using KittyClaw.Core.TeamChat;
using KittyClaw.Web.Api;
using KittyClaw.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Default to HTTP-only on :5230 when no URL config is provided. Beaver Board is a local-only
// app with no HTTPS cert, so the framework default (HTTP + HTTPS dual binding on :5000/:5001)
// is wrong here. 5230 is the historical port — kept for backward compatibility
// with existing skills, bookmarks, and external integrations that point at it.
//
// Only kick in when nothing else (ASPNETCORE_URLS, launchSettings.applicationUrl, --urls,
// urls config key) has set the URL — otherwise UseUrls() called after CreateBuilder would
// overwrite that config and break the qa launch profile, QaRunner test instances, etc.
//
// Also propagate to ASPNETCORE_URLS so downstream consumers that read the env var directly
// (e.g. ClaudeRunner.ResolveApiUrl, which builds the API URL passed to skills) see the same
// port Kestrel is actually binding.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    const string fallbackUrl = "http://localhost:5230";
    builder.WebHost.UseUrls(fallbackUrl);
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", fallbackUrl);
}

// BEAVERBOARD_DATA_DIR: primary data directory override.
// Falls back to KITTYCLAW_DATA_DIR (backward compat with existing setups).
// Defaults to %APPDATA%/BeaverBoard/ — not KittyClaw, for clean rebrand.
// Legacy TodoApp path is migrated to the active data dir on first run.
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var dataDir = Environment.GetEnvironmentVariable("BEAVERBOARD_DATA_DIR")
    ?? Environment.GetEnvironmentVariable("KITTYCLAW_DATA_DIR")
    ?? Path.Combine(appData, "BeaverBoard");
var legacyTodoAppDir = Path.Combine(appData, "TodoApp");
if (!Directory.Exists(dataDir) && Directory.Exists(legacyTodoAppDir))
{
    Directory.Move(legacyTodoAppDir, dataDir);
}
var appSettings = new KittyClaw.Core.Services.AppSettingsService(dataDir);
builder.Services.AddSingleton(appSettings);
builder.Services.AddSingleton(new KittyClaw.Core.Services.LocalizationService(appSettings));
builder.Services.AddSingleton(new ProjectService(dataDir));
builder.Services.AddSingleton<TicketService>();
builder.Services.AddSingleton<LabelService>();
builder.Services.AddSingleton<ColumnService>();
builder.Services.AddSingleton<MemberService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<DashboardService>();
builder.Services.AddSingleton<AgentsTemplateService>();
builder.Services.AddScoped<KittyClaw.Web.Services.BoardFilterState>();
builder.Services.AddScoped<KittyClaw.Web.Services.BoardSortState>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<KittyClaw.Web.Services.BoardUpdateNotifier>();
builder.Services.AddScoped<KittyClaw.Web.Services.EscapeKeyStack>();

// Automation engine
builder.Services.AddSingleton<AutomationStore>();
builder.Services.AddSingleton<TriggerStateStore>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton(new RunLogStore(dataDir));
builder.Services.AddSingleton<AgentRunRegistry>(sp => new AgentRunRegistry(sp.GetRequiredService<RunLogStore>()));
// Cap concurrent claude subprocesses across all projects (chats bypass). Override with the
// KITTYCLAW_MAX_CONCURRENT_AGENTS env var if 3 is too tight or too loose for the host.
var maxConcurrent = int.TryParse(Environment.GetEnvironmentVariable("KITTYCLAW_MAX_CONCURRENT_AGENTS"), out var mc) && mc > 0 ? mc : 3;
builder.Services.AddSingleton(new RunConcurrencyGate(maxConcurrent));

// Runtime config
builder.Services.AddSingleton<AgentRuntimeConfigLoader>(sp => new AgentRuntimeConfigLoader(dataDir));
// Default workspace: use BEAVERBOARD_DEFAULT_WORKSPACE env var, or fall back to app root.
// User must configure their own projects via the UI.

// Runtimes (all implement IAgentRuntime)
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<IAgentRuntime, ClaudeCodeRuntime>();
builder.Services.AddSingleton<IAgentRuntime, OpenCodeRuntime>();

// Stubs below — commented out until real implementation exists.
// These create a false impression of broad runtime support.
// Uncomment and implement when ready to add a new runner.
// builder.Services.AddSingleton<IAgentRuntime, MimoCodeRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, ScriptRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, CodexRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, GitHubCopilotRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, AntigravityRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, VibeRuntime>();
// builder.Services.AddSingleton<IAgentRuntime, KimiCodeRuntime>();

builder.Services.AddSingleton<AgentRuntimeRouter>();
builder.Services.AddSingleton<IAgentPromptBuilder, PromptBuilder>();

// Keep ClaudeRunner for backward compat (used by ClaudeCodeRuntime)
builder.Services.AddSingleton<ClaudeRunner>();
builder.Services.AddSingleton<CostTracker>();

// Zone A: Core extension points (generic, not OpenCode-specific)
builder.Services.AddSingleton<ITicketExecutionMetadataStore, TicketExecutionMetadataStore>();
builder.Services.AddSingleton<IProviderModelCatalog, OpenCodeProviderModelCatalog>();
builder.Services.AddSingleton<IExecutionPolicyService, OpenCodeExecutionPolicyService>();

// Zone B: OpenCode integration
builder.Services.AddSingleton<OpenCodeConfig>();
builder.Services.AddSingleton<OpenCodePolicyConfig>();
// OpenCodeRunner now requires AgentRunRegistry for full integration
builder.Services.AddSingleton<OpenCodeRunner>(sp =>
    new OpenCodeRunner(
        sp.GetRequiredService<OpenCodeConfig>(),
        sp.GetRequiredService<AgentRunRegistry>(),
        sp.GetRequiredService<ILogger<OpenCodeRunner>>(),
        sp.GetService<IProviderModelCatalog>()
    ));
builder.Services.AddSingleton<IWorktreeService, WorktreeService>();

// Failure logbook (SQLite-backed, persists across restarts)
builder.Services.AddSingleton<FailureLogStore>(sp => new FailureLogStore(dataDir));

// Team Chat
builder.Services.AddSingleton<ITeamChatService>(sp => new TeamChatService(dataDir));
builder.Services.AddSingleton<ITeamCommandRouter, TeamCommandRouter>();
builder.Services.AddSingleton<IRunSteeringBridge, RunSteeringBridge>();
builder.Services.AddSingleton<ITeamChatMentionParser, TeamChatMentionParser>();
builder.Services.AddSingleton<IAgentChatSignalFilter, AgentChatSignalFilter>();
builder.Services.AddSingleton<IAgentChatPolicyService>(sp => new AgentChatPolicyService(dataDir));
builder.Services.AddSingleton<IAgentCommunicationService, AgentCommunicationService>();

// Configure RunnerRegistry with all available runners
builder.Services.AddSingleton<RunnerRegistry>(sp =>
{
    var registry = new RunnerRegistry();
    var claudeRunner = sp.GetRequiredService<ClaudeRunner>();
    var opencodeRunner = sp.GetRequiredService<OpenCodeRunner>();
    
    registry.RegisterRunner(new ClaudeRunnerAdapter(claudeRunner));
    registry.RegisterRunner(opencodeRunner);
    
    // Set OpenCode as default if available
    if (opencodeRunner.IsAvailable)
    {
        registry.SetDefaultRunner("opencode");
    }
    
    return registry;
});
builder.Services.AddSingleton<RunnerAvailabilityChecker>();
builder.Services.AddSingleton<AutomationEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationEngine>());
builder.Services.AddSingleton<TicketAutoRunService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketAutoRunService>());
builder.Services.AddSingleton<GitRepositoryWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GitRepositoryWatcher>());
builder.Services.AddSingleton<KittyClaw.Core.Services.DashboardTileGate>();
builder.Services.AddSingleton<KittyClaw.Core.Services.DashboardScriptRunner>();
builder.Services.AddSingleton<KittyClaw.Core.Services.DashboardRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KittyClaw.Core.Services.DashboardRefreshService>());
builder.Services.AddSingleton<KittyClaw.Web.Services.AgentRunsState>();
builder.Services.AddSingleton<KittyClaw.Web.Services.RunnerStatusState>();
builder.Services.AddSingleton<KittyClaw.Web.Services.ToastService>();
builder.Services.AddScoped<KittyClaw.Web.Services.TeamChatState>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<KittyClaw.Web.Services.UpdateCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KittyClaw.Web.Services.UpdateCheckService>());

// Folder picker: only on Windows hosts (local or MAUI-Windows). Cloud deployments
// register nothing, so the UI hides the Parcourir button.
if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<KittyClaw.Core.Platform.IFolderPicker, KittyClaw.Core.Platform.WindowsFolderPicker>();

builder.Services.AddCors(options =>
{
    // LocalOnly: only localhost and 127.0.0.1 on the app port are allowed.
    // This is a local-only tool — do not expose this port publicly.
    options.AddPolicy("LocalOnly", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5230",
                "http://127.0.0.1:5230"
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
    
    // AllowAll is kept only for health/doctor endpoints that need zero-CORS.
    // All other endpoints must use "LocalOnly".
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Serve uploaded images
var uploadsDir = Path.Combine(dataDir, "uploads");
Directory.CreateDirectory(uploadsDir);
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads"
});

app.UseAntiforgery();

app.UseCors("LocalOnly");
app.MapOpenApi();
app.MapTodoApi();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dev/update-check/simulate", (string version, KittyClaw.Web.Services.UpdateCheckService svc) =>
    {
        svc.SimulateUpdate(version);
        return Results.Ok(new { simulated = version });
    }).ExcludeFromDescription();
    app.MapPost("/api/dev/update-check/reset", (KittyClaw.Web.Services.UpdateCheckService svc) =>
    {
        svc.ResetSimulation();
        return Results.Ok(new { reset = true });
    }).ExcludeFromDescription();
}

app.MapGet("/api/docs", async (HttpContext ctx) =>
{
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    using var client = new HttpClient();
    var json = await client.GetStringAsync($"{baseUrl}/openapi/v1.json");
    using var doc = JsonDocument.Parse(json);
    var markdown = OpenApiMarkdownGenerator.Generate(doc);
    return Results.Text(markdown, "text/markdown; charset=utf-8");
}).ExcludeFromDescription();

// Health endpoint — no CORS required (GET only, no sensitive data)
app.MapGet("/api/health", (HttpContext ctx) =>
{
    var checks = new List<object>();
    bool allOk = true;
    
    // .NET runtime
    checks.Add(new { name = "dotnet", status = "ok", detail = ".NET " + Environment.Version });
    
    // Data directory
    try
    {
        var ddOk = Directory.Exists(dataDir) || Directory.CreateDirectory(dataDir) != null;
        checks.Add(new { name = "dataDir", status = ddOk ? "ok" : "error", detail = dataDir });
        if (!ddOk) allOk = false;
    }
    catch (Exception ex)
    {
        checks.Add(new { name = "dataDir", status = "error", detail = ex.Message });
        allOk = false;
    }
    
    // Uploads writable
    try
    {
        var testFile = Path.Combine(uploadsDir, ".health-check");
        File.WriteAllText(testFile, "ok");
        File.Delete(testFile);
        checks.Add(new { name = "uploads", status = "ok", detail = uploadsDir });
    }
    catch (Exception ex)
    {
        checks.Add(new { name = "uploads", status = "error", detail = ex.Message });
        allOk = false;
    }
    
    // CORS mode (informational)
    checks.Add(new { name = "cors", status = "info", detail = "LocalOnly (localhost + 127.0.0.1 only)" });
    
    // Port
    checks.Add(new { name = "port", status = "info", detail = ctx.Request.Host.Port?.ToString() ?? "unknown" });
    
    return Results.Ok(new
    {
        status = allOk ? "healthy" : "degraded",
        timestamp = DateTime.UtcNow,
        checks
    });
}).WithTags("Health").ExcludeFromDescription();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
