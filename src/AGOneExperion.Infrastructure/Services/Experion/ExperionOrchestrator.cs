using System.Text;
using System.Text.Json;
using AGOneExperion.Core.Configuration;
using AGOneExperion.Core.Enums;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using AGOneExperion.Infrastructure.Services.Activity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreActivityEvent = AGOneExperion.Core.Models.ActivityEvent;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AGOneExperion.Infrastructure.Services.Experion;

/// <summary>
/// Wires together every subsystem:
///
///   Activity Mining path:
///     SDK batch → UserIdentityResolver → BlobActivityRepository.AppendEvents
///               → ActivityProfileBuilder.Merge → BlobActivityRepository.SaveProfile
///
///   Circle Gesture path:
///     SDK circle → DomFragmentCleaner → LLM #1 (context detect + search query)
///               → AzureSearchService (hybrid) → LLM #2 (RAG response)
///               → LlmRecommendationEngine → ExperionResponse
///
///   Recommendation-only path:
///     RecommendationRequest → BlobActivityRepository.GetProfile
///               → LlmRecommendationEngine → RecommendationResponse
///
///   Follow-up chat path:
///     AskRequest → AzureSearchService → LLM (chat) → ExperionResponse
/// </summary>
public class ExperionOrchestrator : IExperionOrchestrator
{
    private readonly IActivityRepository _activityRepo;
    private readonly ActivityProfileBuilder _profileBuilder;
    private readonly IDomFragmentCleaner _domCleaner;
    private readonly IChatService _chat;
    private readonly IKnowledgeBaseSearch _search;
    private readonly IEmbeddingService _embedding;
    private readonly IRecommendationEngine _recommender;
    private readonly ExperionSettings _settings;
    private readonly ILogger<ExperionOrchestrator> _log;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ExperionOrchestrator(
        IActivityRepository activityRepo,
        ActivityProfileBuilder profileBuilder,
        IDomFragmentCleaner domCleaner,
        IChatService chat,
        IKnowledgeBaseSearch search,
        IEmbeddingService embedding,
        IRecommendationEngine recommender,
        IOptions<ExperionSettings> settings,
        ILogger<ExperionOrchestrator> log)
    {
        _activityRepo = activityRepo;
        _profileBuilder = profileBuilder;
        _domCleaner = domCleaner;
        _chat = chat;
        _search = search;
        _embedding = embedding;
        _recommender = recommender;
        _settings = settings.Value;
        _log = log;
    }

    // ══════════════════════════════════════════════════════════════════
    // Activity Mining
    // ══════════════════════════════════════════════════════════════════

    public async Task TrackActivityAsync(
        ActivityBatchRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (request.Events == null || request.Events.Count == 0) return;

        var userId = string.IsNullOrWhiteSpace(request.UserId)
            ? BuildAnonId(ipAddress, userAgent)
            : request.UserId;

        // Map DTOs → domain events
        var events = request.Events.Select(dto => new CoreActivityEvent
        {
            UserId = userId,
            SessionId = request.SessionId,
            Type = Enum.TryParse<ActivityType>(dto.Type, true, out var t) ? t : ActivityType.Click,
            TimestampUtc = dto.TimestampUtc,
            PageUrl = dto.PageUrl ?? string.Empty,
            PageTitle = dto.PageTitle,
            PayloadJson = dto.PayloadJson,
            SiteId = request.SiteId,
            IpHash = Hash(ipAddress)
        }).ToList();

        // Persist raw events
        await _activityRepo.AppendEventsAsync(userId, events, ct);

        // Rebuild profile incrementally
        var existing = await _activityRepo.GetProfileAsync(userId, ct);
        var updated = _profileBuilder.Merge(existing, userId, string.IsNullOrWhiteSpace(request.UserId), events);
        await _activityRepo.SaveProfileAsync(updated, ct);

        _log.LogDebug("[Orchestrator] Tracked {Count} events for user {UserId}", events.Count, userId);
    }

    // ══════════════════════════════════════════════════════════════════
    // Circle Gesture Pipeline
    // ══════════════════════════════════════════════════════════════════

