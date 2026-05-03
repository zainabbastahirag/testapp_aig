using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;

namespace AGOneExperion.Infrastructure.Services.Search;

/// <summary>
/// Azure AI Search-backed knowledge base retrieval.
/// Supports hybrid search: keyword + optional vector (when embeddings are available).
/// </summary>
public class AzureSearchService : IKnowledgeBaseSearch
{
    private readonly SearchIndexClientFactory _factory;
    private readonly ILogger<AzureSearchService> _log;

    public AzureSearchService(SearchIndexClientFactory factory, ILogger<AzureSearchService> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<List<DocumentReference>> SearchAsync(
        string indexName,
        string query,
        float[]? vector = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        try
        {
            var client = _factory.GetClient(indexName);

            var options = new SearchOptions
            {
                Size = topK,
                Select = { "title", "content", "url", "id" },
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = "default"
                }
            };

            if (vector != null)
            {
                options.VectorSearch = new VectorSearchOptions();
                options.VectorSearch.Queries.Add(new VectorizedQuery(vector)
                {
                    Fields = { "content_vector" },
                    KNearestNeighborsCount = topK
                });
            }

            var results = await client.SearchAsync<SearchDocument>(query, options, ct);
            var docs = new List<DocumentReference>();

            await foreach (var result in results.Value.GetResultsAsync())
            {
                docs.Add(new DocumentReference
                {
                    Title = result.Document.TryGetValue("title", out var t) ? t?.ToString() ?? "Untitled" : "Untitled",
                    Snippet = result.Document.TryGetValue("content", out var c) ? c?.ToString()?[..Math.Min(c.ToString()!.Length, 400)] : null,
                    Url = result.Document.TryGetValue("url", out var u) ? u?.ToString() : null,
                    RelevanceScore = result.Score ?? 0
                });
            }

            _log.LogDebug("[Search] Index={Index}, Query={Query}, Results={Count}", indexName, query, docs.Count);
            return docs;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Search] Failed for index={Index}", indexName);
            return new List<DocumentReference>();
        }
    }
}

/// <summary>
/// Factory that builds a <see cref="SearchClient"/> per index name.
/// </summary>
public class SearchIndexClientFactory
{
    private readonly Uri _endpoint;
    private readonly AzureKeyCredential _credential;

    public SearchIndexClientFactory(string endpoint, string apiKey)
    {
        _endpoint = new Uri(endpoint);
        _credential = new AzureKeyCredential(apiKey);
    }

    public SearchClient GetClient(string indexName) =>
        new(_endpoint, indexName, _credential);
}
