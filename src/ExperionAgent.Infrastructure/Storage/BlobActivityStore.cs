using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using ExperionAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExperionAgent.Infrastructure.Storage;

public class BlobActivityStore : IActivityStore
{
    private readonly BlobServiceClient _blobService;
    private readonly StorageSettings _settings;
    private readonly ILogger<BlobActivityStore> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BlobActivityStore(IOptions<StorageSettings> settings, ILogger<BlobActivityStore> log)
    {
        _settings = settings.Value;
        _log = log;
        _blobService = new BlobServiceClient(_settings.BlobConnectionString);
    }

    public async Task AppendEventsAsync(string siteId, string userId, List<ActivityEvent> events, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_settings.ActivityContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var blobPath = $"{siteId}/{userId}/{date}.jsonl";
        var appendBlob = container.GetAppendBlobClient(blobPath);

        try
        {
            await appendBlob.CreateIfNotExistsAsync(cancellationToken: ct);
        }
        catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "BlobAlreadyExists")
        {
            // expected on concurrent calls
        }

        var sb = new StringBuilder();
        foreach (var e in events)
            sb.AppendLine(JsonSerializer.Serialize(e, JsonOpts));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await appendBlob.AppendBlockAsync(stream, cancellationToken: ct);

        _log.LogDebug("[Store] Appended {Count} events to {Path}", events.Count, blobPath);
    }

    public async Task<UserProfile?> GetProfileAsync(string siteId, string userId, CancellationToken ct = default)
    {
        return await ReadJsonBlobAsync<UserProfile>(
            _settings.ActivityContainerName,
            $"{siteId}/{userId}/profile.json", ct);
    }

    public async Task SaveProfileAsync(string siteId, string userId, UserProfile profile, CancellationToken ct = default)
    {
        await WriteJsonBlobAsync(
            _settings.ActivityContainerName,
            $"{siteId}/{userId}/profile.json", profile, ct);
    }

    public async Task<List<ActivityEvent>> GetRecentEventsAsync(string siteId, string userId, int count = 50, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_settings.ActivityContainerName);
        if (!await container.ExistsAsync(ct))
            return new List<ActivityEvent>();

        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var blobPath = $"{siteId}/{userId}/{date}.jsonl";
        var blobClient = container.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync(ct))
            return new List<ActivityEvent>();

        var response = await blobClient.DownloadContentAsync(ct);
        var lines = response.Value.Content.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var events = new List<ActivityEvent>();
        foreach (var line in lines.TakeLast(count))
        {
            try
            {
                var e = JsonSerializer.Deserialize<ActivityEvent>(line, JsonOpts);
                if (e != null) events.Add(e);
            }
            catch { /* skip malformed lines */ }
        }

        return events;
    }

    public async Task SaveRecommendationsAsync(string siteId, string userId, List<Recommendation> recs, CancellationToken ct = default)
    {
        await WriteJsonBlobAsync(
            _settings.ActivityContainerName,
            $"{siteId}/{userId}/recommendations.json", recs, ct);
    }

    public async Task<List<Recommendation>> GetLastRecommendationsAsync(string siteId, string userId, CancellationToken ct = default)
    {
        return await ReadJsonBlobAsync<List<Recommendation>>(
            _settings.ActivityContainerName,
            $"{siteId}/{userId}/recommendations.json", ct) ?? new List<Recommendation>();
    }

    private async Task<T?> ReadJsonBlobAsync<T>(string containerName, string blobPath, CancellationToken ct) where T : class
    {
        try
        {
            var container = _blobService.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobPath);
            if (!await blob.ExistsAsync(ct)) return null;
            var response = await blob.DownloadContentAsync(ct);
            return JsonSerializer.Deserialize<T>(response.Value.Content.ToString(), JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Store] Failed to read {Path}", blobPath);
            return null;
        }
    }

    private async Task WriteJsonBlobAsync<T>(string containerName, string blobPath, T data, CancellationToken ct)
    {
        var container = _blobService.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient(blobPath);
        var json = JsonSerializer.Serialize(data, JsonOpts);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, ct);
    }
}
