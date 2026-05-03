using System.Text.Json;
using Azure.Storage.Blobs;
using ExperionAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExperionAgent.Infrastructure.Storage;

public class ConversationStore
{
    private readonly BlobServiceClient _blobService;
    private readonly StorageSettings _settings;
    private readonly ILogger<ConversationStore> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ConversationStore(IOptions<StorageSettings> settings, ILogger<ConversationStore> log)
    {
        _settings = settings.Value;
        _log = log;
        _blobService = new BlobServiceClient(_settings.BlobConnectionString);
    }

    public async Task SaveAsync(string conversationId, ConversationLog log, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_settings.ConversationContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient($"{conversationId}.json");
        var json = JsonSerializer.Serialize(log, JsonOpts);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, ct);
    }

    public async Task<ConversationLog?> LoadAsync(string conversationId, CancellationToken ct = default)
    {
        try
        {
            var container = _blobService.GetBlobContainerClient(_settings.ConversationContainerName);
            var blob = container.GetBlobClient($"{conversationId}.json");
            if (!await blob.ExistsAsync(ct)) return null;
            var response = await blob.DownloadContentAsync(ct);
            return JsonSerializer.Deserialize<ConversationLog>(
                response.Value.Content.ToString(), JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[ConvStore] Failed to load {Id}", conversationId);
            return null;
        }
    }
}

public class ConversationLog
{
    public string ConversationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? ContextSummary { get; set; }
    public List<ConversationTurn> Turns { get; set; } = new();
    public ConversationFeedback? Feedback { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ConversationTurn
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ConversationFeedback
{
    public bool Positive { get; set; }
    public string? Text { get; set; }
    public DateTime Timestamp { get; set; }
}
