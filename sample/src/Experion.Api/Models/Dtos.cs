namespace Experion.Api.Models;

// ── Inbound DTOs ─────────────────────────────────────────────────────

public class IdentifyRequest
{
    public string TenantId { get; set; } = "default";
    public string? UserId { get; set; }
    public string? AnonId { get; set; }
}

public class IdentifyResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string ResolvedUserId { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public string? RegionCluster { get; set; }
}

public class ActivityEventDto
{
    public string Type { get; set; } = string.Empty;          // pageview, click, scroll, dwell, form, search…
    public string? Url { get; set; }
    public string? Selector { get; set; }
    public string? Text { get; set; }
    public Dictionary<string, object?>? Meta { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class EventBatchRequest
{
    public string SessionId { get; set; } = string.Empty;
    public List<ActivityEventDto> Events { get; set; } = new();
}

public class ProcessRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = "default";
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public string? CapturedText { get; set; }     // optional DOM text from circle gesture
    public string Source { get; set; } = "chat";  // chat | gesture | idle
}

public class ProcessResponse
{
    public bool Success { get; set; }
    public string LogId { get; set; } = string.Empty;
    public string IntentType { get; set; } = string.Empty;   // ACTION | GENERATION
    public string? ActionKey { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public bool CacheHit { get; set; }
    public long ProcessingMs { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PipelineStep> Pipeline { get; set; } = new();
}

public class PipelineStep
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
}

public class FeedbackRequest
{
    public string LogId { get; set; } = string.Empty;
    public bool Positive { get; set; }
    public string? Comment { get; set; }
}

public class NudgePayload
{
    public string Type { get; set; } = "nudge";
    public string Message { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}
