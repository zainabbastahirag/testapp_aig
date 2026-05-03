namespace AGOneExperion.Core.Configuration;

/// <summary>
/// Bound to the "Experion" configuration section.
/// </summary>
public class ExperionSettings
{
    // Azure Blob Storage — activity + profile persistence
    public string BlobConnectionString { get; set; } = string.Empty;
    public string ActivityContainerName { get; set; } = "experion-activity";
    public string ProfileContainerName { get; set; } = "experion-profiles";

    // Azure OpenAI
    public string OpenAiEndpoint { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gpt-4.1";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string EmbeddingEndpoint { get; set; } = string.Empty;
    public string EmbeddingApiKey { get; set; } = string.Empty;

    // Azure AI Search
    public string SearchEndpoint { get; set; } = string.Empty;
    public string SearchApiKey { get; set; } = string.Empty;
    public string DefaultKbIndex { get; set; } = "experion-kb";
    public int SearchTopK { get; set; } = 5;

    // Behaviour tuning
    public int MaxActivityEventsPerUser { get; set; } = 500;
    public int MaxFragmentTokens { get; set; } = 4000;
    public int MaxOutputTokens { get; set; } = 2000;
    public int MaxRecommendations { get; set; } = 3;
    public string ResponseLanguage { get; set; } = "en";

    // Proactive nudge: trigger after N events with no explicit gesture
    public int ProactiveNudgeAfterEvents { get; set; } = 8;
}
