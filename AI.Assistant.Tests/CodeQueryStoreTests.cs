using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Storage;

namespace AI.Assistant.Tests;

public class CodeQueryStoreTests
{
    private const string TestCollection = "test_code_rag";
    private readonly FakeVectorStore _vectorStore = new();
    private readonly CodeQueryStore _store;
    private readonly RagOptions _options;

    public CodeQueryStoreTests()
    {
        _options = new RagOptions { QdrantCollectionName = TestCollection };
        _store = new CodeQueryStore(_vectorStore, Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Fact]
    public async Task SearchAsync_ReturnsRetrievedCodeChunks()
    {
        _vectorStore.Results =
        [
            MakeResult("id1", 0.95f, "a.cs", "class A { }"),
            MakeResult("id2", 0.80f, "b.cs", "class B { }")
        ];

        var results = await _store.SearchAsync(new float[512], topK: 5);

        Assert.Equal(2, results.Count);
        var first = Assert.IsType<CodeChunk>(results[0].Chunk);
        var second = Assert.IsType<CodeChunk>(results[1].Chunk);
        Assert.Equal("a.cs", first.FilePath);
        Assert.Equal("class A { }", first.Content);
        Assert.Equal(0.95f, results[0].Score);
        Assert.Equal("b.cs", second.FilePath);
        Assert.Equal(0.80f, results[1].Score);
    }

    [Fact]
    public async Task SearchAsync_FiltersByChunkType()
    {
        _vectorStore.Results =
        [
            MakeResult("id1", 0.9f, "a.cs", "code")
        ];

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
    public async Task SearchAsync_MapsMetadataCorrectly()
    {
        var now = DateTime.UtcNow.ToString("O");
        _vectorStore.Results =
        [
            new VectorSearchResult
            {
                Id = "chunk1",
                Score = 0.9f,
                Metadata = new Dictionary<string, string>
                {
                    [CodeRagSchema.FieldFilePath] = "test.cs",
                    [CodeRagSchema.FieldContent] = "var x = 1;",
                    [CodeRagSchema.FieldLanguage] = "csharp",
                    [CodeRagSchema.FieldChunkType] = "File",
                    [CodeRagSchema.FieldStartLine] = "1",
                    [CodeRagSchema.FieldEndLine] = "5",
                    [CodeRagSchema.FieldProjectPath] = @"D:\proj",
                    [CodeRagSchema.FieldNamespace] = "MyApp",
                    [CodeRagSchema.FieldClassName] = "Program",
                    [CodeRagSchema.FieldMethodName] = "Main",
                    [CodeRagSchema.FieldSymbolName] = "",
                    [CodeRagSchema.FieldIndexedAt] = now
                }
            }
        ];

        var results = await _store.SearchAsync(new float[512], topK: 1);

        var codeChunk = Assert.IsType<CodeChunk>(results[0].Chunk);
        Assert.Equal("chunk1", codeChunk.Id);
        Assert.Equal("test.cs", codeChunk.FilePath);
        Assert.Equal("var x = 1;", codeChunk.Content);
        Assert.Equal("csharp", codeChunk.Language);
        Assert.Equal(CodeChunkType.File, codeChunk.ChunkType);
        Assert.Equal(1, codeChunk.StartLine);
        Assert.Equal(5, codeChunk.EndLine);
        Assert.Equal(@"D:\proj", codeChunk.ProjectPath);
        Assert.Equal("MyApp", codeChunk.Namespace);
        Assert.Equal("Program", codeChunk.ClassName);
        Assert.Equal("Main", codeChunk.MethodName);
    }

    [Fact]
    public async Task SearchAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _store.SearchAsync(new float[512], topK: 5, cts.Token));
    }

    // ============ Fakes ============

    private static VectorSearchResult MakeResult(string id, float score, string filePath, string content)
        => new()
        {
            Id = id,
            Score = score,
            Metadata = new Dictionary<string, string>
            {
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
                [CodeRagSchema.FieldIndexedAt] = DateTime.UtcNow.ToString("O")
            }
        };

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
