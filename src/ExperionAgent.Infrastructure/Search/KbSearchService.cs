using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using ExperionAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExperionAgent.Infrastructure.Search;

public class KbSearchService : IKbSearchService
{
    private readonly SearchSettings _settings;
    private readonly IAIClient _aiClient;
    private readonly ILogger<KbSearchService> _log;

    public KbSearchService(IOptions<SearchSettings> settings, IAIClient aiClient, ILogger<KbSearchService> log)
    {
        _settings = settings.Value;
        _aiClient = aiClient;
        _log = log;
    }

    public async Task<List<KbDocument>> SearchAsync(string query, string indexName, int topK = 5, CancellationToken ct = default)
    {
        try
        {
            var searchClient = new SearchClient(
                new Uri(_settings.Endpoint),
                indexName,
                new AzureKeyCredential(_settings.ApiKey));

            var vector = await _aiClient.EmbedAsync(query, ct);

            var options = new SearchOptions
            {
                Size = topK,
                Select = { "title", "content", "url" },
                VectorSearch = new()
                {
                    Queries =
                    {
                        new VectorizedQuery(new ReadOnlyMemory<float>(vector))
                        {
                            KNearestNeighborsCount = topK,
                            Fields = { "content_vector" }
                        }
                    }
                }
            };

            var response = await searchClient.SearchAsync<SearchDocument>(query, options, ct);
            var results = new List<KbDocument>();

            await foreach (var r in response.Value.GetResultsAsync())
            {
                results.Add(new KbDocument
                {
                    Title = r.Document.TryGetValue("title", out var t) ? t?.ToString() ?? "Untitled" : "Untitled",
                    Snippet = r.Document.TryGetValue("content", out var c) ? c?.ToString() : null,
                    Url = r.Document.TryGetValue("url", out var u) ? u?.ToString() : null,
                    RelevanceScore = r.Score ?? 0
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[KbSearch] Search failed for index {Index}", indexName);
            return new List<KbDocument>();
        }
    }
}
