namespace Experion.Api.Storage;

/// <summary>
/// Append-only blob store for conversation history and activity events.
/// One blob per (container, path); each line is a JSON record (JSONL).
/// </summary>
public interface IBlobStore
{
    /// <summary>Append a single JSONL record to the blob at the given path. Creates the blob if it doesn't exist.</summary>
    Task AppendJsonLineAsync(string container, string path, object record, CancellationToken ct = default);

    /// <summary>Read all JSONL records as raw lines from the blob at the given path. Empty if not found.</summary>
    Task<List<string>> ReadAllLinesAsync(string container, string path, CancellationToken ct = default);

    /// <summary>Read the most recent N JSONL records (last N lines).</summary>
    Task<List<string>> ReadTailAsync(string container, string path, int n, CancellationToken ct = default);

    /// <summary>List blob paths under a prefix (used by inspectors / demo UI).</summary>
    Task<List<string>> ListAsync(string container, string prefix, CancellationToken ct = default);
}
