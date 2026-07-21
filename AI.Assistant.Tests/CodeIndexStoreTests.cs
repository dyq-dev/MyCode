using System.Collections.Concurrent;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Storage;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Tests;

public class CodeIndexStoreTests
{
    private const string TestCollection = "test_code_rag";
    private readonly FakeQdrantIndexStorage _qdrant = new();
    private readonly FakeVectorStore _vectorStore;
    private readonly CodeIndexStore _store;
    private readonly RagOptions _options;

    public CodeIndexStoreTests()
    {
        _options = new RagOptions { QdrantCollectionName = TestCollection };
        _vectorStore = new FakeVectorStore(_qdrant);
        _store = new CodeIndexStore(_vectorStore, _qdrant, _options);
    }

    private static CodeChunk Chunk(string filePath, string projectPath = @"D:\test", string content = "code", string language = "csharp")
    {
        return new CodeChunk
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = filePath,
            Content = content,
            Language = language,
            ChunkType = CodeChunkType.File,
            StartLine = 1,
            EndLine = 1,
            Namespace = null,
            ClassName = null,
            MethodName = null,
            SymbolName = null,
            ProjectPath = projectPath,
            IndexedAt = DateTime.UtcNow
        };
    }

    // ============ SaveChunksAsync ============

    [Fact]
    public async Task SaveChunksAsync_SavesAllChunks()
    {
        var chunks = new[] { Chunk("a.cs"), Chunk("b.cs") };

        await _store.SaveChunksAsync(chunks);

        var points = _qdrant.GetByType("chunk");
        Assert.Equal(2, points.Count);
    }

    [Fact]
    public async Task SaveChunksAsync_SetsChunkMetadata()
    {
        var chunk = Chunk("test.cs", @"D:\proj", "class Foo {}", "csharp");
        chunk.StartLine = 1;
        chunk.EndLine = 5;

        await _store.SaveChunksAsync([chunk]);

        var point = _qdrant.GetByType("chunk").Single();
        var p = point.Payload;
        Assert.Equal("test.cs", p["file_path"].StringValue);
        Assert.Equal("class Foo {}", p["content"].StringValue);
        Assert.Equal("csharp", p["language"].StringValue);
        Assert.Equal("File", p["chunk_type"].StringValue);
        Assert.Equal("1", p["start_line"].StringValue);
        Assert.Equal("5", p["end_line"].StringValue);
        Assert.Equal(@"D:\proj", p["project_path"].StringValue);
    }

    [Fact]
    public async Task SaveChunksAsync_CreatesIndexRecordForEachFile()
    {
        var chunks = new[]
        {
            Chunk("shared.cs"),
            Chunk("shared.cs"),
            Chunk("other.cs")
        };

        await _store.SaveChunksAsync(chunks);

        var records = _qdrant.GetByType("index_record");
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Payload["file_path"].StringValue == "shared.cs");
        Assert.Contains(records, r => r.Payload["file_path"].StringValue == "other.cs");
    }

    [Fact]
    public async Task SaveChunksAsync_EmptyChunks_DoesNothing()
    {
        await _store.SaveChunksAsync([]);

        Assert.Empty(_qdrant.GetAll());
    }

    [Fact]
    public async Task SaveChunksAsync_UpdatesExistingIndexRecord()
    {
        var chunk = Chunk("test.cs");

        await _store.SaveChunksAsync([chunk]);
        var firstTs = _qdrant.GetByType("index_record").Single().Payload["indexed_at"].StringValue;

        await Task.Delay(10);

        await _store.SaveChunksAsync([chunk]);
        var secondTs = _qdrant.GetByType("index_record").Single().Payload["indexed_at"].StringValue;

        Assert.NotEqual(firstTs, secondTs);
        Assert.Single(_qdrant.GetByType("index_record"));
    }

    [Fact]
    public async Task SaveChunksAsync_NullMetadataFields_BecomeEmpty()
    {
        var chunk = Chunk("test.cs");
        chunk.Namespace = null;
        chunk.ClassName = null;
        chunk.MethodName = null;
        chunk.SymbolName = null;

        await _store.SaveChunksAsync([chunk]);

        var point = _qdrant.GetByType("chunk").Single();
        Assert.Equal("", point.Payload["namespace"].StringValue);
        Assert.Equal("", point.Payload["class_name"].StringValue);
        Assert.Equal("", point.Payload["method_name"].StringValue);
        Assert.Equal("", point.Payload["symbol_name"].StringValue);
    }

    // ============ DeleteChunksByFileAsync ============

    [Fact]
    public async Task DeleteChunksByFileAsync_RemovesChunksAndIndexRecord()
    {
        await _store.SaveChunksAsync([Chunk("keep.cs"), Chunk("delete.cs")]);

        await _store.DeleteChunksByFileAsync("delete.cs");

        var chunks = _qdrant.GetByType("chunk");
        Assert.Single(chunks);
        Assert.Equal("keep.cs", chunks[0].Payload["file_path"].StringValue);

        var records = _qdrant.GetByType("index_record");
        Assert.Single(records);
    }

    [Fact]
    public async Task DeleteChunksByFileAsync_NonExistentFile_DoesNothing()
    {
        await _store.SaveChunksAsync([Chunk("a.cs")]);
        await _store.DeleteChunksByFileAsync("nonexistent.cs");

        Assert.Single(_qdrant.GetByType("chunk"));
    }

    // ============ DeleteProjectAsync ============

    [Fact]
    public async Task DeleteProjectAsync_RemovesAllProjectData()
    {
        await _store.SaveChunksAsync([
            Chunk("a.cs", @"D:\proj1"),
            Chunk("b.cs", @"D:\proj1"),
            Chunk("c.cs", @"D:\proj2")
        ]);

        await _store.DeleteProjectAsync(@"D:\proj1");

        var chunks = _qdrant.GetByType("chunk");
        Assert.Single(chunks);
        Assert.Equal(@"D:\proj2", chunks[0].Payload["project_path"].StringValue);

        var records = _qdrant.GetByType("index_record");
        Assert.Single(records);
        Assert.Equal(@"D:\proj2", records[0].Payload["project_path"].StringValue);
    }

    [Fact]
    public async Task DeleteProjectAsync_NonExistentProject_DoesNothing()
    {
        await _store.SaveChunksAsync([Chunk("a.cs", @"D:\proj1")]);
        await _store.DeleteProjectAsync(@"D:\nonexistent");

        Assert.Single(_qdrant.GetByType("chunk"));
    }

    // ============ GetIndexedFilesAsync ============

    [Fact]
    public async Task GetIndexedFilesAsync_ReturnsRecordsForProject()
    {
        await _store.SaveChunksAsync([
            Chunk("a.cs", @"D:\proj1"),
            Chunk("b.cs", @"D:\proj1"),
            Chunk("c.cs", @"D:\proj2")
        ]);

        var records = await _store.GetIndexedFilesAsync(@"D:\proj1");

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.FilePath == "a.cs");
        Assert.Contains(records, r => r.FilePath == "b.cs");
    }

    [Fact]
    public async Task GetIndexedFilesAsync_EmptyProject_ReturnsEmpty()
    {
        var records = await _store.GetIndexedFilesAsync(@"D:\empty");
        Assert.Empty(records);
    }

    [Fact]
    public async Task GetIndexedFilesAsync_SetsIndexedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await _store.SaveChunksAsync([Chunk("a.cs", @"D:\proj")]);
        var after = DateTime.UtcNow.AddSeconds(1);

        var records = await _store.GetIndexedFilesAsync(@"D:\proj");
        var record = records.Single();
        Assert.InRange(record.IndexedAt, before, after);
    }

    [Fact]
    public async Task GetIndexedFilesAsync_AfterReIndex_TimestampUpdated()
    {
        var chunk = Chunk("a.cs", @"D:\proj");
        await _store.SaveChunksAsync([chunk]);
        var firstTs = (await _store.GetIndexedFilesAsync(@"D:\proj"))[0].IndexedAt;

        await Task.Delay(10);
        await _store.SaveChunksAsync([chunk]);
        var secondTs = (await _store.GetIndexedFilesAsync(@"D:\proj"))[0].IndexedAt;

        Assert.NotEqual(firstTs, secondTs);
    }

    // ============ Workflow ============

    [Fact]
    public async Task Workflow_FullLifecycle()
    {
        await _store.SaveChunksAsync([
            Chunk("a.cs", @"D:\proj1"),
            Chunk("b.cs", @"D:\proj1")
        ]);
        Assert.Equal(2, (await _store.GetIndexedFilesAsync(@"D:\proj1")).Count);

        await _store.SaveChunksAsync([Chunk("c.cs", @"D:\proj2")]);
        Assert.Single(await _store.GetIndexedFilesAsync(@"D:\proj2"));

        await _store.DeleteChunksByFileAsync("a.cs");
        Assert.Single(await _store.GetIndexedFilesAsync(@"D:\proj1"));
        Assert.Equal("b.cs", (await _store.GetIndexedFilesAsync(@"D:\proj1")).Single().FilePath);

        await _store.DeleteProjectAsync(@"D:\proj1");
        Assert.Empty(await _store.GetIndexedFilesAsync(@"D:\proj1"));
        Assert.Single(await _store.GetIndexedFilesAsync(@"D:\proj2"));
    }

    // ============ Cancellation ============

    [Fact]
    public async Task SaveChunksAsync_RespectsCancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _store.SaveChunksAsync([Chunk("a.cs")], cts.Token));
    }

    // ============ Fake VectorStore ============

    private sealed class FakeVectorStore : IVectorStore
    {
        private readonly FakeQdrantIndexStorage _qdrant;

        public FakeVectorStore(FakeQdrantIndexStorage qdrant) => _qdrant = qdrant;

        public Task UpsertAsync(string collection, string id, float[] vector, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var point = new PointStruct
            {
                Id = new PointId { Uuid = id },
                Vectors = vector
            };
            if (metadata is not null)
            {
                foreach (var kv in metadata)
                    point.Payload.Add(kv.Key, kv.Value);
            }

            _qdrant._store.AddOrUpdate(collection, _ => [point], (_, list) =>
            {
                list.RemoveAll(p => p.Id.Uuid == id);
                list.Add(point);
                return list;
            });
            return Task.CompletedTask;
        }

        public Task<IList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK = 5, Dictionary<string, string>? filter = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IList<VectorSearchResult>>([]);

        public Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            if (_qdrant._store.TryGetValue(collection, out var list))
                list.RemoveAll(p => p.Id.Uuid == id);
            return Task.CompletedTask;
        }
    }
}

