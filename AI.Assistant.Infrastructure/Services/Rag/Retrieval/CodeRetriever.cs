using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Retrieval;

public class CodeRetriever : IRetriever
{
    private readonly IEmbeddingService _embedding;
    private readonly IQueryStore _queryStore;
    private readonly RagOptions _options;

    public CodeRetriever(
        IEmbeddingService embedding,
        IQueryStore queryStore,
        IOptions<RagOptions> options)
    {
        _embedding = embedding;
        _queryStore = queryStore;
        _options = options.Value;
    }

    public async Task<IList<RetrievedKnowledgeChunk>> VectorSearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var actualTopK = Math.Min(topK, _options.MaxTopK);

        var vector = await _embedding.EmbedAsync(query, cancellationToken);
        var results = await _queryStore.SearchAsync(vector, actualTopK, cancellationToken);

        return results;
    }
}
