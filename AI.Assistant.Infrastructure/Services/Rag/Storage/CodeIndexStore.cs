using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

/// <summary>
/// 代码索引存储器——基于 Qdrant（IVectorStore + IQdrantIndexStorage）持久化分块和索引记录。
/// 每个代码块作为一个 Qdrant point 存储，附带完整的元数据；每个文件对应一条索引记录
/// （_type = "index_record"），用于增量索引的比较和清理。
/// </summary>
public class CodeIndexStore : ICodeIndexStore
{
    // Qdrant 集合向量维度，需与 Embedding 模型输出维度一致
    private const int VectorSize = 512;

    private readonly IVectorStore _vectorStore;
    private readonly IQdrantIndexStorage _storage;
    private readonly RagOptions _options;

    public CodeIndexStore(IVectorStore vectorStore, IQdrantIndexStorage storage, RagOptions options)
    {
        _vectorStore = vectorStore;
        _storage = storage;
        _options = options;
    }

    /// <summary>旧重载：接收原始 CodeChunk，内部填充零向量后委托给新重载</summary>
    public Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        var embedded = chunks.Select(c => new EmbeddedChunk
        {
            Chunk = c,
            Vector = new float[VectorSize]
        });

        return SaveChunksAsync(embedded, cancellationToken);
    }

    /// <summary>
    /// 新重载：保存一批已嵌入的代码分块。
    /// 每个分块作为一个 Qdrant point 存入（含元数据），
    /// 再按文件路径分组为每个文件创建/更新一条索引记录。
    /// </summary>
    public async Task SaveChunksAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await EnsureCollectionAsync(collection, cancellationToken);

        var chunksList = chunks.ToList();
        if (chunksList.Count == 0)
            return;

        // 逐块写入 Qdrant：ID + 向量 + 元数据
        foreach (var embedded in chunksList)
        {
            var chunk = embedded.Chunk;
            var metadata = new Dictionary<string, string>
            {
                [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
                [CodeRagSchema.FieldFilePath] = chunk.FilePath,
                [CodeRagSchema.FieldContent] = chunk.Content,
                [CodeRagSchema.FieldLanguage] = chunk.Language,
                [CodeRagSchema.FieldChunkType] = chunk.ChunkType.ToString(),
                [CodeRagSchema.FieldStartLine] = chunk.StartLine.ToString(),
                [CodeRagSchema.FieldEndLine] = chunk.EndLine.ToString(),
                [CodeRagSchema.FieldProjectPath] = chunk.ProjectPath,
                [CodeRagSchema.FieldNamespace] = chunk.Namespace ?? "",
                [CodeRagSchema.FieldClassName] = chunk.ClassName ?? "",
                [CodeRagSchema.FieldMethodName] = chunk.MethodName ?? "",
                [CodeRagSchema.FieldSymbolName] = chunk.SymbolName ?? "",
                [CodeRagSchema.FieldIndexedAt] = chunk.IndexedAt.ToString("O")
            };

            await _vectorStore.UpsertAsync(collection, chunk.Id, embedded.Vector, metadata, cancellationToken);
        }

        // 按文件分组，每个文件创建一条索引记录（用于后续增量扫描对比）
        var now = DateTime.UtcNow;
        var groupedByFile = chunksList
            .GroupBy(c => c.Chunk.FilePath)
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
                [CodeRagSchema.FieldProjectPath] = chunksList.First(c => c.Chunk.FilePath == group.FilePath).Chunk.ProjectPath
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
