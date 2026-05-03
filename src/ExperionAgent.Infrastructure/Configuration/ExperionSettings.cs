namespace ExperionAgent.Infrastructure.Configuration;

public class ExperionSettings
{
    public string DefaultKbIndex { get; set; } = "ag-experion-index";
    public int MaxCapturedElements { get; set; } = 50;
    public int MaxFragmentTokens { get; set; } = 4000;
    public string Model { get; set; } = "gpt-4.1";
    public int MaxOutputTokens { get; set; } = 2000;
    public int SearchTopK { get; set; } = 5;
    public string ResponseLanguage { get; set; } = "en";
}

public class StorageSettings
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string ActivityContainerName { get; set; } = "experion-activity";
    public string ConversationContainerName { get; set; } = "experion-conversations";
}

public class OpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = "gpt-4.1";
    public string EmbeddingEndpoint { get; set; } = string.Empty;
    public string EmbeddingApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public class SearchSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class RecommendationSettings
{
    public int IdleThresholdSeconds { get; set; } = 30;
    public int EventBatchTrigger { get; set; } = 20;
    public int MaxSuggestionsPerRequest { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 5;
}
