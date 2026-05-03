namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Generates text embeddings used for vector KB search.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
