using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Retrieval;

namespace AI.Assistant.Tests;

public class CodeRetrieverTests
{
    private readonly FakeEmbeddingService _embedding = new();
    private readonly FakeQueryStore _queryStore = new();
    private readonly RagOptions _options = new();
    private readonly CodeRetriever _retriever;

    public CodeRetrieverTests()
    {
        _retriever = new CodeRetriever(_embedding, _queryStore, Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Fact]
    public async Task VectorSearchAsync_EmbedsQueryAndSearches()
    {
        _queryStore.Results =
        [
            new RetrievedKnowledgeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "code", Language = "csharp" },
                Score = 0.9f
            }
        ];

        var results = await _retriever.VectorSearchAsync("how to DI", topK: 5);

        Assert.Single(results);
        var codeChunk = Assert.IsType<CodeChunk>(results[0].Chunk);
        Assert.Equal("a.cs", codeChunk.FilePath);
        Assert.Equal("how to DI", _embedding.LastQuery);
    }

    [Fact]
    public async Task VectorSearchAsync_PassesVectorToQueryStore()
    {
        _queryStore.Results = [];

        await _retriever.VectorSearchAsync("test query", topK: 5);

        Assert.NotNull(_queryStore.LastVector);
        Assert.Equal(512, _queryStore.LastVector.Length);
        Assert.Equal(5, _queryStore.LastTopK);
    }

    [Fact]
    public async Task VectorSearchAsync_ClampsTopKToMaxTopK()
    {
        _options.MaxTopK = 10;
        _queryStore.Results = [];

        await _retriever.VectorSearchAsync("query", topK: 100);

        Assert.Equal(10, _queryStore.LastTopK);
    }

    [Fact]
    public async Task VectorSearchAsync_DefaultTopKUsesFive()
    {
        _queryStore.Results = [];

        await _retriever.VectorSearchAsync("query");

        Assert.Equal(5, _queryStore.LastTopK);
    }

    [Fact]
    public async Task VectorSearchAsync_ReturnsEmptyList_WhenNoResults()
    {
        _queryStore.Results = [];

        var results = await _retriever.VectorSearchAsync("query");

        Assert.Empty(results);
    }

    [Fact]
    public async Task VectorSearchAsync_PreservesMultipleResults()
    {
        _queryStore.Results =
        [
            new RetrievedKnowledgeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "class A" },
                Score = 0.9f
            },
            new RetrievedKnowledgeChunk
            {
                Chunk = new CodeChunk { FilePath = "b.cs", Content = "class B" },
                Score = 0.7f
            }
        ];

        var results = await _retriever.VectorSearchAsync("query", topK: 10);

        Assert.Equal(2, results.Count);
        var first = Assert.IsType<CodeChunk>(results[0].Chunk);
        var second = Assert.IsType<CodeChunk>(results[1].Chunk);
        Assert.Equal("a.cs", first.FilePath);
        Assert.Equal("b.cs", second.FilePath);
    }

    [Fact]
    public async Task VectorSearchAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _retriever.VectorSearchAsync("query", cancellationToken: cts.Token));
    }

    // ============ Fakes ============

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public string? LastQuery { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastQuery = text;
            return Task.FromResult(new float[512]);
        }

        public Task<IList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
            => Task.FromResult<IList<float[]>>(texts.Select(_ => new float[512]).ToList());
    }

    private sealed class FakeQueryStore : IQueryStore
    {
        public IList<RetrievedKnowledgeChunk> Results { get; set; } = [];
        public float[]? LastVector { get; private set; }
        public int LastTopK { get; private set; }

        public Task<IList<RetrievedKnowledgeChunk>> SearchAsync(float[] queryVector, int topK = 5, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastVector = queryVector;
            LastTopK = topK;
            return Task.FromResult(Results);
        }
    }
}
