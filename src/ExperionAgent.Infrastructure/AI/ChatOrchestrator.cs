using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using ExperionAgent.Infrastructure.Configuration;
using ExperionAgent.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExperionAgent.Infrastructure.AI;

public class ChatOrchestrator : IChatOrchestrator
{
    private readonly IDomCleaner _cleaner;
    private readonly IAIClient _ai;
    private readonly IKbSearchService _kbSearch;
    private readonly IActivityStore _activityStore;
    private readonly ConversationStore _conversationStore;
    private readonly ExperionSettings _settings;
    private readonly ILogger<ChatOrchestrator> _log;

    public ChatOrchestrator(
        IDomCleaner cleaner,
        IAIClient ai,
        IKbSearchService kbSearch,
        IActivityStore activityStore,
        ConversationStore conversationStore,
        IOptions<ExperionSettings> settings,
        ILogger<ChatOrchestrator> log)
    {
        _cleaner = cleaner;
        _ai = ai;
        _kbSearch = kbSearch;
        _activityStore = activityStore;
        _conversationStore = conversationStore;
        _settings = settings.Value;
        _log = log;
    }

    public async Task<ChatResponse> ProcessCircleAsync(
        CircleProcessRequest request, string resolvedUserId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conversationId = Guid.NewGuid().ToString("N");

        try
        {
            var cleaned = _cleaner.Clean(request.CapturedElements, _settings.MaxFragmentTokens);

            var contextResult = await DetectContextAsync(cleaned, request, ct);

            var kbDocs = await _kbSearch.SearchAsync(
                contextResult.SearchQuery, _settings.DefaultKbIndex, _settings.SearchTopK, ct);

            var responseResult = await GenerateResponseAsync(contextResult, kbDocs, ct);

            var conversation = new ConversationLog
            {
                ConversationId = conversationId,
                UserId = resolvedUserId,
                SiteId = request.SiteId,
                PageUrl = request.PageUrl,
                ContextSummary = contextResult.ContextSummary,
                Turns =
                {
                    new ConversationTurn
                    {
                        Role = "system",
                        Content = $"Context: {contextResult.ContextSummary}",
                        Timestamp = DateTime.UtcNow
                    },
                    new ConversationTurn
                    {
                        Role = "assistant",
                        Content = responseResult.Message,
                        Timestamp = DateTime.UtcNow
                    }
                }
            };
            await _conversationStore.SaveAsync(conversationId, conversation, ct);

            sw.Stop();
            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                ContextSummary = contextResult.ContextSummary,
                IntentType = contextResult.ContentType,
                Message = responseResult.Message,
                RelatedDocuments = kbDocs,
                Suggestions = responseResult.Suggestions,
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Chat] Circle processing failed");
            return new ChatResponse
            {
                Success = false,
                ErrorMessage = "An error occurred while processing your request.",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<ChatResponse> AskAsync(
        ChatAskRequest request, string resolvedUserId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var conversation = await _conversationStore.LoadAsync(request.ConversationId, ct);
            var historyContext = conversation != null
                ? BuildHistoryContext(conversation)
                : "No prior conversation context.";

            var kbDocs = await _kbSearch.SearchAsync(
                request.Question,
                request.KbIndexName ?? _settings.DefaultKbIndex,
                _settings.SearchTopK, ct);

            var kbContext = string.Join("\n", kbDocs.Select(d => $"- {d.Title}: {d.Snippet}"));

            var systemPrompt = @"You are Experion, an AI assistant embedded in a website. You help users
understand the content they're viewing, answer questions, and guide them.

You have access to:
1. Previous conversation context
2. Relevant knowledge base documents
3. The user's current question

Be helpful, concise, and contextual. If the KB docs are relevant, cite them.
Return JSON: { ""message"": ""your response"", ""suggestions"": [""follow-up 1"", ""follow-up 2""] }";

            var userPrompt = $@"## Conversation History
{historyContext}

## Knowledge Base Results
{(string.IsNullOrEmpty(kbContext) ? "No relevant documents found." : kbContext)}

## User Question
{request.Question}";

            var response = await _ai.ChatAsync(systemPrompt, userPrompt, _settings.Model,
                _settings.MaxOutputTokens, 0.4, ct);

            var parsed = ParseJson<ResponseResult>(response)
                ?? new ResponseResult { Message = response };

            if (conversation != null)
            {
                conversation.Turns.Add(new ConversationTurn
                {
                    Role = "user", Content = request.Question, Timestamp = DateTime.UtcNow
                });
                conversation.Turns.Add(new ConversationTurn
                {
                    Role = "assistant", Content = parsed.Message, Timestamp = DateTime.UtcNow
                });
                await _conversationStore.SaveAsync(request.ConversationId, conversation, ct);
            }

            sw.Stop();
            return new ChatResponse
            {
                Success = true,
                ConversationId = request.ConversationId,
                Message = parsed.Message,
                RelatedDocuments = kbDocs,
                Suggestions = parsed.Suggestions,
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Chat] Ask failed");
            return new ChatResponse
            {
                Success = false,
                ErrorMessage = "Failed to process question.",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task LogFeedbackAsync(string conversationId, bool positive, string? feedbackText, CancellationToken ct = default)
    {
        var conversation = await _conversationStore.LoadAsync(conversationId, ct);
        if (conversation != null)
        {
            conversation.Feedback = new ConversationFeedback
            {
                Positive = positive,
                Text = feedbackText,
                Timestamp = DateTime.UtcNow
            };
            await _conversationStore.SaveAsync(conversationId, conversation, ct);
        }
    }

    private async Task<ContextDetectionResult> DetectContextAsync(
        CleanedFragment cleaned, CircleProcessRequest request, CancellationToken ct)
    {
        var systemPrompt = @"You are a context detection engine. Analyze the captured DOM content from a webpage
and determine: what the user is looking at, what type of content it is, and what would be
a good search query to find related information in a knowledge base.

Return JSON: { ""contextSummary"": ""brief description"", ""contentType"": ""text|form|data|navigation|interactive"", ""searchQuery"": ""search terms"" }";

        var userPrompt = $@"Page URL: {request.PageUrl}
Page Title: {request.PageTitle ?? "Unknown"}
Captured Content:
{cleaned.CleanedText}";

        var response = await _ai.ChatAsync(systemPrompt, userPrompt, _settings.Model, 500, 0.2, ct);
        return ParseJson<ContextDetectionResult>(response) ?? new ContextDetectionResult();
    }

    private async Task<ResponseResult> GenerateResponseAsync(
        ContextDetectionResult context, List<KbDocument> kbDocs, CancellationToken ct)
    {
        var kbContent = string.Join("\n", kbDocs.Select(d => $"- {d.Title}: {d.Snippet}"));

        var systemPrompt = @"You are Experion, a helpful AI assistant embedded in a website.
Based on the detected context and KB results, generate a helpful response for the user.
Be concise, clear, and actionable.

Return JSON: { ""message"": ""your response"", ""suggestions"": [""follow-up suggestion 1"", ""suggestion 2""] }";

        var userPrompt = $@"## Context
{context.ContextSummary}
Content Type: {context.ContentType}

## Knowledge Base Results
{(string.IsNullOrEmpty(kbContent) ? "No relevant documents found." : kbContent)}

Generate a helpful popup response for the user.";

        var response = await _ai.ChatAsync(systemPrompt, userPrompt, _settings.Model,
            _settings.MaxOutputTokens, 0.4, ct);

        return ParseJson<ResponseResult>(response)
            ?? new ResponseResult { Message = response };
    }

    private static string BuildHistoryContext(ConversationLog conversation)
    {
        var sb = new StringBuilder();
        foreach (var turn in conversation.Turns.TakeLast(10))
            sb.AppendLine($"[{turn.Role}]: {turn.Content}");
        return sb.ToString();
    }

    private static T? ParseJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n', 2).Last();
            if (cleaned.EndsWith("```")) cleaned = cleaned[..cleaned.LastIndexOf("```")];
            return JsonSerializer.Deserialize<T>(cleaned.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}

internal class ContextDetectionResult
{
    public string ContextSummary { get; set; } = "Circled content";
    public string ContentType { get; set; } = "text";
    public string SearchQuery { get; set; } = string.Empty;
}

internal class ResponseResult
{
    public string Message { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
}
