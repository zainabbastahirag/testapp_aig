namespace AGOneExperion.Core.Models;

// ── Inbound SDK payloads ──────────────────────────────────────────────

/// <summary>
/// Single DOM element captured inside the user's circle gesture.
/// </summary>
public class CapturedElement
{
    public string Tag { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Text { get; set; }
    public string? Type { get; set; }
    public string? Href { get; set; }
    public string? Src { get; set; }
    public string? Alt { get; set; }
    public string? ClassName { get; set; }
    public string? Role { get; set; }
    public string? AriaLabel { get; set; }
    public string? Placeholder { get; set; }
    public string? Value { get; set; }
    public bool IsInteractive { get; set; }
    public ElementRect? Rect { get; set; }
}

public class ElementRect
{
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class CircleRegion
{
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Radius { get; set; }
}

/// <summary>
/// Main request from the Experion SDK (circle gesture → contextual help).
/// </summary>
public class ExperionRequest
{
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public string? SiteId { get; set; }
    public string? KbIndexName { get; set; }
    public CircleRegion? CircleRegion { get; set; }
    public List<CapturedElement> CapturedElements { get; set; } = new();
    public string? CapturedText { get; set; }
}

/// <summary>
/// Activity batch sent by the SDK on scroll, click, page-view, etc.
/// </summary>
public class ActivityBatchRequest
{
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public List<ActivityEventDto> Events { get; set; } = new();
}

public class ActivityEventDto
{
    public string Type { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }
    public string? PayloadJson { get; set; }
}

/// <summary>
/// Request for the recommendation engine without a circle gesture.
/// The engine reads the user's profile and current page to generate suggestions.
/// </summary>
public class RecommendationRequest
{
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public string? SiteId { get; set; }
    public string? KbIndexName { get; set; }
    public int MaxRecommendations { get; set; } = 3;
}

/// <summary>
/// Follow-up question after an Experion popup interaction.
/// </summary>
public class ExperionAskRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? KbIndexName { get; set; }
}

/// <summary>
/// User feedback on an Experion interaction.
/// </summary>
public class FeedbackRequest
{
    public string SessionId { get; set; } = string.Empty;
    public bool Positive { get; set; }
    public string? FeedbackText { get; set; }
}

// ── Outbound responses ───────────────────────────────────────────────

/// <summary>
/// Response sent back to the SDK for popup rendering.
/// </summary>
public class ExperionResponse
{
    public bool Success { get; set; }
    public string? ContextSummary { get; set; }
    public string? Message { get; set; }
    public string? IntentType { get; set; }
    public List<DocumentReference> RelatedDocuments { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public List<Recommendation> Recommendations { get; set; } = new();
    public long ProcessingMs { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A referenced document from the KB / Azure AI Search.
/// </summary>
public class DocumentReference
{
    public string Title { get; set; } = string.Empty;
    public string? Snippet { get; set; }
    public string? Url { get; set; }
    public double RelevanceScore { get; set; }
}

/// <summary>
/// Response for the recommendation-only endpoint.
/// </summary>
public class RecommendationResponse
{
    public bool Success { get; set; }
    public string? InferredIntent { get; set; }
    public List<Recommendation> Recommendations { get; set; } = new();
    public long ProcessingMs { get; set; }
    public string? ErrorMessage { get; set; }
}
