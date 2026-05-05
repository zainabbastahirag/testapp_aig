using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIBaba.Services;

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
    public int TimeoutSeconds { get; set; } = 60;
}

public interface IOllamaService
{
    /// <summary>
    /// Send a chat to the local Ollama server. Returns the assistant text.
    /// Throws OllamaUnavailableException if the daemon can't be reached.
    /// </summary>
    Task<string> ChatAsync(string systemPrompt, IEnumerable<ChatMessage> history, string userMessage, CancellationToken ct = default);
}

public class ChatMessage
{
    public string Role { get; set; } = "user";       // user | assistant | system
    public string Content { get; set; } = string.Empty;
}

public class OllamaUnavailableException : Exception
{
    public OllamaUnavailableException(string message, Exception inner) : base(message, inner) { }
}

public class OllamaService : IOllamaService
{
    private readonly HttpClient _http;
    private readonly OllamaSettings _settings;
    private readonly ILogger<OllamaService> _log;

    public OllamaService(HttpClient http, OllamaSettings settings, ILogger<OllamaService> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<string> ChatAsync(string systemPrompt, IEnumerable<ChatMessage> history, string userMessage, CancellationToken ct = default)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        foreach (var m in history)
            messages.Add(new { role = m.Role, content = m.Content });
        messages.Add(new { role = "user", content = userMessage });

        var payload = new
        {
            model = _settings.Model,
            messages,
            stream = false,
            options = new { temperature = 0.6, num_predict = 200 }
        };

        try
        {
            using var res = await _http.PostAsJsonAsync("api/chat", payload, ct);
            res.EnsureSuccessStatusCode();
            var raw = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c))
            {
                return c.GetString() ?? "";
            }
            return "";
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "Ollama request failed at {Url}", _settings.BaseUrl);
            throw new OllamaUnavailableException(
                $"Could not reach Ollama at {_settings.BaseUrl}. Make sure Ollama is running and the model '{_settings.Model}' is pulled.",
                ex);
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Ollama request timed out");
            throw new OllamaUnavailableException(
                $"Ollama timed out after {_settings.TimeoutSeconds}s. Try a smaller model or a shorter prompt.",
                ex);
        }
    }
}
