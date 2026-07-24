using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Context;

namespace AI.Assistant.Tests;

public class RagContextBuilderTests
{
    // ============ 空输入 ============

    [Fact]
    public async Task BuildAsync_EmptyChunks_ReturnsEmptyContext()
    {
        var builder = MakeBuilder();

        var ctx = await builder.BuildAsync([]);

        Assert.Equal("", ctx.ContextText);
        Assert.Empty(ctx.Sources);
        Assert.Equal(0, ctx.EstimatedTokens);
        Assert.Equal(0, ctx.TotalRetrieved);
        Assert.Equal(0, ctx.TotalUsed);
    }

    // ============ 基本拼接 ============

    [Fact]
    public async Task BuildAsync_SingleChunk_FormatsCorrectly()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.GroupByFile = false;
        });

        var chunks = new[] { Chunk("a.cs", "class A { }", score: 0.9f) };
        var ctx = await builder.BuildAsync(chunks);

        Assert.Contains("[文件: a.cs (第 1-10 行)]", ctx.ContextText);
        Assert.Contains("```csharp", ctx.ContextText);
        Assert.Contains("class A { }", ctx.ContextText);
        Assert.Contains("```", ctx.ContextText);
        Assert.Single(ctx.Sources);
        Assert.Equal(1, ctx.TotalUsed);
        Assert.Equal(1, ctx.TotalRetrieved);
    }

    [Fact]
    public async Task BuildAsync_MultipleChunks_AllIncluded()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.GroupByFile = false;
            o.MaxChunks = 10;
            o.MaxContextTokens = 10000;
        });

        var chunks = new[]
        {
            Chunk("a.cs", "code a", score: 0.9f),
            Chunk("b.cs", "code b", score: 0.8f),
            Chunk("c.cs", "code c", score: 0.7f)
        };

        var ctx = await builder.BuildAsync(chunks);

        Assert.Contains("code a", ctx.ContextText);
        Assert.Contains("code b", ctx.ContextText);
        Assert.Contains("code c", ctx.ContextText);
        Assert.Equal(3, ctx.TotalUsed);
    }

    // ============ MaxChunks 截断 ============

    [Fact]
    public async Task BuildAsync_MaxChunksLimit_TruncatesCount()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 2;
            o.MaxChunksPerFile = 10;
            o.MaxContextTokens = 10000;
            o.GroupByFile = false;
        });

        var chunks = new[]
        {
            Chunk("a.cs", "x", score: 0.9f),
            Chunk("b.cs", "x", score: 0.8f),
            Chunk("c.cs", "x", score: 0.7f)
        };

        var ctx = await builder.BuildAsync(chunks);

        Assert.Equal(2, ctx.TotalUsed);
        Assert.Contains("a.cs", ctx.ContextText);
        Assert.Contains("b.cs", ctx.ContextText);
        Assert.DoesNotContain("c.cs", ctx.ContextText);
    }

    // ============ MaxContextTokens 截断 ============

    [Fact]
    public async Task BuildAsync_TokenLimit_TruncatesBeforeLimit()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxContextTokens = 10;
            o.GroupByFile = false;
        });

        var chunks = new[]
        {
            Chunk("a.cs", "hello world hello world", score: 0.9f),
            Chunk("b.cs", "more content here", score: 0.8f)
        };

        var ctx = await builder.BuildAsync(chunks);

        // 第一块的 segment token 已超过 10，但因为至少留一块，所以 a.cs 还在
        Assert.Contains("a.cs", ctx.ContextText);
        Assert.DoesNotContain("b.cs", ctx.ContextText);
        Assert.Equal(1, ctx.TotalUsed);
    }

    // ============ 至少保留一块 ============

    [Fact]
    public async Task BuildAsync_TokenLimit_IncludesAtLeastOneChunk()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxContextTokens = 1;
            o.GroupByFile = false;
        });

        var chunks = new[]
        {
            Chunk("a.cs", "long content here that definitely exceeds one token", score: 0.9f),
            Chunk("b.cs", "x", score: 0.8f)
        };

        var ctx = await builder.BuildAsync(chunks);

        Assert.Contains("a.cs", ctx.ContextText);
        Assert.DoesNotContain("b.cs", ctx.ContextText);
        Assert.Equal(1, ctx.TotalUsed);
    }

    // ============ MaxChunksPerFile ============

    [Fact]
    public async Task BuildAsync_MaxChunksPerFile_LimitsPerFile()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxChunksPerFile = 2;
            o.MaxContextTokens = 10000;
            o.GroupByFile = false;
        });

        var chunks = new[]
        {
            Chunk("same.cs", "1", score: 0.9f),
            Chunk("same.cs", "2", score: 0.8f),
            Chunk("same.cs", "3", score: 0.7f),
            Chunk("other.cs", "4", score: 0.6f)
        };

        var ctx = await builder.BuildAsync(chunks);

        // same.cs 最多保留 2 条（score 最高的 0.9 和 0.8）
        // 所以总共 3 条：same.cs x2 + other.cs x1
        Assert.Equal(3, ctx.TotalUsed);
        Assert.Contains("1", ctx.ContextText);
        Assert.Contains("2", ctx.ContextText);
        Assert.DoesNotContain("3", ctx.ContextText);
        Assert.Contains("4", ctx.ContextText);
    }

    // ============ 按文件分组 ============

    [Fact]
    public async Task BuildAsync_GroupByFile_GroupsCorrectly()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxContextTokens = 10000;
            o.GroupByFile = true;
        });

        var chunks = new[]
        {
            Chunk("b.cs", "bbb", score: 0.7f, startLine: 30, endLine: 35),
            Chunk("a.cs", "aaa1", score: 0.9f, startLine: 10, endLine: 15),
            Chunk("a.cs", "aaa2", score: 0.85f, startLine: 1, endLine: 5)
        };

        var ctx = await builder.BuildAsync(chunks);

        // a.cs（最高分 0.9）应排在 b.cs（0.7）之前
        var aIdx = ctx.ContextText.IndexOf("[文件: a.cs", StringComparison.Ordinal);
        var bIdx = ctx.ContextText.IndexOf("[文件: b.cs", StringComparison.Ordinal);
        Assert.True(aIdx >= 0);
        Assert.True(bIdx >= 0);
        Assert.True(aIdx < bIdx, "a.cs (score 0.9) should appear before b.cs (score 0.7)");

        // a.cs 内部按 StartLine 排序：先 1-5，再 10-15
        var line1Idx = ctx.ContextText.IndexOf("第 1-5 行", StringComparison.Ordinal);
        var line2Idx = ctx.ContextText.IndexOf("第 10-15 行", StringComparison.Ordinal);
        Assert.True(line1Idx < line2Idx, $"Expected line 1-5 before line 10-15, but l1={line1Idx}, l2={line2Idx}\n{ctx.ContextText}");
    }

    // ============ 平铺模式 ============

    [Fact]
    public async Task BuildAsync_FlatMode_NoGrouping()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxContextTokens = 10000;
            o.GroupByFile = false;
        });

        var chunks = new[]
        {
            Chunk("a.cs", "aaa", score: 0.8f),
            Chunk("b.cs", "bbb", score: 0.9f)
        };

        var ctx = await builder.BuildAsync(chunks);

        // 平铺模式不分组，按 Score 降序：b.cs (0.9) 先，a.cs (0.8) 后
        var bIdx = ctx.ContextText.IndexOf("b.cs");
        var aIdx = ctx.ContextText.IndexOf("a.cs");
        Assert.True(bIdx < aIdx);
    }

    // ============ EstimatedTokens ============

    [Fact]
    public async Task BuildAsync_EstimatedTokens_ReflectsTextLength()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.MaxChunks = 10;
            o.MaxContextTokens = 10000;
            o.GroupByFile = false;
        });

        var content = "class A { void M() { } }";
        var chunks = new[] { Chunk("a.cs", content, score: 0.9f) };
        var ctx = await builder.BuildAsync(chunks);

        // EstimatedTokens ≈ ContextText.Length / 3
        var expectedTokenCount = ctx.ContextText.Length / 3;
        Assert.Equal(expectedTokenCount, ctx.EstimatedTokens);
    }

    // ============ Prefix ============

    [Fact]
    public async Task BuildAsync_Prefix_AddedWhenConfigured()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "CUSTOM PREFIX";
            o.MaxChunks = 1;
            o.MaxContextTokens = 10000;
            o.GroupByFile = false;
        });

        var chunks = new[] { Chunk("a.cs", "code", score: 0.9f) };
        var ctx = await builder.BuildAsync(chunks);

        Assert.StartsWith("CUSTOM PREFIX", ctx.ContextText.Trim());
    }

    [Fact]
    public async Task BuildAsync_EmptyPrefix_OmitsHeader()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.GroupByFile = false;
        });

        var chunks = new[] { Chunk("a.cs", "code", score: 0.9f) };
        var ctx = await builder.BuildAsync(chunks);

        Assert.DoesNotContain("以下", ctx.ContextText);
        Assert.DoesNotContain("---", ctx.ContextText);
    }

    // ============ ShowLineNumbers ============

    [Fact]
    public async Task BuildAsync_ShowLineNumbersFalse_OmitsLineRange()
    {
        var builder = MakeBuilder(o =>
        {
            o.Prefix = "";
            o.ShowLineNumbers = false;
            o.GroupByFile = false;
        });

        var chunks = new[] { Chunk("a.cs", "code", score: 0.9f) };
        var ctx = await builder.BuildAsync(chunks);

        Assert.DoesNotContain("第", ctx.ContextText);
        Assert.Contains("[文件: a.cs]", ctx.ContextText);
    }

    // ============ 帮助方法 ============

    private static RagContextBuilder MakeBuilder(Action<RagContextOptions>? configure = null)
    {
        var options = new RagContextOptions();
        configure?.Invoke(options);
        return new RagContextBuilder(options);
    }

    private static RetrievedCodeChunk Chunk(
        string filePath,
        string content,
        float score = 1.0f,
        int startLine = 1,
        int endLine = 10,
        string language = "csharp")
    {
        return new RetrievedCodeChunk
        {
            Chunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = filePath,
                Content = content,
                Language = language,
                ChunkType = CodeChunkType.File,
                StartLine = startLine,
                EndLine = endLine,
                ProjectPath = @"D:\test",
                IndexedAt = DateTime.UtcNow
            },
            Score = score
        };
    }
}
