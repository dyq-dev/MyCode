using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Retrieval;

public class KnowledgeQueryStore : IQueryStore
{
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;

    public KnowledgeQueryStore(IVectorStore vectorStore, IOptions<RagOptions> options)
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
            var metadata = r.Metadata;
            var sourceType = metadata.GetValueOrDefault(CodeRagSchema.FieldSourceType, "");

            IKnowledgeChunk chunk = sourceType == CodeRagSchema.SourceTypeCode
                ? CodeRagMapper.ToCodeChunk(metadata, r.Id)
                : MapToKnowledgeChunk(metadata, r.Id);

            return new RetrievedKnowledgeChunk
            {
                Chunk = chunk,
                Score = r.Score
            };
        }).ToList();
    }

    private static KnowledgeChunk MapToKnowledgeChunk(Dictionary<string, string> metadata, string id)
    {
        var chunk = new KnowledgeChunk
        {
            Id = id,
            Content = metadata.GetValueOrDefault(CodeRagSchema.FieldContent, ""),
            SourceUri = metadata.GetValueOrDefault(CodeRagSchema.FieldSourceUri, ""),
            ProjectPath = metadata.GetValueOrDefault(CodeRagSchema.FieldProjectPath, ""),
            IndexedAt = ParseDateTime(metadata.GetValueOrDefault(CodeRagSchema.FieldIndexedAt, "")),
            SourceId = metadata.GetValueOrDefault(CodeRagSchema.FieldSourceId, ""),
        };

        if (metadata.TryGetValue(CodeRagSchema.FieldSourceType, out var st))
            chunk.SourceType = st switch
            {
                CodeRagSchema.SourceTypeCode => SourceType.Code,
                CodeRagSchema.SourceTypeDocument => SourceType.Document,
                _ => SourceType.Unknown
            };

        foreach (var kv in metadata)
        {
            if (!IsSystemField(kv.Key))
                chunk.Metadata[kv.Key] = kv.Value;
        }

        return chunk;
    }

    private static bool IsSystemField(string key) => key switch
    {
        CodeRagSchema.FieldType => true,
        CodeRagSchema.FieldSourceId => true,
        CodeRagSchema.FieldSourceType => true,
        CodeRagSchema.FieldSourceUri => true,
        CodeRagSchema.FieldContent => true,
        CodeRagSchema.FieldProjectPath => true,
        CodeRagSchema.FieldIndexedAt => true,
        _ => false
    };

    private static DateTime ParseDateTime(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result)
            ? result : DateTime.MinValue;
}
