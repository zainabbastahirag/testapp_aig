namespace ExperionAgent.Core.Interfaces;

public interface IAIClient
{
    Task<string> ChatAsync(string systemPrompt, string userMessage, string? model = null, int maxTokens = 2000, double temperature = 0.3, CancellationToken ct = default);
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
