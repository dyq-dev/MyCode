using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

public class CodeQueryStore : IQueryStore
{
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;

    public CodeQueryStore(IVectorStore vectorStore, IOptions<RagOptions> options)
    {
        _vectorStore = vectorStore;
        _options = options.Value;
    }

    public async Task<IList<RetrievedKnowledgeChunk>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
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
            return new RetrievedKnowledgeChunk
            {
                Chunk = chunk,
                Score = r.Score
            };
        }).ToList();
    }
}
