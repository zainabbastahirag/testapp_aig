using System.Diagnostics;
using System.Text.Json;
using ExperionAgent.Core.Enums;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using ExperionAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExperionAgent.Infrastructure.Recommendations;

public class RecommendationEngine : IRecommendationEngine
{
    private readonly IActivityStore _store;
    private readonly IKbSearchService _kbSearch;
    private readonly IAIClient _ai;
    private readonly ExperionSettings _expSettings;
    private readonly RecommendationSettings _recSettings;
    private readonly ILogger<RecommendationEngine> _log;

    public RecommendationEngine(
        IActivityStore store,
        IKbSearchService kbSearch,
        IAIClient ai,
        IOptions<ExperionSettings> expSettings,
        IOptions<RecommendationSettings> recSettings,
        ILogger<RecommendationEngine> log)
    {
        _store = store;
        _kbSearch = kbSearch;
        _ai = ai;
        _expSettings = expSettings.Value;
        _recSettings = recSettings.Value;
        _log = log;
    }

    public async Task<RecommendationResponse> GenerateAsync(
        RecommendationRequest request, string resolvedUserId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var siteId = request.SiteId;

        var profile = await _store.GetProfileAsync(siteId, resolvedUserId, ct);
        var recentEvents = request.RecentEvents
            ?? await _store.GetRecentEventsAsync(siteId, resolvedUserId, 30, ct);
        var lastRecs = await _store.GetLastRecommendationsAsync(siteId, resolvedUserId, ct);

        var ruleRecs = ApplyRules(profile, recentEvents, request, lastRecs);

        var aiRecs = await RankWithAI(profile, recentEvents, ruleRecs, request, ct);

        var final = aiRecs.Take(_recSettings.MaxSuggestionsPerRequest).ToList();

        await _store.SaveRecommendationsAsync(siteId, resolvedUserId, final, ct);

        sw.Stop();
        _log.LogInformation("[Recommend] Generated {Count} suggestions for {User} in {Ms}ms",
            final.Count, resolvedUserId, sw.ElapsedMilliseconds);

        return new RecommendationResponse
        {
            Success = true,
            ResolvedUserId = resolvedUserId,
            Recommendations = final,
            ProcessingMs = sw.ElapsedMilliseconds
        };
    }

    private List<Recommendation> ApplyRules(
        UserProfile? profile, List<Core.Models.ActivityEvent> recentEvents,
        RecommendationRequest request, List<Recommendation> lastRecs)
    {
        var recs = new List<Recommendation>();

        if (profile == null || profile.TotalVisits <= 1)
        {
            recs.Add(new Recommendation
            {
                Type = RecommendationType.Welcome,
                Title = "Welcome!",
                Message = "It looks like you're new here. Let me help you get started.",
                Priority = 100,
                Reason = "first_visit"
            });
        }
        else if (profile.LastSession?.LastPage != null &&
                 profile.LastSession.LastPage != request.CurrentPageUrl)
        {
            recs.Add(new Recommendation
            {
                Type = RecommendationType.ContinueWhereLeftOff,
                Title = "Pick up where you left off",
                Message = $"Last time you were looking at: {profile.LastSession.LastPage}",
                ActionUrl = profile.LastSession.LastPage,
                ActionLabel = "Go there",
                Priority = 80,
                Reason = "returning_user"
            });
        }

        if (recentEvents.Any(e => e.Type == EventType.FormFocus))
        {
            var formEvent = recentEvents.Last(e => e.Type == EventType.FormFocus);
            recs.Add(new Recommendation
            {
                Type = RecommendationType.FormHelp,
                Title = "Need help with this form?",
                Message = "I noticed you're working on a form. I can help explain the fields.",
                Priority = 90,
                Reason = "form_interaction"
            });
        }

        if (recentEvents.Any(e => e.Type == EventType.IdleStart) &&
            request.Trigger == "idle")
        {
            recs.Add(new Recommendation
            {
                Type = RecommendationType.ContextualTip,
                Title = "Still there?",
                Message = "I can help if you're looking for something specific.",
                Priority = 60,
                Reason = "idle_detected"
            });
        }

        if (profile is { TopPages.Count: > 3 })
        {
            var topPage = profile.TopPages.OrderByDescending(p => p.VisitCount).First();
            recs.Add(new Recommendation
            {
                Type = RecommendationType.ContentSuggestion,
                Title = "Based on your interests",
                Message = $"You seem interested in {topPage.Title ?? topPage.Url}. Want to explore related content?",
                Priority = 50,
                Reason = "interest_pattern"
            });
        }

        return recs;
    }

    private async Task<List<Recommendation>> RankWithAI(
        UserProfile? profile, List<Core.Models.ActivityEvent> recentEvents,
        List<Recommendation> ruleRecs, RecommendationRequest request,
        CancellationToken ct)
    {
        if (!ruleRecs.Any())
            return ruleRecs;

        try
        {
            var kbDocs = new List<KbDocument>();
            if (request.CurrentPageTitle != null)
                kbDocs = await _kbSearch.SearchAsync(request.CurrentPageTitle, _expSettings.DefaultKbIndex, 3, ct);

            var systemPrompt = @"You are a recommendation ranking engine for a website AI assistant.
Given the user's profile, recent activity, current page context, and candidate recommendations,
rank and optionally rewrite the recommendations to be most relevant and helpful.

Return a JSON array of objects with: { ""title"", ""message"", ""priority"" (1-100), ""reason"" }
Only return the JSON array, nothing else.";

            var context = new
            {
                profile = profile != null ? new
                {
                    profile.TotalVisits,
                    profile.EngagementScore,
                    profile.CurrentIntent,
                    topPages = profile.TopPages.Take(5),
                    profile.Location
                } : null,
                recentEvents = recentEvents.TakeLast(10).Select(e => new { e.Type, e.PageUrl, e.ElementText }),
                currentPage = new { request.CurrentPageUrl, request.CurrentPageTitle },
                kbContext = kbDocs.Take(3).Select(d => new { d.Title, d.Snippet }),
                candidates = ruleRecs.Select(r => new { r.Title, r.Message, r.Priority, r.Reason })
            };

            var response = await _ai.ChatAsync(systemPrompt, JsonSerializer.Serialize(context),
                _expSettings.Model, 1000, 0.3, ct);

            var ranked = ParseJsonArray<RankedRec>(response);
            if (ranked != null && ranked.Any())
            {
                return ranked.OrderByDescending(r => r.Priority).Select(r => new Recommendation
                {
                    Type = RecommendationType.ProactiveChat,
                    Title = r.Title,
                    Message = r.Message,
                    Priority = r.Priority,
                    Reason = r.Reason
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Recommend] AI ranking failed, using rule-based order");
        }

        return ruleRecs.OrderByDescending(r => r.Priority).ToList();
    }

    private static List<T>? ParseJsonArray<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n', 2).Last();
            if (cleaned.EndsWith("```")) cleaned = cleaned[..cleaned.LastIndexOf("```")];
            return JsonSerializer.Deserialize<List<T>>(cleaned.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private class RankedRec
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? Reason { get; set; }
    }
}
