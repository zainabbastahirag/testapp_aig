using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Wraps Azure AI Search for KB document retrieval.
/// </summary>
public interface IKnowledgeBaseSearch
{
    Task<List<DocumentReference>> SearchAsync(
        string indexName,
        string query,
        float[]? vector = null,
        int topK = 5,
        CancellationToken ct = default);
}
