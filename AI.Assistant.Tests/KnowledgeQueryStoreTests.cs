using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Retrieval;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Tests;

public class KnowledgeQueryStoreTests
{
    private const string TestCollection = "test_code_rag";
    private readonly FakeVectorStore _vectorStore = new();
    private readonly KnowledgeQueryStore _store;
    private readonly RagOptions _options;

    public KnowledgeQueryStoreTests()
    {
        _options = new RagOptions { QdrantCollectionName = TestCollection };
        _store = new KnowledgeQueryStore(_vectorStore, Options.Create(_options));
    }

    [Fact]
    public async Task SearchAsync_CodeResult_ReturnsCodeChunk()
    {
        _vectorStore.Results =
        [
            MakeCodeResult("id1", 0.95f, "test.cs", "class A { }")
        ];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Single(results);
        Assert.Equal(0.95f, results[0].Score);
        Assert.IsType<CodeChunk>(results[0].Chunk);
        var code = (CodeChunk)results[0].Chunk;
        Assert.Equal("test.cs", code.FilePath);
        Assert.Equal("class A { }", code.Content);
    }

    [Fact]
    public async Task SearchAsync_DocumentResult_ReturnsKnowledgeChunk()
    {
        _vectorStore.Results =
        [
            MakeDocResult("doc1", 0.88f, "readme.md", "# Installation", "doc-001")
        ];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Single(results);
        Assert.Equal(0.88f, results[0].Score);
        Assert.IsType<KnowledgeChunk>(results[0].Chunk);
        var doc = (KnowledgeChunk)results[0].Chunk;
        Assert.Equal("doc-001", doc.SourceId);
        Assert.Equal("readme.md", doc.SourceUri);
        Assert.Equal("# Installation", doc.Content);
        Assert.Equal(SourceType.Document, doc.SourceType);
    }

    [Fact]
    public async Task SearchAsync_MixedResults_ReturnsBothTypes()
    {
        _vectorStore.Results =
        [
            MakeCodeResult("c1", 0.95f, "a.cs", "code"),
            MakeDocResult("d1", 0.80f, "doc.md", "document text", "src1")
        ];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Equal(2, results.Count);
        Assert.IsType<CodeChunk>(results[0].Chunk);
        Assert.IsType<KnowledgeChunk>(results[1].Chunk);
    }

