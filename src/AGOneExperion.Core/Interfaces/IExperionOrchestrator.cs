using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Main pipeline orchestrator: activity mining + contextual AI response + recommendations.
/// </summary>
public interface IExperionOrchestrator
{
    /// <summary>
    /// Ingest a batch of raw activity events from the SDK.
    /// Updates the user's profile in Blob Storage.
    /// </summary>
    Task TrackActivityAsync(ActivityBatchRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>
    /// Full circle-gesture pipeline:
    /// DOM cleaning → context detection → KB search → RAG response → recommendations.
    /// </summary>
    Task<ExperionResponse> ProcessCircleAsync(ExperionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Recommendation-only call (no circle gesture).
    /// Reads the user's activity profile and returns ranked suggestions for the current page.
    /// </summary>
    Task<RecommendationResponse> GetRecommendationsAsync(RecommendationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Handle a follow-up question in the Experion sidebar chat.
    /// </summary>
    Task<ExperionResponse> AskFollowUpAsync(string sessionId, ExperionAskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Record user feedback (thumbs up/down) for an interaction.
    /// </summary>
    Task LogFeedbackAsync(string sessionId, bool positive, string? text, CancellationToken ct = default);
}
