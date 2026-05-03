using Experion.Api.Data;
using Experion.Api.Providers;
using Experion.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Data ─────────────────────────────────────────────────────────────
var dbPath = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=experion.db";
builder.Services.AddDbContext<ExperionDbContext>(opt => opt.UseSqlite(dbPath));

// ── Providers (mock by default; switch via Llm:Provider in appsettings) ──
var llmProvider = builder.Configuration["Llm:Provider"] ?? "Mock";
if (llmProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
{
    var aoaiSettings = new AzureOpenAISettings();
    builder.Configuration.GetSection("AzureOpenAI").Bind(aoaiSettings);
    builder.Services.AddSingleton(aoaiSettings);
    builder.Services.AddHttpClient<AzureOpenAIEmbeddingProvider>();
    builder.Services.AddHttpClient<AzureOpenAILlmProvider>();
    builder.Services.AddSingleton<IEmbeddingProvider>(sp => new AzureOpenAIEmbeddingProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureOpenAIEmbeddingProvider)), aoaiSettings));
    builder.Services.AddSingleton<ILlmProvider>(sp => new AzureOpenAILlmProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureOpenAILlmProvider)), aoaiSettings));
}
else
{
    builder.Services.AddSingleton<IEmbeddingProvider, MockEmbeddingProvider>();
    builder.Services.AddSingleton<ILlmProvider, MockLlmProvider>();
}

// ── Pipeline steps ───────────────────────────────────────────────────
builder.Services.AddScoped<ContextBuilderStep>();
builder.Services.AddScoped<SemanticCacheStep>();
builder.Services.AddScoped<IntentRouterStep>();
builder.Services.AddScoped<ActionTriggerStep>();
builder.Services.AddScoped<LlmGenerationStep>();
builder.Services.AddScoped<PersistStep>();

// ── Async lane (in-memory channels stand in for Service Bus) ─────────
builder.Services.AddSingleton<IActionDispatcher, ChannelActionDispatcher>();
builder.Services.AddSingleton<IActivityEventBus, ChannelActivityBus>();
builder.Services.AddHostedService<ActionWorker>();
builder.Services.AddHostedService<ActivityMiningWorker>();
builder.Services.AddScoped<RecommendationEngine>();

// ── Orchestrator ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IExperionService, ExperionOrchestrator>();

// ── SignalR ──────────────────────────────────────────────────────────
builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();
builder.Services.AddSignalR();

// ── Web ──────────────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── DB init + seed ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExperionDbContext>();
    await db.Database.EnsureCreatedAsync();
    var embed = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
    await Seeder.SeedAsync(db, embed);
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHub<ExperionHub>("/hubs/experion");

app.Run();
