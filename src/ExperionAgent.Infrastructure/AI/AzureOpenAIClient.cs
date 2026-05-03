using System.ClientModel;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace ExperionAgent.Infrastructure.AI;

public class AzureOpenAIClient : IAIClient
{
    private readonly OpenAISettings _settings;
    private readonly ILogger<AzureOpenAIClient> _log;

    public AzureOpenAIClient(IOptions<OpenAISettings> settings, ILogger<AzureOpenAIClient> log)
    {
        _settings = settings.Value;
        _log = log;
    }

    public async Task<string> ChatAsync(
        string systemPrompt, string userMessage,
        string? model = null, int maxTokens = 2000, double temperature = 0.3,
        CancellationToken ct = default)
    {
        var deploymentModel = model ?? _settings.DefaultModel;

        var client = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(_settings.Endpoint),
            new ApiKeyCredential(_settings.ApiKey));

        var chatClient = client.GetChatClient(deploymentModel);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = (float)temperature
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var completion = await chatClient.CompleteChatAsync(messages, options, ct);
        var response = completion.Value.Content[0].Text;

        _log.LogDebug("[AI] Chat completed with {Model}, tokens: {Usage}",
            deploymentModel, completion.Value.Usage?.TotalTokenCount);

        return response;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(_settings.EmbeddingEndpoint),
            new ApiKeyCredential(_settings.EmbeddingApiKey));

        var embeddingClient = client.GetEmbeddingClient(_settings.EmbeddingModel);
        var result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
