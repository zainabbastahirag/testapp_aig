using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IActivityStore
{
    Task AppendEventsAsync(string siteId, string userId, List<ActivityEvent> events, CancellationToken ct = default);
    Task<UserProfile?> GetProfileAsync(string siteId, string userId, CancellationToken ct = default);
    Task SaveProfileAsync(string siteId, string userId, UserProfile profile, CancellationToken ct = default);
    Task<List<ActivityEvent>> GetRecentEventsAsync(string siteId, string userId, int count = 50, CancellationToken ct = default);
    Task SaveRecommendationsAsync(string siteId, string userId, List<Recommendation> recs, CancellationToken ct = default);
    Task<List<Recommendation>> GetLastRecommendationsAsync(string siteId, string userId, CancellationToken ct = default);
}
