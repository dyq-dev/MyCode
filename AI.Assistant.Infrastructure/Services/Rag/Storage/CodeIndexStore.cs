using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

public class CodeIndexStore : ICodeIndexStore
{
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

    public async Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await EnsureCollectionAsync(collection, cancellationToken);

        var chunksList = chunks.ToList();
        if (chunksList.Count == 0)
            return;

        foreach (var chunk in chunksList)
        {
            var metadata = new Dictionary<string, string>
            {
                ["_type"] = "chunk",
                ["file_path"] = chunk.FilePath,
                ["content"] = chunk.Content,
                ["language"] = chunk.Language,
                ["chunk_type"] = chunk.ChunkType.ToString(),
                ["start_line"] = chunk.StartLine.ToString(),
                ["end_line"] = chunk.EndLine.ToString(),
                ["project_path"] = chunk.ProjectPath,
                ["namespace"] = chunk.Namespace ?? "",
                ["class_name"] = chunk.ClassName ?? "",
                ["method_name"] = chunk.MethodName ?? "",
                ["symbol_name"] = chunk.SymbolName ?? "",
                ["indexed_at"] = chunk.IndexedAt.ToString("O")
            };

            await _vectorStore.UpsertAsync(collection, chunk.Id, new float[VectorSize], metadata, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var groupedByFile = chunksList
            .GroupBy(c => c.FilePath)
            .Select(g => new { FilePath = g.Key, IndexedAt = now });

        foreach (var group in groupedByFile)
        {
            var recordId = DeterministicId("idx_", group.FilePath);
            var recordMetadata = new Dictionary<string, string>
            {
                ["_type"] = "index_record",
                ["file_path"] = group.FilePath,
                ["file_hash"] = "",
                ["last_modified_at"] = "",
                ["indexed_at"] = group.IndexedAt.ToString("O"),
                ["project_path"] = chunksList.First(c => c.FilePath == group.FilePath).ProjectPath
            };

            await _vectorStore.UpsertAsync(collection, recordId, new float[VectorSize], recordMetadata, cancellationToken);
        }
    }

    public async Task DeleteChunksByFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await DeleteByFilterAsync(collection, [("_type", "chunk"), ("file_path", filePath)], cancellationToken);
        await DeleteByFilterAsync(collection, [("_type", "index_record"), ("file_path", filePath)], cancellationToken);
    }

    public async Task DeleteProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var collection = _options.QdrantCollectionName;
        await DeleteByFilterAsync(collection, [("_type", "chunk"), ("project_path", projectPath)], cancellationToken);
        await DeleteByFilterAsync(collection, [("_type", "index_record"), ("project_path", projectPath)], cancellationToken);
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
                Key = "_type",
                Match = new Match { Keywords = new RepeatedStrings { Strings = { "index_record" } } }
            }
        });
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "project_path",
                Match = new Match { Keywords = new RepeatedStrings { Strings = { projectPath } } }
            }
        });

        var points = await _storage.ScrollAllAsync(collection, filter, ct);

        return points.Select(point =>
        {
            var payload = point.Payload;
            return new IndexFileRecord
            {
                FilePath = payload["file_path"].StringValue,
                FileHash = payload["file_hash"].StringValue,
                LastModifiedAt = TryParseDateTime(payload["last_modified_at"].StringValue),
                IndexedAt = TryParseDateTime(payload["indexed_at"].StringValue)
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
