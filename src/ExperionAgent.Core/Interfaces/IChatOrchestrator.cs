using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IChatOrchestrator
{
    Task<ChatResponse> ProcessCircleAsync(CircleProcessRequest request, string resolvedUserId, CancellationToken ct = default);
    Task<ChatResponse> AskAsync(ChatAskRequest request, string resolvedUserId, CancellationToken ct = default);
    Task LogFeedbackAsync(string conversationId, bool positive, string? feedbackText, CancellationToken ct = default);
}
