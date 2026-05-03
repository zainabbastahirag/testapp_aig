using Experion.Api.Data;
using Experion.Api.Providers;
using Experion.Api.Services;
using Experion.Api.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Data (SQL Server via SQLite for the demo) ─────────────────────────
var dbPath = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=experion.db";
builder.Services.AddDbContext<ExperionDbContext>(opt => opt.UseSqlite(dbPath));

// ── Blob store: Azure when connection string present, else local files ──
var azureBlobConn = builder.Configuration["BlobStorage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureBlobConn))
{
    builder.Services.AddSingleton<IBlobStore>(_ => new AzureBlobStore(azureBlobConn));
    Console.WriteLine("[Storage] Azure Blob Storage configured.");
}
else
{
    var configured = builder.Configuration["BlobStorage:LocalRoot"];
    var local = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(builder.Environment.ContentRootPath, "data", "blob")
        : configured;
    builder.Services.AddSingleton<IBlobStore>(_ => new LocalFileBlobStore(local));
    Console.WriteLine($"[Storage] Local file blob store at {local}");
}

// ── LLM + Embedding providers (Mock by default; AzureOpenAI swap-in) ──
var llmProvider = builder.Configuration["Llm:Provider"] ?? "Mock";
if (llmProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
{
    var aoai = new AzureOpenAISettings();
    builder.Configuration.GetSection("AzureOpenAI").Bind(aoai);
    builder.Services.AddSingleton(aoai);
    builder.Services.AddHttpClient<AzureOpenAIEmbeddingProvider>();
    builder.Services.AddHttpClient<AzureOpenAILlmProvider>();
    builder.Services.AddSingleton<IEmbeddingProvider>(sp => new AzureOpenAIEmbeddingProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureOpenAIEmbeddingProvider)), aoai));
    builder.Services.AddSingleton<ILlmProvider>(sp => new AzureOpenAILlmProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureOpenAILlmProvider)), aoai));
}
else
{
    builder.Services.AddSingleton<IEmbeddingProvider, MockEmbeddingProvider>();
    builder.Services.AddSingleton<ILlmProvider, MockLlmProvider>();
}

// ── Action queue + worker ─────────────────────────────────────────────
builder.Services.AddSingleton<IActionDispatcher, ChannelActionDispatcher>();
builder.Services.AddHostedService<ActionWorker>();

// ── Recommendation engine ─────────────────────────────────────────────
builder.Services.AddScoped<RecommendationEngine>();

// ── Flat ExperionService (one method per pipeline step) ───────────────
builder.Services.AddScoped<ExperionService>();

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

// ── DB init + seed ────────────────────────────────────────────────────
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
