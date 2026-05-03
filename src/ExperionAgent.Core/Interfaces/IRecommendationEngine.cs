using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IRecommendationEngine
{
    Task<RecommendationResponse> GenerateAsync(
        RecommendationRequest request, string resolvedUserId,
        CancellationToken ct = default);
}
