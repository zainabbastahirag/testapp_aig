using System.Text;
using System.Text.Json;

namespace Experion.Api.Storage;

/// <summary>
/// Local-filesystem implementation of IBlobStore. Used when no Azure Storage
/// connection string is configured. Writes to <root>/<container>/<path>.
/// </summary>
public class LocalFileBlobStore : IBlobStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public LocalFileBlobStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task AppendJsonLineAsync(string container, string path, object record, CancellationToken ct = default)
    {
        var full = ResolvePath(container, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var line = JsonSerializer.Serialize(record, JsonOpts) + "\n";

        await _gate.WaitAsync(ct);
        try { await File.AppendAllTextAsync(full, line, Encoding.UTF8, ct); }
        finally { _gate.Release(); }
    }

    public async Task<List<string>> ReadAllLinesAsync(string container, string path, CancellationToken ct = default)
    {
        var full = ResolvePath(container, path);
        if (!File.Exists(full)) return new();
        var text = await File.ReadAllTextAsync(full, Encoding.UTF8, ct);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<List<string>> ReadTailAsync(string container, string path, int n, CancellationToken ct = default)
    {
        var all = await ReadAllLinesAsync(container, path, ct);
        return all.TakeLast(n).ToList();
    }

    public Task<List<string>> ListAsync(string container, string prefix, CancellationToken ct = default)
    {
        var full = ResolvePath(container, prefix);
        var dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? full;
        if (!Directory.Exists(dir)) return Task.FromResult(new List<string>());
        var rootContainer = Path.Combine(_root, container);
        var paths = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(p => p.StartsWith(Path.Combine(_root, container, prefix), StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).StartsWith(Path.GetFileName(prefix), StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(rootContainer, p).Replace('\\', '/'))
            .OrderBy(p => p)
            .ToList();
        return Task.FromResult(paths);
    }

    private string ResolvePath(string container, string path) =>
        Path.Combine(_root, container, path.Replace("..", "").TrimStart('/', '\\'));
}
