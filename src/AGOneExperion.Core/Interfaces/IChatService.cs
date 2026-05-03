using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Wraps Azure OpenAI chat completion and template rendering.
/// </summary>
public interface IChatService
{
    Task<ChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model = "gpt-4.1",
        int maxTokens = 4096,
        double temperature = 0.7,
        string? correlationId = null,
        CancellationToken ct = default);
}

public class ChatResult
{
    public bool Success { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}
