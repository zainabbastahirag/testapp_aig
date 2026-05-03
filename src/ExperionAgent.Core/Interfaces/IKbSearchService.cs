using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IKbSearchService
{
    Task<List<KbDocument>> SearchAsync(string query, string indexName, int topK = 5, CancellationToken ct = default);
}
