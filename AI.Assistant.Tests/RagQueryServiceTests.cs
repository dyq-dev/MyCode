using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Tests;

public class RagQueryServiceTests
{
    private readonly FakeCodeRetriever _retriever = new();
    private readonly FakeRagContextBuilder _contextBuilder = new();
    private readonly RagOptions _options = new();
    private readonly RagQueryService _service;

    public RagQueryServiceTests()
    {
        _service = CreateService();
    }

    private RagQueryService CreateService(Action<RagOptions>? configure = null)
    {
        configure?.Invoke(_options);
        return new RagQueryService(
            _retriever,
            _contextBuilder,
            Options.Create(_options),
            NullLogger<RagQueryService>.Instance);
    }

    // ============ 关键词匹配 ============

    [Fact]
    public async Task QueryAsync_NormalMessage_NoRag()
    {
        var result = await _service.QueryAsync("你好，今天天气不错");

        Assert.False(result.HasContext);
        Assert.Equal(0, result.ChunksUsed);
        Assert.Null(result.ContextText);
    }

    [Fact]
    public async Task QueryAsync_ChineseKeyword_TriggersRag()
    {
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "class A", Language = "csharp" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "RAG CONTEXT";

        var result = await _service.QueryAsync("这个接口怎么实现");

        Assert.True(result.HasContext);
        Assert.Equal("RAG CONTEXT", result.ContextText);
        Assert.Equal(1, result.ChunksUsed);
    }

    [Fact]
    public async Task QueryAsync_EnglishKeyword_TriggersRag()
    {
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "interface IFoo", Language = "csharp" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "ENGLISH CTX";

        var result = await _service.QueryAsync("how to use this interface");

        Assert.True(result.HasContext);
        Assert.Equal("ENGLISH CTX", result.ContextText);
    }

    [Fact]
    public async Task QueryAsync_ArchitectureKeyword_TriggersRag()
    {
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "arch.cs", Content = "architecture" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "ARCH CTX";

        var result = await _service.QueryAsync("项目中使用了哪些设计模式");

        Assert.True(result.HasContext);
        Assert.Equal("ARCH CTX", result.ContextText);
    }

    [Fact]
    public async Task QueryAsync_PipelineKeyword_TriggersRag()
    {
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "pipeline.cs", Content = "RAG pipeline" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "PIPELINE CTX";

        var result = await _service.QueryAsync("RAG链路经过哪些组件");

        Assert.True(result.HasContext);
        Assert.Equal("PIPELINE CTX", result.ContextText);
    }

    [Fact]
    public async Task QueryAsync_DependencyKeyword_TriggersRag()
    {
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "dep.cs", Content = "dependency chain" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "DEP CTX";

        var result = await _service.QueryAsync("MemoryService依赖链");

        Assert.True(result.HasContext);
        Assert.Equal("DEP CTX", result.ContextText);
    }

    // ============ 空结果 ============

    [Fact]
    public async Task QueryAsync_KeywordButNoResults_ReturnsNoContext()
    {
        _retriever.Results = [];

        var result = await _service.QueryAsync("这个类在哪里");

        Assert.False(result.HasContext);
        Assert.Equal(0, result.ChunksUsed);
    }

    // ============ 异常处理 ============

    [Fact]
    public async Task QueryAsync_RetrievalError_ReturnsNoContextWithError()
    {
        _retriever.Exception = new InvalidOperationException("Qdrant 连接失败");

        var result = await _service.QueryAsync("查找 UserService 类");

        Assert.False(result.HasContext);
        Assert.Contains("Qdrant", result.ErrorMessage);
    }

