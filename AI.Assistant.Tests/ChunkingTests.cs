using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Chunking;
using AI.Assistant.Infrastructure.Services.Rag.Chunking.Strategies;

namespace AI.Assistant.Tests;

public class ChunkingTests
{
    private const string ProjectPath = @"D:\projects\test-app";
    private static readonly WholeFileChunkStrategy Strategy = new();

    private static CodeFile File(string path, string content, string? language = null)
    {
        var ext = Path.GetExtension(path);
        return new CodeFile
        {
            FilePath = path,
            Content = content,
            Language = language ?? ext switch
            {
                ".cs" => "csharp",
                ".md" => "markdown",
                ".json" => "json",
                ".xaml" => "xaml",
                _ => "text"
            },
            FileHash = "dummyhash",
            Encoding = "utf-8"
        };
    }

    // ============ WholeFileChunkStrategy — 字段验证 ============

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_ReturnsSingleChunk()
    {
        var file = File("Program.cs", "class Program { }");
        var chunks = await Strategy.ChunkAsync(file, ProjectPath).ToListAsync();
        Assert.Single(chunks);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsFilePath()
    {
        var file = File("src/Program.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal("src/Program.cs", chunk.FilePath);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsProjectPath()
    {
        var file = File("Program.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(ProjectPath, chunk.ProjectPath);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsContent()
    {
        var content = "class Foo { }";
        var file = File("test.cs", content);
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(content, chunk.Content);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsLanguage()
    {
        var file = File("test.cs", "code", "csharp");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal("csharp", chunk.Language);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsChunkTypeToFile()
    {
        var file = File("test.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(CodeChunkType.File, chunk.ChunkType);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsStartLineToOne()
    {
        var file = File("test.cs", "line1\nline2\nline3");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(1, chunk.StartLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsEndLineToLineCount()
    {
        var file = File("test.cs", "line1\nline2\nline3");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(3, chunk.EndLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SetsIndexedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var file = File("test.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        var after = DateTime.UtcNow.AddSeconds(1);
        Assert.InRange(chunk.IndexedAt, before, after);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_GeneratesUniqueId()
    {
        var file = File("test.cs", "code");
        var chunk1 = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        var chunk2 = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.NotEqual(chunk1.Id, chunk2.Id);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_MetadataFieldsAreNull()
    {
        var file = File("test.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Null(chunk.Namespace);
        Assert.Null(chunk.ClassName);
        Assert.Null(chunk.MethodName);
        Assert.Null(chunk.SymbolName);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_VectorIdIsEmpty()
    {
        var file = File("test.cs", "code");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Empty(chunk.VectorId);
    }

    // ============ WholeFileChunkStrategy — 边界场景 ============

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_EmptyFile_EndLineIsZero()
    {
        var file = File("empty.cs", "");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Empty(chunk.Content);
        Assert.Equal(1, chunk.StartLine);
        Assert.Equal(0, chunk.EndLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SingleLine()
    {
        var file = File("single.cs", "only one line");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(1, chunk.EndLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_MultiLine()
    {
        var content = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line{i}"));
        var file = File("big.cs", content);
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(100, chunk.EndLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_WithTrailingNewline()
    {
        var file = File("test.cs", "line1\nline2\n");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.Equal(2, chunk.EndLine);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_SupportsAnyExtension()
    {
        var file = File("unknown.xyz", "content");
        var chunk = await Strategy.ChunkAsync(file, ProjectPath).SingleAsync();
        Assert.NotNull(chunk);
        Assert.Equal("unknown.xyz", chunk.FilePath);
    }

    [Fact]
    public async Task WholeFileStrategy_ChunkAsync_RespectsCancellation()
    {
        var file = File("test.cs", "content");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Strategy.ChunkAsync(file, ProjectPath, cts.Token).ToListAsync().AsTask());
    }

    // ============ ChunkManager — 策略路由 ============

    [Fact]
    public async Task ChunkManager_NoExactMatch_UsesFallback()
    {
        var manager = CreateManager();
        var file = File("app.xyz", "random content", "text");
        var chunks = await manager.ChunkAsync(file, ProjectPath).ToListAsync();
        Assert.Single(chunks);
        Assert.Equal("text", chunks[0].Language);
    }

    [Fact]
    public async Task ChunkManager_CsFile_GoesToWholeFileStrategy()
    {
        var manager = CreateManager();
        var file = File("Program.cs", "class P { }");
        var chunks = await manager.ChunkAsync(file, ProjectPath).ToListAsync();
        Assert.Single(chunks);
        Assert.Equal(CodeChunkType.File, chunks[0].ChunkType);
    }

    [Fact]
    public async Task ChunkManager_MdFile_GoesToWholeFileStrategy()
    {
        var manager = CreateManager();
        var file = File("README.md", "# Hello", "markdown");
        var chunks = await manager.ChunkAsync(file, ProjectPath).ToListAsync();
        Assert.Single(chunks);
        Assert.Equal("markdown", chunks[0].Language);
    }

    [Fact]
    public async Task ChunkManager_RespectsCancellation()
    {
        var manager = CreateManager();
        var file = File("test.cs", "content");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.ChunkAsync(file, ProjectPath, cts.Token).ToListAsync().AsTask());
    }

    // ============ ChunkManager — 策略选择 ============

    [Fact]
    public async Task ChunkManager_StrategyWithMatchingExtension_Selected()
    {
        var customStrategy = new MockChunkStrategy([".myext"]);
        var manager = new ChunkManager([Strategy, customStrategy]);

        var file = File("test.myext", "custom content", "myext");
        var chunks = await manager.ChunkAsync(file, ProjectPath).ToListAsync();

        Assert.Single(chunks);
        Assert.Equal("custom_lang", chunks[0].Language);
        Assert.Single(customStrategy.Calls);
    }

    [Fact]
    public async Task ChunkManager_StrategyWithNonMatchingExtension_FallsBack()
    {
        var customStrategy = new MockChunkStrategy([".myext"]);
        var manager = new ChunkManager([Strategy, customStrategy]);

        var file = File("test.other", "content", "text");
        var chunks = await manager.ChunkAsync(file, ProjectPath).ToListAsync();

        Assert.Single(chunks);
        Assert.Empty(customStrategy.Calls);
    }

    // ============ Helper ============

    private static ChunkManager CreateManager()
    {
        return new ChunkManager([Strategy]);
    }

    private sealed class MockChunkStrategy : IChunkStrategy
    {
        public string[] SupportedExtensions { get; }
        public List<(CodeFile, string)> Calls { get; } = [];

        public MockChunkStrategy(string[] extensions)
        {
            SupportedExtensions = extensions;
        }

        public async IAsyncEnumerable<CodeChunk> ChunkAsync(
            CodeFile file,
            string projectPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Calls.Add((file, projectPath));
            cancellationToken.ThrowIfCancellationRequested();
            yield return new CodeChunk
            {
                Id = "mock",
                FilePath = file.FilePath,
                Content = file.Content,
                Language = "custom_lang",
                ChunkType = CodeChunkType.File,
                ProjectPath = projectPath,
                IndexedAt = DateTime.UtcNow
            };
        }
    }
}
