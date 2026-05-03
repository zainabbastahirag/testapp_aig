using ExperionAgent.Core.Enums;

namespace ExperionAgent.Core.Models;

public class Recommendation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RecommendationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionLabel { get; set; }
    public string? ActionUrl { get; set; }
    public int Priority { get; set; }
    public bool Dismissable { get; set; } = true;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}
