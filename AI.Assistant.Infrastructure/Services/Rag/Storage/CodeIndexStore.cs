using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

public class CodeIndexStore : IKnowledgeStore
{
    private const int VectorSize = 512;

    private readonly IVectorStore _vectorStore;
    private readonly IQdrantIndexStorage _storage;
    private readonly RagOptions _options;
    private readonly IEmbeddingService _embeddingService;

    public CodeIndexStore(IVectorStore vectorStore, IQdrantIndexStorage storage, RagOptions options, IEmbeddingService embeddingService)
    {
        _vectorStore = vectorStore;
        _storage = storage;
        _options = options;
        _embeddingService = embeddingService;
    }

    public async Task SaveChunksAsync(
        IEnumerable<IKnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunksList = chunks.ToList();
        if (chunksList.Count == 0)
            return;

        var collection = _options.QdrantCollectionName;
        await EnsureCollectionAsync(collection, cancellationToken);

        const int maxChars = 500;
        var embedResults = await _embeddingService.EmbedBatchAsync(
            chunksList.Select(c => c.Content.Length > maxChars ? c.Content[..maxChars] : c.Content),
            cancellationToken);

        for (int i = 0; i < chunksList.Count; i++)
        {
            var chunk = chunksList[i];
            var vector = i < embedResults.Count ? embedResults[i] : new float[VectorSize];

            if (chunk is CodeChunk codeChunk)
            {
                var metadata = new Dictionary<string, string>
                {
                    [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
                    [CodeRagSchema.FieldSourceId] = codeChunk.SourceId,
                    [CodeRagSchema.FieldSourceType] = CodeRagSchema.SourceTypeCode,
                    [CodeRagSchema.FieldSourceUri] = codeChunk.ProjectPath,
                    [CodeRagSchema.FieldFilePath] = codeChunk.FilePath,
                    [CodeRagSchema.FieldContent] = codeChunk.Content,
                    [CodeRagSchema.FieldLanguage] = codeChunk.Language,
                    [CodeRagSchema.FieldChunkType] = codeChunk.ChunkType.ToString(),
                    [CodeRagSchema.FieldStartLine] = codeChunk.StartLine.ToString(),
                    [CodeRagSchema.FieldEndLine] = codeChunk.EndLine.ToString(),
                    [CodeRagSchema.FieldProjectPath] = codeChunk.ProjectPath,
                    [CodeRagSchema.FieldIndexedAt] = codeChunk.IndexedAt.ToString("O")
                };

                metadata[CodeRagSchema.FieldNamespace] = codeChunk.Namespace ?? "";
                metadata[CodeRagSchema.FieldClassName] = codeChunk.ClassName ?? "";
                metadata[CodeRagSchema.FieldMethodName] = codeChunk.MethodName ?? "";
                metadata[CodeRagSchema.FieldSymbolName] = codeChunk.SymbolName ?? "";

                await _vectorStore.UpsertAsync(collection, codeChunk.Id, vector, metadata, cancellationToken);
            }
            else
            {
                var sourceTypeStr = chunk.SourceType switch
                {
                    SourceType.Code => CodeRagSchema.SourceTypeCode,
                    SourceType.Document or SourceType.Markdown or SourceType.Text => CodeRagSchema.SourceTypeDocument,
                    _ => "unknown"
                };

                var metadata = new Dictionary<string, string>
                {
                    [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
                    [CodeRagSchema.FieldSourceId] = chunk.SourceId,
                    [CodeRagSchema.FieldSourceType] = sourceTypeStr,
                    [CodeRagSchema.FieldSourceUri] = chunk.SourceUri,
                    [CodeRagSchema.FieldContent] = chunk.Content,
                    [CodeRagSchema.FieldProjectPath] = chunk.ProjectPath,
                    [CodeRagSchema.FieldIndexedAt] = chunk.IndexedAt.ToString("O")
                };

                foreach (var kv in chunk.Metadata)
                    metadata[kv.Key] = kv.Value;

                await _vectorStore.UpsertAsync(collection, chunk.Id, vector, metadata, cancellationToken);
            }
        }

        // Index records only for CodeChunks (file-level tracking)
        var codeChunks = chunksList.OfType<CodeChunk>().ToList();
        if (codeChunks.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var groupedByFile = codeChunks
            .GroupBy(c => c.FilePath)
            .Select(g => new { FilePath = g.Key, IndexedAt = now });

        foreach (var group in groupedByFile)
        {
            var recordId = DeterministicId("idx_", group.FilePath);
            var recordMetadata = new Dictionary<string, string>
            {
                [CodeRagSchema.FieldType] = CodeRagSchema.TypeIndexRecord,
                [CodeRagSchema.FieldFilePath] = group.FilePath,
                [CodeRagSchema.FieldFileHash] = "",
                [CodeRagSchema.FieldLastModifiedAt] = "",
                [CodeRagSchema.FieldIndexedAt] = group.IndexedAt.ToString("O"),
                [CodeRagSchema.FieldProjectPath] = codeChunks.First(c => c.FilePath == group.FilePath).ProjectPath
            };

            await _vectorStore.UpsertAsync(collection, recordId, new float[VectorSize], recordMetadata, cancellationToken);
        }
    }

    public async Task DeleteChunksByFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeChunk), (CodeRagSchema.FieldFilePath, filePath)], cancellationToken);
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeIndexRecord), (CodeRagSchema.FieldFilePath, filePath)], cancellationToken);
    }

    public async Task DeleteChunksBySourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeChunk), (CodeRagSchema.FieldSourceId, sourceId)], cancellationToken);
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeIndexRecord), (CodeRagSchema.FieldSourceId, sourceId)], cancellationToken);
    }

    public async Task DeleteProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeChunk), (CodeRagSchema.FieldProjectPath, projectPath)], cancellationToken);
        await DeleteByFilterAsync(collection, [(CodeRagSchema.FieldType, CodeRagSchema.TypeIndexRecord), (CodeRagSchema.FieldProjectPath, projectPath)], cancellationToken);
    }

    public async Task<IList<IndexFileRecord>> GetIndexedFilesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await EnsureCollectionAsync(collection, cancellationToken);
        return await ScrollIndexRecordsAsync(collection, projectPath, cancellationToken);
    }

    private async Task DeleteByFilterAsync(string collection, IEnumerable<(string Key, string Value)> conditions, CancellationToken ct)
    {
        var filter = new Filter();
        foreach (var (key, value) in conditions)
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = key,
                    Match = new Match { Keywords = new RepeatedStrings { Strings = { value } } }
                }
            });
        }

        await _storage.DeleteByFilterAsync(collection, filter, ct);
    }

    private async Task<List<IndexFileRecord>> ScrollIndexRecordsAsync(string collection, string projectPath, CancellationToken ct)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = CodeRagSchema.FieldType,
                Match = new Match { Keywords = new RepeatedStrings { Strings = { CodeRagSchema.TypeIndexRecord } } }
            }
        });
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = CodeRagSchema.FieldProjectPath,
                Match = new Match { Keywords = new RepeatedStrings { Strings = { projectPath } } }
            }
        });

        var points = await _storage.ScrollAllAsync(collection, filter, ct);

        return points.Select(point =>
        {
            var payload = point.Payload;
            return new IndexFileRecord
            {
                FilePath = payload[CodeRagSchema.FieldFilePath].StringValue,
                FileHash = payload[CodeRagSchema.FieldFileHash].StringValue,
                LastModifiedAt = TryParseDateTime(payload[CodeRagSchema.FieldLastModifiedAt].StringValue),
                IndexedAt = TryParseDateTime(payload[CodeRagSchema.FieldIndexedAt].StringValue)
            };
        }).ToList();
    }

    private async Task EnsureCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        if (await _storage.CollectionExistsAsync(collection, cancellationToken))
            return;

        await _storage.CreateCollectionAsync(collection, new VectorParams
        {
            Size = VectorSize,
            Distance = Distance.Cosine
        }, cancellationToken);
    }

    private static string DeterministicId(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + value));
        return new Guid(hash[..16]).ToString("N");
    }

    private static DateTime TryParseDateTime(string value)
    {
        if (string.IsNullOrEmpty(value))
            return DateTime.MinValue;
        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result))
            return result;
        return DateTime.MinValue;
    }
}