    public async Task<ExperionResponse> ProcessCircleAsync(ExperionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var sessionId = request.SessionId.Length > 0 ? request.SessionId : Guid.NewGuid().ToString("N");

        try
        {
            _log.LogInformation("[Orchestrator] Circle pipeline start: session={Session}, elements={Count}",
                sessionId, request.CapturedElements.Count);

            // ── 1. Clean DOM fragment ─────────────────────────────────
            var cleaned = _domCleaner.Clean(request.CapturedElements, _settings.MaxFragmentTokens);

            // ── 2. LLM #1: Context detection + search query ───────────
            var (contextSummary, searchQuery, intentType) = await DetectContextAsync(cleaned, request, sessionId, ct);

            // ── 3. Hybrid KB search ───────────────────────────────────
            float[]? vector = null;
            try { vector = await _embedding.EmbedAsync(searchQuery, ct); } catch { /* non-fatal */ }

            var kbIndex = request.KbIndexName ?? _settings.DefaultKbIndex;
            var kbDocs = await _search.SearchAsync(kbIndex, searchQuery, vector, _settings.SearchTopK, ct);

            // ── 4. LLM #2: RAG response generation ────────────────────
            var (message, suggestions) = await GenerateResponseAsync(contextSummary, intentType, kbDocs, request, sessionId, ct);

            // ── 5. Recommendations based on profile + current page ─────
            var profile = await _activityRepo.GetProfileAsync(request.UserId, ct);
            var recResponse = await _recommender.RecommendAsync(new RecommendationRequest
            {
                UserId = request.UserId,
                SessionId = sessionId,
                PageUrl = request.PageUrl,
                PageTitle = request.PageTitle,
                KbIndexName = kbIndex,
                MaxRecommendations = _settings.MaxRecommendations
            }, profile, ct);

            sw.Stop();

            return new ExperionResponse
            {
                Success = true,
                ContextSummary = contextSummary,
                IntentType = intentType,
                Message = message,
                RelatedDocuments = kbDocs,
                Suggestions = suggestions,
                Recommendations = recResponse.Recommendations,
                ProcessingMs = sw.ElapsedMilliseconds,
                SessionId = sessionId
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "[Orchestrator] Circle pipeline failed: session={Session}", sessionId);
            return new ExperionResponse { Success = false, ErrorMessage = "Processing failed.", ProcessingMs = sw.ElapsedMilliseconds };
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Recommendation-only
    // ══════════════════════════════════════════════════════════════════

    public async Task<RecommendationResponse> GetRecommendationsAsync(
        RecommendationRequest request, CancellationToken ct = default)
    {
        var profile = await _activityRepo.GetProfileAsync(request.UserId, ct);
        return await _recommender.RecommendAsync(request, profile, ct);
    }

    // ══════════════════════════════════════════════════════════════════
    // Follow-up chat
    // ══════════════════════════════════════════════════════════════════

    public async Task<ExperionResponse> AskFollowUpAsync(
        string sessionId, ExperionAskRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            float[]? vector = null;
            try { vector = await _embedding.EmbedAsync(request.Question, ct); } catch { /* non-fatal */ }

            var kbIndex = request.KbIndexName ?? _settings.DefaultKbIndex;
            var kbDocs = await _search.SearchAsync(kbIndex, request.Question, vector, _settings.SearchTopK, ct);
            var kbContent = string.Join("\n", kbDocs.Select(d => $"- [{d.Title}]: {d.Snippet}"));

            var system = $@"You are Experion, an embedded AI assistant. Answer the user's question concisely and helpfully.
Use the knowledge base content below when relevant.
Respond in {_settings.ResponseLanguage}.

## Knowledge Base
{(string.IsNullOrWhiteSpace(kbContent) ? "(nothing found)" : kbContent)}";

            var result = await _chat.ChatAsync(
                system, request.Question,
                _settings.ChatModel, _settings.MaxOutputTokens, 0.7, sessionId, ct);

            sw.Stop();

            return new ExperionResponse
            {
                Success = result.Success,
                Message = result.Response ?? result.ErrorMessage ?? "No response.",
                RelatedDocuments = kbDocs,
                ProcessingMs = sw.ElapsedMilliseconds,
                SessionId = sessionId,
                ErrorMessage = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Orchestrator] Follow-up failed: session={Session}", sessionId);
            return new ExperionResponse { Success = false, ErrorMessage = ex.Message, ProcessingMs = sw.ElapsedMilliseconds };
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Feedback
    // ══════════════════════════════════════════════════════════════════

    public async Task LogFeedbackAsync(string sessionId, bool positive, string? text, CancellationToken ct = default)
    {
        // Stored as a synthetic event in the user activity log so it stays in Blob Storage
        // and can be queried for reinforcement learning / analytics later.
        var userId = sessionId; // session doubles as a lookup key here
        var feedbackEvent = new CoreActivityEvent
        {
            UserId = userId,
            SessionId = sessionId,
            Type = ActivityType.Click,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                feedbackPositive = positive,
                feedbackText = text,
                eventKind = "feedback"
            })
        };

        await _activityRepo.AppendEventsAsync(userId, new[] { feedbackEvent }, ct);
        _log.LogInformation("[Orchestrator] Feedback for session {Session}: positive={Positive}", sessionId, positive);
    }

    // ══════════════════════════════════════════════════════════════════
    // Private helpers
    // ══════════════════════════════════════════════════════════════════

    private async Task<(string ContextSummary, string SearchQuery, string IntentType)> DetectContextAsync(
        CleanedFragment cleaned, ExperionRequest request, string sessionId, CancellationToken ct)
    {
        var system = @"You are a context-detection AI. Given a cleaned DOM fragment and page URL,
identify what the user is looking at and generate an optimal knowledge-base search query.

Respond ONLY with JSON (no markdown fences):
{
  ""contextSummary"": ""<2-3 sentence summary of the circled content>"",
  ""intentType"": ""<Exploration|ProductSearch|Learning|Decision|ContextualHelp|DirectQuestion|Unknown>"",
  ""searchQuery"": ""<keyword-rich query for knowledge base retrieval>""
}";

        var user = $"URL: {request.PageUrl}\nTitle: {request.PageTitle ?? "unknown"}\n\n{cleaned.CleanedText}";

        var result = await _chat.ChatAsync(system, user, _settings.ChatModel, 512, 0.3, sessionId, ct);

        if (!result.Success || string.IsNullOrEmpty(result.Response))
            return ("Circled content on the page.", cleaned.CleanedText[..Math.Min(cleaned.CleanedText.Length, 200)], "ContextualHelp");

        try
        {
            using var doc = JsonDocument.Parse(StripFences(result.Response));
            var summary = doc.RootElement.GetProperty("contextSummary").GetString() ?? "Circled content.";
            var intent = doc.RootElement.GetProperty("intentType").GetString() ?? "ContextualHelp";
            var query = doc.RootElement.GetProperty("searchQuery").GetString() ?? summary;
            return (summary, query, intent);
        }
        catch { return ("Circled content.", cleaned.CleanedText[..Math.Min(cleaned.CleanedText.Length, 200)], "ContextualHelp"); }
    }

    private async Task<(string Message, List<string> Suggestions)> GenerateResponseAsync(
        string contextSummary, string intentType, List<DocumentReference> kbDocs,
        ExperionRequest request, string sessionId, CancellationToken ct)
    {
        var kbContent = string.Join("\n", kbDocs.Select(d => $"- [{d.Title}]: {d.Snippet}"));

        var system = $@"You are Experion, an AI assistant embedded on an enterprise website.
The user has circled an area on the page. Provide a concise, helpful response.
Respond in {_settings.ResponseLanguage}.

Respond ONLY with JSON (no markdown fences):
{{
  ""message"": ""<contextual answer in 2-4 sentences, use markdown for structure if helpful>"",
  ""suggestions"": [""<follow-up question 1>"", ""<follow-up question 2>"", ""<follow-up question 3>""]
}}";

        var userMsg = new StringBuilder();
        userMsg.AppendLine($"## What the user circled");
        userMsg.AppendLine(contextSummary);
        userMsg.AppendLine($"Intent: {intentType}");
        userMsg.AppendLine();
        userMsg.AppendLine("## Relevant Knowledge Base");
        userMsg.AppendLine(string.IsNullOrWhiteSpace(kbContent) ? "(nothing retrieved)" : kbContent);

        var result = await _chat.ChatAsync(system, userMsg.ToString(), _settings.ChatModel, _settings.MaxOutputTokens, 0.7, sessionId, ct);

        if (!result.Success || string.IsNullOrEmpty(result.Response))
            return ("I found some content here but couldn't generate a detailed response.", new List<string>());

        try
        {
            using var doc = JsonDocument.Parse(StripFences(result.Response));
            var msg = doc.RootElement.GetProperty("message").GetString() ?? result.Response;
            var sug = doc.RootElement.TryGetProperty("suggestions", out var sugEl)
                ? sugEl.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                : new List<string>();
            return (msg, sug);
        }
        catch { return (result.Response ?? "No response.", new List<string>()); }
    }

    private static string BuildAnonId(string? ip, string? ua)
    {
        var raw = $"{ip ?? "unknown"}|{ua ?? "unknown"}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "anon-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string? Hash(string? value)
    {
        if (value == null) return null;
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```")) t = t[(t.IndexOf('\n') + 1)..];
        if (t.EndsWith("```")) t = t[..t.LastIndexOf("```")];
        return t.Trim();
    }
}
