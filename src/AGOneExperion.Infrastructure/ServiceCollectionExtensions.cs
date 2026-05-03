using AGOneExperion.Core.Configuration;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Infrastructure.Services;
using AGOneExperion.Infrastructure.Services.Activity;
using AGOneExperion.Infrastructure.Services.Chat;
using AGOneExperion.Infrastructure.Services.Experion;
using AGOneExperion.Infrastructure.Services.Search;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AGOneExperion.Infrastructure;

/// <summary>
/// Extension method to register all Experion infrastructure services into the DI container.
/// Call from Program.cs: builder.Services.AddExperionInfrastructure(builder.Configuration).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExperionInfrastructure(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // ── Config ────────────────────────────────────────────────────
        services.Configure<ExperionSettings>(opts =>
            configuration.GetSection("Experion").Bind(opts));

        // ── Blob Storage ──────────────────────────────────────────────
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<ExperionSettings>>().Value;
            return new BlobServiceClient(settings.BlobConnectionString);
        });

        services.AddScoped<IActivityRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<ExperionSettings>>().Value;
            var blobService = sp.GetRequiredService<BlobServiceClient>();
            var log = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BlobActivityRepository>>();
            return new BlobActivityRepository(
                blobService,
                settings.ActivityContainerName,
                settings.ProfileContainerName,
                log);
        });

        // ── Activity Mining ───────────────────────────────────────────
        services.AddScoped<ActivityProfileBuilder>();
        services.AddScoped<IUserIdentityResolver, UserIdentityResolver>();

        // ── AI / Search ───────────────────────────────────────────────
        services.AddScoped<AzureOpenAIService>();
        services.AddScoped<IChatService>(sp => sp.GetRequiredService<AzureOpenAIService>());
        services.AddScoped<IEmbeddingService>(sp => sp.GetRequiredService<AzureOpenAIService>());

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<ExperionSettings>>().Value;
            return new SearchIndexClientFactory(settings.SearchEndpoint, settings.SearchApiKey);
        });
        services.AddScoped<IKnowledgeBaseSearch, AzureSearchService>();

        // ── Domain Services ───────────────────────────────────────────
        services.AddScoped<IDomFragmentCleaner, DomFragmentCleaner>();
        services.AddScoped<IRecommendationEngine, LlmRecommendationEngine>();
        services.AddScoped<IExperionOrchestrator, ExperionOrchestrator>();

        return services;
    }
}
