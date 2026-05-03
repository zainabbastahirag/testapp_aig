using ExperionAgent.API.Middleware;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Infrastructure.AI;
using ExperionAgent.Infrastructure.Configuration;
using ExperionAgent.Infrastructure.Identity;
using ExperionAgent.Infrastructure.Mining;
using ExperionAgent.Infrastructure.Recommendations;
using ExperionAgent.Infrastructure.Search;
using ExperionAgent.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// ── CORS (allow any origin — SDK runs on host websites) ─────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ── Configuration Binding ────────────────────────────────────────────
builder.Services.Configure<ExperionSettings>(builder.Configuration.GetSection("Experion"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<SearchSettings>(builder.Configuration.GetSection("Search"));
builder.Services.Configure<RecommendationSettings>(builder.Configuration.GetSection("Recommendation"));

// ── Core Services ────────────────────────────────────────────────────
builder.Services.AddSingleton<IIdentityResolver, IdentityResolver>();
builder.Services.AddSingleton<IActivityStore, BlobActivityStore>();
builder.Services.AddSingleton<ConversationStore>();

builder.Services.AddScoped<IActivityMiner, ActivityMiner>();
builder.Services.AddScoped<IRecommendationEngine, RecommendationEngine>();
builder.Services.AddScoped<IChatOrchestrator, ChatOrchestrator>();

builder.Services.AddScoped<IDomCleaner, DomCleanerService>();
builder.Services.AddScoped<IAIClient, AzureOpenAIClient>();
builder.Services.AddScoped<IKbSearchService, KbSearchService>();

// ── ASP.NET Core ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v2", new() { Title = "Experion Agent API", Version = "v2" });
});

var app = builder.Build();

app.UseCors();
app.UseMiddleware<IdentityMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v2/swagger.json", "Experion Agent v2"));
}

app.MapControllers();
app.Run();
