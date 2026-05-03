using ExperionAgent.Core.Enums;

namespace ExperionAgent.Core.Models;

public class ActivityEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public EventType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }
    public string? ElementSelector { get; set; }
    public string? ElementText { get; set; }
    public string? ElementTag { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
