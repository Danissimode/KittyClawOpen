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
using KittyClaw.Web.Services;
using Microsoft.AspNetCore.Authentication;

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
    const string fallbackUrl = "http://127.0.0.1:5230";
    builder.WebHost.UseUrls(fallbackUrl);
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", fallbackUrl);
}

// Use BeaverBoardPaths for platform-aware storage directories.
// This handles macOS ~/Library/Application Support, Linux XDG, and Windows APPDATA.
BeaverBoardPaths.EnsureDirectories();
BeaverBoardPaths.MigrateFromLegacy();
var dataDir = BeaverBoardPaths.DataDir;
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
// Roster store for execution slots, presets, and fallback policies
var rosterStore = new RosterStore(dataDir);
rosterStore.Load();
builder.Services.AddSingleton(rosterStore);
// Ticket slot assignment store (global, not project-specific)
var ticketSlotStore = new TicketSlotAssignmentStore(dataDir);
ticketSlotStore.Load();
builder.Services.AddSingleton(ticketSlotStore);
// Auto-run deduplication store (persistent idempotency)
builder.Services.AddSingleton<AutoRunDeduplicationStore>(sp => 
    new AutoRunDeduplicationStore(dataDir, sp.GetRequiredService<ILogger<AutoRunDeduplicationStore>>()));
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

// AgentRuntimeRouter is currently unused and requires per-project config that
// can't be resolved as a singleton. Re-register with proper factory when needed.
// builder.Services.AddSingleton<AgentRuntimeRouter>();
builder.Services.AddSingleton<IAgentPromptBuilder, PromptBuilder>();

// Keep ClaudeRunner for backward compat (used by ClaudeCodeRuntime)
builder.Services.AddSingleton<ClaudeRunner>();
builder.Services.AddSingleton<CostTracker>();
// Token budget and cost economy: estimates context size, enforces role budgets,
// suggests fallback models, warns on large fanouts.
builder.Services.AddSingleton<TokenBudgetService>();
// API token authentication for IDE/integration access.
builder.Services.AddSingleton<ApiTokenService>();
// Register the custom authentication handler and wire up the "ApiToken" policy
// so that .RequireAuthorization("ApiToken") on IDE endpoints actually enforces
// Bearer-token validation instead of being silently ignored.
builder.Services.AddAuthentication("ApiToken")
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        "ApiToken", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiToken", policy =>
    {
        policy.AuthenticationSchemes.Add("ApiToken");
        policy.RequireAuthenticatedUser();
    });
});

// Zone A: Core extension points (generic, not OpenCode-specific)
builder.Services.AddSingleton<ITicketExecutionMetadataStore, TicketExecutionMetadataStore>();
// Persists ticket execution metadata (provider, model, worktree) to the metadata store
// whenever a run completes — both automation-triggered and ad-hoc runs survive restarts.
builder.Services.AddHostedService<TicketExecutionPersistenceService>();
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

// Run → TeamChat notifier: posts start/complete/fail/stop events into team chat
builder.Services.AddSingleton<TeamChatRunNotifier>();

// Auto-status updater: moves tickets to In Progress / Review / Failed on run events
builder.Services.AddSingleton<AutoTicketStatusUpdater>();

// Configure RunnerRegistry with all available runners
builder.Services.AddSingleton<RunnerRegistry>(sp =>
{
    var registry = new RunnerRegistry();
    var claudeRunner = sp.GetRequiredService<ClaudeRunner>();
    var opencodeRunner = sp.GetRequiredService<OpenCodeRunner>();
    
    registry.RegisterRunner(new ClaudeRunnerAdapter(claudeRunner));
    registry.RegisterRunner(opencodeRunner);
    
    // Respect user's saved preference first
    var settings = sp.GetService<SettingsService>();
    string? preferred = null;
    try
    {
        var data = settings?.LoadSync();
        preferred = data?.PreferredRunner;
    }
    catch { /* ignore settings read errors during startup */ }
    
    if (!string.IsNullOrEmpty(preferred) && preferred != "auto")
    {
        var runner = registry.GetRunner(preferred);
        if (runner is not null && runner.IsAvailable)
        {
            registry.SetDefaultRunner(preferred);
            return registry;
        }
    }
    
    // Fallback: OpenCode if available, otherwise Claude
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
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


var app = builder.Build();

// Force instantiation of event subscribers so they wire up to AgentRunRegistry
_ = app.Services.GetRequiredService<TeamChatRunNotifier>();
_ = app.Services.GetRequiredService<AutoTicketStatusUpdater>();

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

// Authentication & Authorization must precede CORS + endpoint mapping
// so that the ApiToken policy is evaluated for every protected endpoint.
app.UseAuthentication();
app.UseAuthorization();

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
        checks.Add(new { name = "dataDir", status = ddOk ? "ok" : "error", detail = new { writable = ddOk, pathKind = "default" } });
        if (!ddOk) allOk = false;
    }
    catch
    {
        checks.Add(new { name = "dataDir", status = "error", detail = new { writable = false, error = "access-denied" } });
        allOk = false;
    }
    
    // Uploads writable
    try
    {
        var testFile = Path.Combine(uploadsDir, ".health-check");
        File.WriteAllText(testFile, "ok");
        File.Delete(testFile);
        checks.Add(new { name = "uploads", status = "ok", detail = new { writable = true } });
    }
    catch
    {
        checks.Add(new { name = "uploads", status = "error", detail = new { writable = false, error = "access-denied" } });
        allOk = false;
    }
    
    // CORS mode (informational)
    checks.Add(new { name = "cors", status = "info", detail = "LocalOnly (127.0.0.1 only)" });
    
    // Port
    checks.Add(new { name = "port", status = "info", detail = ctx.Request.Host.Port?.ToString() ?? "unknown" });
    
    return Results.Ok(new
    {
        status = allOk ? "healthy" : "degraded",
        timestamp = DateTime.UtcNow,
        checks
    });
}).WithTags("Health").ExcludeFromDescription();

// Alias for launcher compatibility — same checks, shorter URL
app.MapGet("/health", (HttpContext ctx) =>
{
    var checks = new List<object>();
    bool allOk = true;
    
    checks.Add(new { name = "dotnet", status = "ok", detail = ".NET " + Environment.Version });
    
    try
    {
        var ddOk = Directory.Exists(dataDir) || Directory.CreateDirectory(dataDir) != null;
        checks.Add(new { name = "dataDir", status = ddOk ? "ok" : "error", detail = new { writable = ddOk } });
        if (!ddOk) allOk = false;
    }
    catch
    {
        checks.Add(new { name = "dataDir", status = "error", detail = new { writable = false } });
        allOk = false;
    }
    
    return Results.Ok(new { status = allOk ? "healthy" : "degraded", timestamp = DateTime.UtcNow, checks });
}).ExcludeFromDescription();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Write runtime port descriptor so the launcher (or a second .app instance)
// can discover and reuse the existing backend instead of spawning duplicates.
// The lock file also prevents concurrent backend startups from racing.
var currentPid = Environment.ProcessId;
File.WriteAllText(BeaverBoardPaths.PidFile, currentPid.ToString());

var portDescriptor = new PortDescriptor
{
    Host = "127.0.0.1",
    Port = 5230,
    Pid = currentPid,
    StartedAt = DateTime.UtcNow,
};
portDescriptor.Write(BeaverBoardPaths.PortFile);

app.Run();
