using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Retrieval;

/// <summary>
/// 代码检索器——Code RAG 查询入口。
/// 流程：Query → Embed → Search（ICodeQueryStore）→ TopK RetrievedCodeChunk。
/// 不依赖 IVectorStore，不关心底层向量库细节。
/// </summary>
public class CodeRetriever : ICodeRetriever
{
    private readonly IEmbeddingService _embedding;
    private readonly ICodeQueryStore _queryStore;
    private readonly RagOptions _options;

    public CodeRetriever(
        IEmbeddingService embedding,
        ICodeQueryStore queryStore,
        IOptions<RagOptions> options)
    {
        _embedding = embedding;
        _queryStore = queryStore;
        _options = options.Value;
    }

    public async Task<IList<RetrievedCodeChunk>> VectorSearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        // 保护：不允许超过配置上限
        var actualTopK = Math.Min(topK, _options.MaxTopK);

        var vector = await _embedding.EmbedAsync(query, cancellationToken);
        var results = await _queryStore.SearchAsync(vector, actualTopK, cancellationToken);

        return results;
    }
}