// ============ Fake QdrantIndexStorage ============

internal sealed class FakeQdrantIndexStorage : IQdrantIndexStorage
{
    internal readonly ConcurrentDictionary<string, List<PointStruct>> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collections = new(StringComparer.OrdinalIgnoreCase);

    public List<PointStruct> GetAll() =>
        _store.TryGetValue("test_code_rag", out var list) ? [.. list] : [];

    public List<PointStruct> GetByType(string type) =>
        GetAll().Where(p => p.Payload.TryGetValue("_type", out var v) && v.StringValue == type).ToList();

    public Task<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken = default)
        => Task.FromResult(_collections.Contains(collectionName));

    public Task CreateCollectionAsync(string collectionName, VectorParams vectorParams, CancellationToken cancellationToken = default)
    {
        _collections.Add(collectionName);
        return Task.CompletedTask;
    }

    public Task DeleteByFilterAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(collectionName, out var list))
        {
            list.RemoveAll(p => MatchesFilter(p, filter));
        }
        return Task.CompletedTask;
    }

    public Task<IList<RetrievedPoint>> ScrollAllAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default)
    {
        var all = GetAll();
        var filtered = filter is null ? all : all.Where(p => MatchesFilter(p, filter)).ToList();

        var result = filtered.Select(p =>
        {
            var rp = new RetrievedPoint();
            rp.Id = p.Id;
            foreach (var kv in p.Payload)
                rp.Payload.Add(kv.Key, kv.Value);
            return rp;
        }).ToList();

        return Task.FromResult<IList<RetrievedPoint>>(result);
    }

    private static bool MatchesFilter(PointStruct point, Filter? filter)
    {
        if (filter is null) return true;
        foreach (var cond in filter.Must)
        {
            if (!MatchesCondition(point, cond))
                return false;
        }
        return true;
    }

    private static bool MatchesCondition(PointStruct point, Condition cond)
    {
        if (cond.Field is null) return false;
        var key = cond.Field.Key;
        if (!point.Payload.TryGetValue(key, out var value))
            return false;
        if (cond.Field.Match?.Keywords is { } keywords)
            return keywords.Strings.Contains(value.StringValue);
        return false;
    }
}
