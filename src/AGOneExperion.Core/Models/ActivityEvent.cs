using AGOneExperion.Core.Enums;

namespace AGOneExperion.Core.Models;

/// <summary>
/// A single atomic user activity event captured by the SDK.
/// Stored in Blob Storage as newline-delimited JSON per user.
/// </summary>
public class ActivityEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ActivityType Type { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // Page context
    public string PageUrl { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public string? SiteId { get; set; }

    // Event-specific payload (serialised as JSON)
    public string? PayloadJson { get; set; }

    // Geo / device (derived server-side)
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public string? DeviceType { get; set; }
    public string? IpHash { get; set; }
}
