using System.Text;
using System.Text.Json;
using AGOneExperion.Core.Configuration;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGOneExperion.Infrastructure.Services.Experion;

/// <summary>
/// LLM-powered recommendation engine.
/// Analyses the user's aggregated profile and current page to generate
/// ranked, explainable recommendations.
/// </summary>
public class LlmRecommendationEngine : IRecommendationEngine
{
    private readonly IChatService _chat;
    private readonly IKnowledgeBaseSearch _search;
    private readonly IEmbeddingService _embedding;
    private readonly ExperionSettings _settings;
    private readonly ILogger<LlmRecommendationEngine> _log;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public LlmRecommendationEngine(
        IChatService chat,
        IKnowledgeBaseSearch search,
        IEmbeddingService embedding,
        IOptions<ExperionSettings> settings,
        ILogger<LlmRecommendationEngine> log)
    {
        _chat = chat;
        _search = search;
        _embedding = embedding;
        _settings = settings.Value;
        _log = log;
    }

    public async Task<RecommendationResponse> RecommendAsync(
        RecommendationRequest request,
        UserProfile? profile,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Step 1: Build profile summary for the LLM
            var profileSummary = BuildProfileSummary(profile);

            // Step 2: Embed the current page context for vector KB search
            var pageContext = $"{request.PageTitle ?? ""} {request.PageUrl}".Trim();
            float[]? vector = null;
            try { vector = await _embedding.EmbedAsync(pageContext, ct); } catch { /* non-fatal */ }

            // Step 3: Retrieve relevant KB documents
            var kbIndex = request.KbIndexName ?? _settings.DefaultKbIndex;
            var kbDocs = await _search.SearchAsync(kbIndex, pageContext, vector, _settings.SearchTopK, ct);
            var kbContent = string.Join("\n", kbDocs.Select(d => $"- [{d.Title}]: {d.Snippet}"));

            // Step 4: Ask LLM to generate recommendations
            var systemPrompt = @"You are an intelligent recommendation agent embedded on an enterprise website.
Your job is to analyse a visitor's behaviour and the current page they are viewing,
then produce personalised, actionable recommendations.

Always respond with valid JSON in this exact schema (no markdown fences):
{
  ""inferredIntent"": ""<one of: Exploration|ProductSearch|Learning|Decision|Unknown>"",
  ""recommendations"": [
    {
      ""type"": ""<Product|Content|Navigation|Comparison|ProactiveNudge>"",
      ""title"": ""<short title>"",
      ""description"": ""<1-2 sentence description>"",
      ""actionUrl"": ""<relative or absolute URL, or null>"",
      ""actionLabel"": ""<CTA text>"",
      ""confidenceScore"": <0.0-1.0>,
      ""reasoning"": ""<brief internal note>""
    }
  ]
}";

            var userPrompt = new StringBuilder();
            userPrompt.AppendLine($"## Current Page");
            userPrompt.AppendLine($"URL: {request.PageUrl}");
            userPrompt.AppendLine($"Title: {request.PageTitle ?? "unknown"}");
            userPrompt.AppendLine();
            userPrompt.AppendLine("## User Behaviour Profile");
            userPrompt.AppendLine(profileSummary);
            userPrompt.AppendLine();
            userPrompt.AppendLine("## Relevant Knowledge Base Content");
            userPrompt.AppendLine(string.IsNullOrWhiteSpace(kbContent) ? "(none found)" : kbContent);
            userPrompt.AppendLine();
            userPrompt.AppendLine($"Generate {request.MaxRecommendations} personalised recommendations.");

            var result = await _chat.ChatAsync(
                systemPrompt,
                userPrompt.ToString(),
                _settings.ChatModel,
                _settings.MaxOutputTokens,
                0.5,
                request.SessionId,
                ct);

            sw.Stop();

            if (!result.Success)
                return new RecommendationResponse { Success = false, ErrorMessage = result.ErrorMessage, ProcessingMs = sw.ElapsedMilliseconds };

            var parsed = ParseRecommendationJson(result.Response);
            return new RecommendationResponse
            {
                Success = true,
                InferredIntent = parsed.InferredIntent,
                Recommendations = parsed.Recommendations.Take(request.MaxRecommendations).ToList(),
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Recommendation] Failed for user {UserId}", request.UserId);
            return new RecommendationResponse { Success = false, ErrorMessage = ex.Message, ProcessingMs = sw.ElapsedMilliseconds };
        }
    }

    private static string BuildProfileSummary(UserProfile? profile)
    {
        if (profile == null) return "No prior activity recorded for this visitor.";

        var sb = new StringBuilder();
        sb.AppendLine($"- Sessions: {profile.TotalSessions}, Page views: {profile.TotalPageViews}, Clicks: {profile.TotalClicks}, Searches: {profile.TotalSearches}");
        sb.AppendLine($"- Engagement score: {profile.EngagementScore:F1}/100");
        sb.AppendLine($"- Inferred intent: {profile.InferredIntent ?? "Unknown"}");
        if (profile.SearchedTerms.Any())
            sb.AppendLine($"- Searched for: {string.Join(", ", profile.SearchedTerms.TakeLast(5))}");
        if (profile.FocusedProducts.Any())
            sb.AppendLine($"- Viewed products: {string.Join(", ", profile.FocusedProducts.TakeLast(5))}");
        if (profile.CategoryInterests.Any())
        {
            var top = profile.CategoryInterests.OrderByDescending(x => x.Value).Take(3);
            sb.AppendLine($"- Top categories: {string.Join(", ", top.Select(x => $"{x.Key}({x.Value})"))}");
        }
        return sb.ToString();
    }

    private static RecommendationLlmOutput ParseRecommendationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var cleaned = StripMarkdownFences(json);
            return JsonSerializer.Deserialize<RecommendationLlmOutput>(cleaned, _json) ?? new();
        }
        catch { return new(); }
    }

    private static string StripMarkdownFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```")) t = t[(t.IndexOf('\n') + 1)..];
        if (t.EndsWith("```")) t = t[..t.LastIndexOf("```")];
        return t.Trim();
    }
}

// ── LLM output DTO ───────────────────────────────────────────────────

internal class RecommendationLlmOutput
{
    public string InferredIntent { get; set; } = "Unknown";
    public List<Recommendation> Recommendations { get; set; } = new();
}
