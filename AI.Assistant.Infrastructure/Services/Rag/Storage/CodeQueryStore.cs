using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

/// <summary>
/// 代码查询存储器——封装 IVectorStore + CodeRagSchema 常量，供 ICodeRetriever 调用。
/// 职责唯一：接收查询向量 → 搜索 Qdrant（自动加 _type = "chunk" 过滤）→ 返回 RetrievedCodeChunk。
/// 所有 Qdrant 字段名、类型标识等细节通过 CodeRagSchema / CodeRagMapper 集中管理。
/// </summary>
public class CodeQueryStore : ICodeQueryStore
{
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;

    public CodeQueryStore(IVectorStore vectorStore, IOptions<RagOptions> options)
    {
        _vectorStore = vectorStore;
        _options = options.Value;
    }

    public async Task<IList<RetrievedCodeChunk>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        // 自动过滤只搜索代码分块，排除索引记录
        var filter = new Dictionary<string, string>
        {
            [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk
        };

        var results = await _vectorStore.SearchAsync(
            _options.QdrantCollectionName,
            queryVector,
            topK,
            filter,
            cancellationToken);

        return results.Select(r =>
        {
            var chunk = CodeRagMapper.ToCodeChunk(r.Metadata, r.Id);
            return new RetrievedCodeChunk
            {
                Chunk = chunk,
                Score = r.Score
            };
        }).ToList();
    }
}
