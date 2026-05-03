using System.Text;
using System.Text.Json;
using AGOneExperion.Core.Configuration;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace AGOneExperion.Infrastructure.Services.Chat;

/// <summary>
/// Azure OpenAI-backed implementation of <see cref="IChatService"/> and <see cref="IEmbeddingService"/>.
/// </summary>
public class AzureOpenAIService : IChatService, IEmbeddingService
{
    private readonly AzureOpenAIClient _chatClient;
    private readonly AzureOpenAIClient _embeddingClient;
    private readonly ExperionSettings _settings;
    private readonly ILogger<AzureOpenAIService> _log;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AzureOpenAIService(IOptions<ExperionSettings> settings, ILogger<AzureOpenAIService> log)
    {
        _settings = settings.Value;
        _log = log;

        _chatClient = new AzureOpenAIClient(
            new Uri(_settings.OpenAiEndpoint),
            new AzureKeyCredential(_settings.OpenAiApiKey));

        // Embedding may use a different endpoint / key
        var embEndpoint = string.IsNullOrEmpty(_settings.EmbeddingEndpoint)
            ? _settings.OpenAiEndpoint
            : _settings.EmbeddingEndpoint;
        var embKey = string.IsNullOrEmpty(_settings.EmbeddingApiKey)
            ? _settings.OpenAiApiKey
            : _settings.EmbeddingApiKey;

        _embeddingClient = new AzureOpenAIClient(
            new Uri(embEndpoint),
            new AzureKeyCredential(embKey));
    }

    // ── Chat ─────────────────────────────────────────────────────────

    public async Task<ChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model = "gpt-4.1",
        int maxTokens = 4096,
        double temperature = 0.7,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        try
        {
            var client = _chatClient.GetChatClient(model);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxTokens,
                Temperature = (float)temperature
            };

            var completion = await client.CompleteChatAsync(messages, options, ct);
            var content = completion.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            _log.LogDebug("[Chat] [{Id}] Tokens in={In}, out={Out}",
                correlationId ?? "n/a",
                completion.Value.Usage?.InputTokenCount,
                completion.Value.Usage?.OutputTokenCount);

            return new ChatResult
            {
                Success = true,
                Response = content,
                PromptTokens = completion.Value.Usage?.InputTokenCount ?? 0,
                CompletionTokens = completion.Value.Usage?.OutputTokenCount ?? 0
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Chat] Failed for correlationId={Id}", correlationId);
            return new ChatResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ── Embeddings ───────────────────────────────────────────────────

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = _embeddingClient.GetEmbeddingClient(_settings.EmbeddingModel);
        var result = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
