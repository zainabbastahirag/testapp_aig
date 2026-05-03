using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Experion.Api.Storage;

/// <summary>
/// Azure Blob Storage implementation of IBlobStore using Append Blobs.
/// Append Blobs are designed exactly for this — they support concurrent
/// appends without read-modify-write, which is perfect for activity logs
/// and conversation history JSONL files.
/// </summary>
public class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public AzureBlobStore(string connectionString)
    {
        _service = new BlobServiceClient(connectionString);
    }

    public async Task AppendJsonLineAsync(string container, string path, object record, CancellationToken ct = default)
    {
        var c = _service.GetBlobContainerClient(container);
        await c.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blob = c.GetAppendBlobClient(path);
        if (!await blob.ExistsAsync(ct))
        {
            await blob.CreateIfNotExistsAsync(cancellationToken: ct);
        }

        var line = JsonSerializer.Serialize(record, JsonOpts) + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        await blob.AppendBlockAsync(ms, cancellationToken: ct);
    }

    public async Task<List<string>> ReadAllLinesAsync(string container, string path, CancellationToken ct = default)
    {
        var c = _service.GetBlobContainerClient(container);
        var blob = c.GetBlobClient(path);
        if (!await blob.ExistsAsync(ct)) return new();

        var resp = await blob.DownloadContentAsync(ct);
        var text = resp.Value.Content.ToString();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<List<string>> ReadTailAsync(string container, string path, int n, CancellationToken ct = default)
    {
        var all = await ReadAllLinesAsync(container, path, ct);
        return all.TakeLast(n).ToList();
    }

    public async Task<List<string>> ListAsync(string container, string prefix, CancellationToken ct = default)
    {
        var c = _service.GetBlobContainerClient(container);
        if (!await c.ExistsAsync(ct)) return new();
        var paths = new List<string>();
        await foreach (var b in c.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            paths.Add(b.Name);
        return paths;
    }
}
