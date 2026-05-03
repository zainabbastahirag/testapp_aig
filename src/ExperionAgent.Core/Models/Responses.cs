namespace ExperionAgent.Core.Models;

public class ActivityIngestResponse
{
    public bool Success { get; set; }
    public string ResolvedUserId { get; set; } = string.Empty;
    public int EventsProcessed { get; set; }
}

public class RecommendationResponse
{
    public bool Success { get; set; }
    public string? ResolvedUserId { get; set; }
    public List<Recommendation> Recommendations { get; set; } = new();
    public long ProcessingMs { get; set; }
}

public class ChatResponse
{
    public bool Success { get; set; }
    public string? ConversationId { get; set; }
    public string? ContextSummary { get; set; }
    public string? IntentType { get; set; }
    public string? Message { get; set; }
    public List<KbDocument> RelatedDocuments { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public long ProcessingMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class KbDocument
{
    public string Title { get; set; } = string.Empty;
    public string? Snippet { get; set; }
    public string? Url { get; set; }
    public double RelevanceScore { get; set; }
}
