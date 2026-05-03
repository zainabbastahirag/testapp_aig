using ExperionAgent.Core.Enums;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExperionAgent.Infrastructure.Mining;

public class ActivityMiner : IActivityMiner
{
    private readonly IActivityStore _store;
    private readonly ILogger<ActivityMiner> _log;

    public ActivityMiner(IActivityStore store, ILogger<ActivityMiner> log)
    {
        _store = store;
        _log = log;
    }

    public async Task<ActivityIngestResponse> IngestAsync(
        ActivityIngestRequest request, string resolvedUserId, GeoLocation? location,
        CancellationToken ct = default)
    {
        var siteId = request.SiteId;

        await _store.AppendEventsAsync(siteId, resolvedUserId, request.Events, ct);

        var profile = await _store.GetProfileAsync(siteId, resolvedUserId, ct)
                      ?? CreateNewProfile(resolvedUserId, siteId, location);

        UpdateProfile(profile, request.Events, request.SessionId);
        await _store.SaveProfileAsync(siteId, resolvedUserId, profile, ct);

        _log.LogInformation("[Miner] Ingested {Count} events for {User}@{Site}",
            request.Events.Count, resolvedUserId, siteId);

        return new ActivityIngestResponse
        {
            Success = true,
            ResolvedUserId = resolvedUserId,
            EventsProcessed = request.Events.Count
        };
    }

    private static UserProfile CreateNewProfile(string userId, string siteId, GeoLocation? location)
    {
        return new UserProfile
        {
            UserId = userId,
            SiteId = siteId,
            FirstSeen = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow,
            Location = location
        };
    }

    private static void UpdateProfile(UserProfile profile, List<ActivityEvent> events, string sessionId)
    {
        profile.LastSeen = DateTime.UtcNow;
        profile.TotalEvents += events.Count;

        if (profile.LastSession?.SessionId != sessionId)
        {
            profile.TotalVisits++;
            profile.LastSession = new SessionSummary
            {
                SessionId = sessionId,
                StartedAt = DateTime.UtcNow
            };
        }

        var session = profile.LastSession!;
        session.EventCount += events.Count;

        foreach (var e in events)
        {
            if (e.PageUrl != null && !session.PagesVisited.Contains(e.PageUrl))
                session.PagesVisited.Add(e.PageUrl);

            session.LastPage = e.PageUrl ?? session.LastPage;

            if (e.Type == EventType.PageView && e.PageUrl != null)
            {
                var existing = profile.TopPages.FirstOrDefault(p => p.Url == e.PageUrl);
                if (existing != null)
                    existing.VisitCount++;
                else
                    profile.TopPages.Add(new PageVisit { Url = e.PageUrl, Title = e.PageTitle, VisitCount = 1 });
            }
        }

        profile.EngagementScore = CalculateEngagement(profile);
        profile.CurrentIntent = InferIntent(events);
    }

    private static double CalculateEngagement(UserProfile profile)
    {
        var visitScore = Math.Min(profile.TotalVisits * 10.0, 30);
        var eventScore = Math.Min(profile.TotalEvents * 0.5, 40);
        var diversityScore = Math.Min(profile.TopPages.Count * 5.0, 30);
        return Math.Round(visitScore + eventScore + diversityScore, 1);
    }

    private static string InferIntent(List<ActivityEvent> events)
    {
        var types = events.Select(e => e.Type).ToList();
        if (types.Contains(EventType.FormFocus) || types.Contains(EventType.FormSubmit))
            return "form-interaction";
        if (types.Count(t => t == EventType.Click) > 5)
            return "active-exploration";
        if (types.Contains(EventType.ScrollDepth))
            return "content-reading";
        if (types.Contains(EventType.Navigation))
            return "browsing";
        return "passive";
    }
}