    [Fact]
    public async Task SearchAsync_UnknownSourceType_FallsBackToKnowledgeChunk()
    {
        var metadata = new Dictionary<string, string>
        {
            [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
            [CodeRagSchema.FieldSourceType] = "unknown",
            [CodeRagSchema.FieldSourceId] = "s1",
            [CodeRagSchema.FieldSourceUri] = "unknown.md",
            [CodeRagSchema.FieldContent] = "some content",
            [CodeRagSchema.FieldProjectPath] = @"D:\test",
            [CodeRagSchema.FieldIndexedAt] = DateTime.UtcNow.ToString("O")
        };

        _vectorStore.Results =
        [
            new VectorSearchResult { Id = "u1", Score = 0.7f, Metadata = metadata }
        ];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Single(results);
        Assert.IsType<KnowledgeChunk>(results[0].Chunk);
        Assert.Equal(SourceType.Unknown, ((KnowledgeChunk)results[0].Chunk).SourceType);
    }

    [Fact]
    public async Task SearchAsync_FiltersByChunkType()
    {
        _vectorStore.Results = [];

        await _store.SearchAsync(new float[512], topK: 5);

        var capturedFilter = _vectorStore.LastFilter;
        Assert.NotNull(capturedFilter);
        Assert.True(capturedFilter.ContainsKey(CodeRagSchema.FieldType));
        Assert.Equal(CodeRagSchema.TypeChunk, capturedFilter[CodeRagSchema.FieldType]);
    }

    [Fact]
    public async Task SearchAsync_PassesTopK()
    {
        _vectorStore.Results = [];

        await _store.SearchAsync(new float[512], topK: 3);

        Assert.Equal(3, _vectorStore.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_EmptyResults_ReturnsEmptyList()
    {
        _vectorStore.Results = [];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_DocumentMetadata_AllFieldsMapped()
    {
        var now = DateTime.UtcNow.ToString("O");
        var metadata = new Dictionary<string, string>
        {
            [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
            [CodeRagSchema.FieldSourceType] = CodeRagSchema.SourceTypeDocument,
            [CodeRagSchema.FieldSourceId] = "doc-007",
            [CodeRagSchema.FieldSourceUri] = @"D:\docs\guide.md",
            [CodeRagSchema.FieldContent] = "# Introduction\nThis is a guide",
            [CodeRagSchema.FieldProjectPath] = @"D:\docs",
            [CodeRagSchema.FieldIndexedAt] = now,
            ["heading"] = "Introduction",
            ["heading_level"] = "1",
            ["heading_path"] = "Introduction"
        };

        _vectorStore.Results =
        [
            new VectorSearchResult { Id = "chunk1", Score = 0.9f, Metadata = metadata }
        ];

        var results = await _store.SearchAsync(new float[512], topK: 1);

        var chunk = (KnowledgeChunk)results[0].Chunk;
        Assert.Equal("chunk1", chunk.Id);
        Assert.Equal("doc-007", chunk.SourceId);
        Assert.Equal(@"D:\docs\guide.md", chunk.SourceUri);
        Assert.Equal("# Introduction\nThis is a guide", chunk.Content);
        Assert.Equal(SourceType.Document, chunk.SourceType);
        Assert.Equal(@"D:\docs", chunk.ProjectPath);
        Assert.Equal("Introduction", chunk.Metadata["heading"]);
        Assert.Equal("1", chunk.Metadata["heading_level"]);
    }

    [Fact]
    public async Task SearchAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _store.SearchAsync(new float[512], topK: 5, cts.Token));
    }

    // ============ Helpers ============

    private static VectorSearchResult MakeCodeResult(string id, float score, string filePath, string content)
    {
        var now = DateTime.UtcNow.ToString("O");
        return new VectorSearchResult
        {
            Id = id,
            Score = score,
            Metadata = new Dictionary<string, string>
            {
                [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
                [CodeRagSchema.FieldSourceType] = CodeRagSchema.SourceTypeCode,
                [CodeRagSchema.FieldSourceId] = "src_code",
                [CodeRagSchema.FieldSourceUri] = filePath,
                [CodeRagSchema.FieldFilePath] = filePath,
                [CodeRagSchema.FieldContent] = content,
                [CodeRagSchema.FieldLanguage] = "csharp",
                [CodeRagSchema.FieldChunkType] = "File",
                [CodeRagSchema.FieldStartLine] = "1",
                [CodeRagSchema.FieldEndLine] = "1",
                [CodeRagSchema.FieldProjectPath] = @"D:\test",
                [CodeRagSchema.FieldNamespace] = "",
                [CodeRagSchema.FieldClassName] = "",
                [CodeRagSchema.FieldMethodName] = "",
                [CodeRagSchema.FieldSymbolName] = "",
                [CodeRagSchema.FieldIndexedAt] = now
            }
        };
    }

    private static VectorSearchResult MakeDocResult(string id, float score, string uri, string content, string sourceId)
    {
        var now = DateTime.UtcNow.ToString("O");
        return new VectorSearchResult
        {
            Id = id,
            Score = score,
            Metadata = new Dictionary<string, string>
            {
                [CodeRagSchema.FieldType] = CodeRagSchema.TypeChunk,
                [CodeRagSchema.FieldSourceType] = CodeRagSchema.SourceTypeDocument,
                [CodeRagSchema.FieldSourceId] = sourceId,
                [CodeRagSchema.FieldSourceUri] = uri,
                [CodeRagSchema.FieldContent] = content,
                [CodeRagSchema.FieldProjectPath] = @"D:\docs",
                [CodeRagSchema.FieldIndexedAt] = now
            }
        };
    }

    // ============ Fake ============

    private sealed class FakeVectorStore : IVectorStore
    {
        public IList<VectorSearchResult> Results { get; set; } = [];
        public Dictionary<string, string>? LastFilter { get; private set; }
        public int LastTopK { get; private set; }

        public Task UpsertAsync(string collection, string id, float[] vector, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK = 5, Dictionary<string, string>? filter = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastFilter = filter;
            LastTopK = topK;
            return Task.FromResult(Results);
        }

        public Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
