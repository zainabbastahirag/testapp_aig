namespace AGOneExperion.Core.Models;

/// <summary>
/// Aggregated profile for a user built from their activity history.
/// Stored per-user in Blob Storage and updated on each session.
/// </summary>
public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public string? CountryCode { get; set; }
    public string? Region { get; set; }

    // Behavioural signals
    public List<string> ViewedPages { get; set; } = new();
    public List<string> SearchedTerms { get; set; } = new();
    public List<string> ClickedElements { get; set; } = new();
    public List<string> FocusedProducts { get; set; } = new();
    public Dictionary<string, int> CategoryInterests { get; set; } = new();

    // Temporal signals
    public int TotalSessions { get; set; }
    public int TotalPageViews { get; set; }
    public int TotalClicks { get; set; }
    public int TotalSearches { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Inferred state
    public string? InferredIntent { get; set; }
    public double? EngagementScore { get; set; }
}