    [Fact]
    public async Task QueryAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _retriever.Exception = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.QueryAsync("查找 UserService 类", cts.Token));
    }

    // ============ EnableDebugInfo ============

    [Fact]
    public async Task QueryAsync_DebugInfoEnabled_WhenTriggered_PopulatesDebugInfo()
    {
        var service = CreateService(o => o.EnableDebugInfo = true);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk
                {
                    FilePath = "a.cs", Content = "class A", Language = "csharp",
                    StartLine = 1, EndLine = 10, ChunkType = CodeChunkType.File
                },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "CTX";

        var result = await service.QueryAsync("这个接口怎么实现");

        Assert.NotNull(result.DebugInfo);
        Assert.True(result.DebugInfo.Triggered);
        Assert.Equal("这个接口怎么实现", result.DebugInfo.UserQuery);
        Assert.Equal("接口", result.DebugInfo.MatchedKeyword);
        Assert.Equal(1, result.DebugInfo.RawChunksReturned);
        Assert.Equal(1, result.DebugInfo.ChunksAfterFilter);
        Assert.Equal(1, result.DebugInfo.ChunksUsedByBuilder);
        Assert.Single(result.DebugInfo.Chunks);
        Assert.Equal("a.cs", result.DebugInfo.Chunks[0].FilePath);
        Assert.Equal(1, result.DebugInfo.Chunks[0].StartLine);
        Assert.Equal(10, result.DebugInfo.Chunks[0].EndLine);
        Assert.Equal(0.9f, result.DebugInfo.Chunks[0].Score);
        Assert.Equal("csharp", result.DebugInfo.Chunks[0].Language);
        Assert.Equal("File", result.DebugInfo.Chunks[0].ChunkType);
    }

    [Fact]
    public async Task QueryAsync_DebugInfoDisabled_ReturnsNullDebugInfo()
    {
        var service = CreateService(o => o.EnableDebugInfo = false);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "class A" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "CTX";

        var result = await service.QueryAsync("这个类在哪里");

        Assert.NotNull(result.ContextText);
        Assert.Null(result.DebugInfo);
    }

    [Fact]
    public async Task QueryAsync_DebugInfoEnabled_NotTriggered_ShowsSkipped()
    {
        var service = CreateService(o => o.EnableDebugInfo = true);

        var result = await service.QueryAsync("你好");

        Assert.NotNull(result.DebugInfo);
        Assert.False(result.DebugInfo.Triggered);
        Assert.Equal("你好", result.DebugInfo.UserQuery);
    }

    // ============ MinimumScoreThreshold ============

    [Fact]
    public async Task QueryAsync_Threshold_FiltersLowScoreChunks()
    {
        var service = CreateService(o => o.MinimumScoreThreshold = 0.5);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "best match" },
                Score = 0.9f
            },
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "b.cs", Content = "poor match" },
                Score = 0.3f
            }
        ];
        _contextBuilder.ContextText = "CTX";
        _contextBuilder.TotalUsed = 1;

        var result = await service.QueryAsync("这个类在哪里");

        Assert.True(result.HasContext);
        Assert.Equal(1, result.ChunksUsed);
    }

    [Fact]
    public async Task QueryAsync_Threshold_AllFiltered_ReturnsNoContext()
    {
        var service = CreateService(o => o.MinimumScoreThreshold = 0.8);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "code" },
                Score = 0.3f
            }
        ];

        var result = await service.QueryAsync("这个类在哪里");

        Assert.False(result.HasContext);
        Assert.Equal(0, result.ChunksUsed);
    }

    [Fact]
    public async Task QueryAsync_ThresholdZero_NoFilterApplied()
    {
        var service = CreateService(o => o.MinimumScoreThreshold = 0.0);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "code" },
                Score = 0.01f
            }
        ];
        _contextBuilder.ContextText = "CTX";

        var result = await service.QueryAsync("这个类在哪里");

        Assert.True(result.HasContext);
        Assert.Equal(1, result.ChunksUsed);
    }

    [Fact]
    public async Task QueryAsync_ThresholdWithDebugInfo_TracksCounts()
    {
        var service = CreateService(o =>
        {
            o.EnableDebugInfo = true;
            o.MinimumScoreThreshold = 0.6;
        });
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "high" },
                Score = 0.9f
            },
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "b.cs", Content = "low" },
                Score = 0.3f
            }
        ];
        _contextBuilder.ContextText = "CTX";

        var result = await service.QueryAsync("这个类在哪里");

        Assert.NotNull(result.DebugInfo);
        Assert.Equal(2, result.DebugInfo.RawChunksReturned);
        Assert.Equal(1, result.DebugInfo.ChunksAfterFilter);
        // DebugInfo Chunks 包含的是原始列表（便于对比）
        Assert.Equal(2, result.DebugInfo.Chunks.Count);
    }

    // ============ Timing ============

    [Fact]
    public async Task QueryAsync_DebugInfoEnabled_IncludesTiming()
    {
        var service = CreateService(o => o.EnableDebugInfo = true);
        _retriever.Results =
        [
            new RetrievedCodeChunk
            {
                Chunk = new CodeChunk { FilePath = "a.cs", Content = "code" },
                Score = 0.9f
            }
        ];
        _contextBuilder.ContextText = "CTX";

        var result = await service.QueryAsync("这个接口怎么实现");

        Assert.NotNull(result.DebugInfo);
        Assert.True(result.DebugInfo.RetrievalElapsed.TotalMilliseconds >= 0);
        Assert.True(result.DebugInfo.ContextBuildElapsed.TotalMilliseconds >= 0);
    }

    // ============ Fakes ============

    private sealed class FakeCodeRetriever : ICodeRetriever
    {
        public IList<RetrievedCodeChunk> Results { get; set; } = [];
        public Exception? Exception { get; set; }

        public Task<IList<RetrievedCodeChunk>> VectorSearchAsync(
            string query, int topK = 5, CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(Results);
        }
    }

    private sealed class FakeRagContextBuilder : IRagContextBuilder
    {
        public string ContextText { get; set; } = "";
        public int TotalUsed { get; set; }
        public int EstimatedTokens { get; set; }

        public Task<RagContext> BuildAsync(IList<RetrievedCodeChunk> chunks, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RagContext
            {
                ContextText = ContextText,
                TotalRetrieved = chunks.Count,
                TotalUsed = TotalUsed > 0 ? TotalUsed : chunks.Count,
                EstimatedTokens = EstimatedTokens > 0 ? EstimatedTokens : ContextText.Length / 3
            });
        }
    }
}
