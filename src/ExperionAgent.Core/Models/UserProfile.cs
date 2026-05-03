namespace ExperionAgent.Core.Models;

public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public int TotalVisits { get; set; }
    public int TotalEvents { get; set; }
    public double AvgSessionDurationSeconds { get; set; }
    public List<PageVisit> TopPages { get; set; } = new();
    public List<string> Interests { get; set; } = new();
    public string? CurrentIntent { get; set; }
    public double EngagementScore { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public GeoLocation? Location { get; set; }
    public SessionSummary? LastSession { get; set; }
    public List<string> RecentRecommendationIds { get; set; } = new();
}

public class PageVisit
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int VisitCount { get; set; }
    public double TotalDwellSeconds { get; set; }
}

public class GeoLocation
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
}

public class SessionSummary
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int EventCount { get; set; }
    public List<string> PagesVisited { get; set; } = new();
    public string? LastPage { get; set; }
}
