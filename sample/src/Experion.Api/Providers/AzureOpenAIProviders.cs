using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Experion.Api.Providers;

public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;     // https://<resource>.openai.azure.com
    public string ApiKey { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = "gpt-4.1";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";
    public string ApiVersion { get; set; } = "2024-08-01-preview";
}

public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly AzureOpenAISettings _s;

    public AzureOpenAIEmbeddingProvider(HttpClient http, AzureOpenAISettings settings)
    {
        _http = http;
        _s = settings;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var url = $"{_s.Endpoint}/openai/deployments/{_s.EmbeddingDeployment}/embeddings?api-version={_s.ApiVersion}";
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { input = text })
        };
        req.Headers.Add("api-key", _s.ApiKey);

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
        return doc?.Data?.FirstOrDefault()?.Embedding ?? Array.Empty<float>();
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")] public List<EmbeddingItem>? Data { get; set; }
    }
    private class EmbeddingItem
    {
        [JsonPropertyName("embedding")] public float[]? Embedding { get; set; }
    }
}

public class AzureOpenAILlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly AzureOpenAISettings _s;

    public AzureOpenAILlmProvider(HttpClient http, AzureOpenAISettings settings)
    {
        _http = http;
        _s = settings;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var url = $"{_s.Endpoint}/openai/deployments/{_s.ChatDeployment}/chat/completions?api-version={_s.ApiVersion}";
        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = 0.4,
            max_tokens = 800
        };
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        req.Headers.Add("api-key", _s.ApiKey);

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var raw = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
        return content;
    }
}
