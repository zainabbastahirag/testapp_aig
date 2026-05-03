namespace ExperionAgent.Core.Models;

public class ActivityIngestRequest
{
    public string? UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? ClientFingerprint { get; set; }
    public List<ActivityEvent> Events { get; set; } = new();
}

public class RecommendationRequest
{
    public string? UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? ClientFingerprint { get; set; }
    public string? CurrentPageUrl { get; set; }
    public string? CurrentPageTitle { get; set; }
    public List<ActivityEvent>? RecentEvents { get; set; }
    public string? Trigger { get; set; }
}

public class CircleProcessRequest
{
    public string? UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? ClientFingerprint { get; set; }
    public string PageUrl { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public CircleRegion? CircleRegion { get; set; }
    public List<CapturedElement> CapturedElements { get; set; } = new();
    public string? CapturedText { get; set; }
}

public class CircleRegion
{
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ChatAskRequest
{
    public string? UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? ClientFingerprint { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? KbIndexName { get; set; }
}

public class FeedbackRequest
{
    public string ConversationId { get; set; } = string.Empty;
    public bool Positive { get; set; }
    public string? FeedbackText { get; set; }
}
