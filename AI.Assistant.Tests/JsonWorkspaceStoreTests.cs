using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Workspace;

namespace AI.Assistant.Tests;

public class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;
    private readonly JsonWorkspaceStore _store;

    public JsonWorkspaceStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "test-workspace.json");
        _store = new JsonWorkspaceStore(_tempFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static KnowledgeSource MakeSource(string name, SourceType type, string? id = null)
    {
        return new KnowledgeSource
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            SourceType = type,
            Uri = name,
            IndexStatus = "已索引",
            LastIndexedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrip_ReturnsSameData()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("a.md", SourceType.Document, "id1"),
            MakeSource("b.cs", SourceType.Code, "id2")
        };

        await _store.SaveAsync(sources);
        var loaded = await _store.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("id1", loaded[0].Id);
        Assert.Equal("a.md", loaded[0].Name);
        Assert.Equal(SourceType.Document, loaded[0].SourceType);
        Assert.Equal("b.cs", loaded[1].Name);
        Assert.Equal(SourceType.Code, loaded[1].SourceType);
    }

    [Fact]
    public async Task Load_FileNotExist_ReturnsEmptyList()
    {
        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task Load_CorruptedFile_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(_tempFile, "not valid json");

        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveThenLoad_IndexStatus_IsSetToNotIndexed()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("doc.md", SourceType.Document)
        };
        sources[0].IndexStatus = "已索引";

        await _store.SaveAsync(sources);
        var loaded = await _store.LoadAsync();

        Assert.Equal("未索引", loaded[0].IndexStatus);
    }

    [Fact]
    public async Task SaveThenLoad_LastIndexedAt_IsNull()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("doc.md", SourceType.Document)
        };
        sources[0].LastIndexedAt = DateTime.UtcNow;

        await _store.SaveAsync(sources);
        var loaded = await _store.LoadAsync();

        Assert.Null(loaded[0].LastIndexedAt);
    }

    [Fact]
    public async Task Load_AfterMultipleSaves_ReturnsLatest()
    {
        await _store.SaveAsync([MakeSource("v1.md", SourceType.Document, "x")]);
        await _store.SaveAsync([MakeSource("v2.md", SourceType.Document, "y")]);
        var loaded = await _store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("y", loaded[0].Id);
    }

    [Fact]
    public async Task Save_EmptyList_LoadReturnsEmpty()
    {
        await _store.SaveAsync([]);
        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task Save_FileInNewDirectory_CreatesDirectory()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sub", "dir");
        var nestedFile = Path.Combine(nestedDir, "ws.json");

        try
        {
            var store = new JsonWorkspaceStore(nestedFile);
            await store.SaveAsync([MakeSource("f.md", SourceType.Document)]);

            Assert.True(File.Exists(nestedFile));
            var loaded = await store.LoadAsync();
            Assert.Single(loaded);
        }
        finally
        {
            if (Directory.Exists(nestedDir))
                Directory.Delete(nestedDir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_FileWithEnum_SourceTypeDeserialized()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("f1.md", SourceType.Markdown, "m1"),
            MakeSource("f2.txt", SourceType.Text, "t1"),
            MakeSource("f3.pdf", SourceType.Pdf, "p1")
        };

        await _store.SaveAsync(sources);
        var loaded = await _store.LoadAsync();

        Assert.Equal(SourceType.Markdown, loaded[0].SourceType);
        Assert.Equal(SourceType.Text, loaded[1].SourceType);
        Assert.Equal(SourceType.Pdf, loaded[2].SourceType);
    }

    [Fact]
    public async Task Save_IndexStatusAndLastIndexedAt_NotSerialized()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("test.md", SourceType.Document, "s1")
        };
        sources[0].IndexStatus = "自定义状态";
        sources[0].LastIndexedAt = DateTime.Parse("2026-01-01T00:00:00Z");

        await _store.SaveAsync(sources);

        var json = await File.ReadAllTextAsync(_tempFile);
        Assert.DoesNotContain("IndexStatus", json);
        Assert.DoesNotContain("LastIndexedAt", json);
    }

    [Fact]
    public async Task SourceType_SerializedAsString()
    {
        var sources = new List<KnowledgeSource>
        {
            MakeSource("doc.md", SourceType.Document, "d1")
        };

        await _store.SaveAsync(sources);

        var json = await File.ReadAllTextAsync(_tempFile);
        Assert.Contains("\"Document\"", json);
    }
}
