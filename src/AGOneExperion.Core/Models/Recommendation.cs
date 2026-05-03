using AGOneExperion.Core.Enums;

namespace AGOneExperion.Core.Models;

/// <summary>
/// A recommendation surfaced to the user by the Recommendation Agent.
/// </summary>
public class Recommendation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RecommendationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ActionUrl { get; set; }
    public string? ActionLabel { get; set; }
    public string? ImageUrl { get; set; }
    public double ConfidenceScore { get; set; }
    public string? Reasoning { get; set; }
}
