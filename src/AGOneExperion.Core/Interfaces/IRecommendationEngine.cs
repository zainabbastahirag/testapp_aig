using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Analyses a user's activity profile and current page context to produce
/// ranked recommendations. This is the core "Recommendation Agent".
/// </summary>
public interface IRecommendationEngine
{
    /// <summary>
    /// Given the user's profile and current page context, infer their intent
    /// and produce a ranked list of recommendations.
    /// </summary>
    Task<RecommendationResponse> RecommendAsync(
        RecommendationRequest request,
        UserProfile? profile,
        CancellationToken ct = default);
}
