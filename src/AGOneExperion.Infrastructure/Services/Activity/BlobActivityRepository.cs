using System.Text;
using System.Text.Json;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace AGOneExperion.Infrastructure.Services.Activity;

/// <summary>
/// Blob Storage-backed activity repository.
///
/// Layout:
///   Container: experion-activity
///     {userId}/events.ndjson   — append-only newline-delimited JSON event log
///
///   Container: experion-profiles
///     {userId}/profile.json    — latest aggregated profile (overwritten on each update)
/// </summary>
public class BlobActivityRepository : IActivityRepository
{
    private readonly BlobServiceClient _blobService;
    private readonly string _activityContainer;
    private readonly string _profileContainer;
    private readonly ILogger<BlobActivityRepository> _log;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BlobActivityRepository(
        BlobServiceClient blobService,
        string activityContainer,
        string profileContainer,
        ILogger<BlobActivityRepository> log)
    {
        _blobService = blobService;
        _activityContainer = activityContainer;
        _profileContainer = profileContainer;
        _log = log;
    }

    // ── Append events ────────────────────────────────────────────────

    public async Task AppendEventsAsync(string userId, IEnumerable<ActivityEvent> events, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_activityContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobName = $"{SanitiseKey(userId)}/events.ndjson";
        var appendBlob = container.GetAppendBlobClient(blobName);
        await appendBlob.CreateIfNotExistsAsync(cancellationToken: ct);

        var sb = new StringBuilder();
        foreach (var ev in events)
            sb.AppendLine(JsonSerializer.Serialize(ev, _json));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await appendBlob.AppendBlockAsync(stream, cancellationToken: ct);

        _log.LogDebug("[Activity] Appended {Count} events for user {UserId}", sb.ToString().Split('\n').Length, userId);
    }

    // ── Read recent events ───────────────────────────────────────────

    public async Task<List<ActivityEvent>> GetRecentEventsAsync(string userId, int maxEvents = 200, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_activityContainer);
        var blobName = $"{SanitiseKey(userId)}/events.ndjson";
        var blob = container.GetAppendBlobClient(blobName);

        if (!await blob.ExistsAsync(ct)) return new List<ActivityEvent>();

        using var stream = new MemoryStream();
        await blob.DownloadToAsync(stream, ct);
        var content = Encoding.UTF8.GetString(stream.ToArray());

        var events = new List<ActivityEvent>();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var ev = JsonSerializer.Deserialize<ActivityEvent>(line, _json);
                if (ev != null) events.Add(ev);
            }
            catch { /* skip malformed lines */ }
        }

        // Return newest-first, capped at maxEvents
        events.Sort((a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));
        return events.Take(maxEvents).ToList();
    }

    // ── Profile ──────────────────────────────────────────────────────

    public async Task<UserProfile?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_profileContainer);
        var blobName = $"{SanitiseKey(userId)}/profile.json";
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(ct)) return null;

        var download = await blob.DownloadContentAsync(ct);
        return JsonSerializer.Deserialize<UserProfile>(download.Value.Content.ToString(), _json);
    }

    public async Task SaveProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(_profileContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobName = $"{SanitiseKey(profile.UserId)}/profile.json";
        var blob = container.GetBlobClient(blobName);
        var json = JsonSerializer.Serialize(profile, _json);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, ct);

        _log.LogDebug("[Activity] Saved profile for user {UserId}", profile.UserId);
    }

    private static string SanitiseKey(string userId) =>
        userId.Replace('/', '_').Replace('\\', '_').Replace(':', '_').ToLowerInvariant();
}
