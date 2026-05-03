using AGOneExperion.Core.Enums;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.Extensions.Logging;

namespace AGOneExperion.Infrastructure.Services.Activity;

/// <summary>
/// Builds and updates a <see cref="UserProfile"/> from raw activity events.
/// Called by the orchestrator after each batch of events is persisted.
/// </summary>
public class ActivityProfileBuilder
{
    private readonly ILogger<ActivityProfileBuilder> _log;

    // How many unique URLs / terms to keep in the profile before trimming
    private const int MaxTrackedItems = 30;

    public ActivityProfileBuilder(ILogger<ActivityProfileBuilder> log)
    {
        _log = log;
    }

    /// <summary>
    /// Merge a list of new events into an existing profile (or create one from scratch).
    /// Returns the updated profile.
    /// </summary>
    public UserProfile Merge(UserProfile? existing, string userId, bool isAnonymous,
        IEnumerable<ActivityEvent> newEvents)
    {
        var profile = existing ?? new UserProfile
        {
            UserId = userId,
            IsAnonymous = isAnonymous,
            FirstSeenUtc = DateTime.UtcNow
        };

        foreach (var ev in newEvents)
        {
            profile.TotalPageViews += ev.Type == ActivityType.PageView ? 1 : 0;
            profile.TotalClicks += ev.Type == ActivityType.Click ? 1 : 0;
            profile.TotalSearches += ev.Type == ActivityType.Search ? 1 : 0;

            if (ev.Type == ActivityType.SessionStart) profile.TotalSessions++;

            if (ev.Type == ActivityType.PageView && !string.IsNullOrWhiteSpace(ev.PageUrl))
                TrimAdd(profile.ViewedPages, ev.PageUrl);

            if (ev.Type == ActivityType.Search && ev.PayloadJson != null)
            {
                var term = ExtractPayloadString(ev.PayloadJson, "query");
                if (!string.IsNullOrWhiteSpace(term)) TrimAdd(profile.SearchedTerms, term);
            }

            if (ev.Type == ActivityType.ProductView && ev.PayloadJson != null)
            {
                var product = ExtractPayloadString(ev.PayloadJson, "productId")
                              ?? ExtractPayloadString(ev.PayloadJson, "productName");
                if (!string.IsNullOrWhiteSpace(product)) TrimAdd(profile.FocusedProducts, product);
            }

            if (!string.IsNullOrWhiteSpace(ev.PageUrl))
            {
                var category = InferCategory(ev.PageUrl);
                if (!string.IsNullOrWhiteSpace(category))
                    profile.CategoryInterests[category] = (profile.CategoryInterests.TryGetValue(category, out var c) ? c : 0) + 1;
            }

            if (ev.TimestampUtc > profile.LastSeenUtc) profile.LastSeenUtc = ev.TimestampUtc;
            if (!string.IsNullOrWhiteSpace(ev.CountryCode)) profile.CountryCode = ev.CountryCode;
            if (!string.IsNullOrWhiteSpace(ev.Region)) profile.Region = ev.Region;
        }

        profile.EngagementScore = ComputeEngagement(profile);
        profile.InferredIntent = InferIntent(profile);
        profile.UpdatedAtUtc = DateTime.UtcNow;

        _log.LogDebug("[ProfileBuilder] Updated profile for {UserId}: score={Score}, intent={Intent}",
            userId, profile.EngagementScore, profile.InferredIntent);

        return profile;
    }

    private static void TrimAdd(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
            if (list.Count > MaxTrackedItems) list.RemoveAt(0);
        }
    }

    private static string? ExtractPayloadString(string json, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }

    private static string? InferCategory(string url)
    {
        // Simple heuristic: extract first meaningful path segment
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0].ToLowerInvariant() : null;
        }
        catch { return null; }
    }

    private static double ComputeEngagement(UserProfile p)
    {
        // Weighted score 0-100
        var score = Math.Min(p.TotalPageViews * 2.0, 30)
                    + Math.Min(p.TotalClicks * 3.0, 30)
                    + Math.Min(p.TotalSearches * 5.0, 20)
                    + Math.Min(p.TotalSessions * 5.0, 20);
        return Math.Round(score, 1);
    }

    private static string InferIntent(UserProfile p)
    {
        if (p.TotalSearches > 3) return "ProductSearch";
        if (p.FocusedProducts.Count > 2) return "Decision";
        if (p.TotalPageViews > 10) return "Exploration";
        if (p.TotalPageViews > 3) return "Learning";
        return "Unknown";
    }
}
