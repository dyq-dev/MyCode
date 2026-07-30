using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Workspace;

namespace AI.Assistant.Tests;

public class WorkspaceManagerTests : IDisposable
{
    private readonly WorkspaceManager _manager;
    private static readonly string WorkspaceDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI.Assistant");

    public WorkspaceManagerTests()
    {
        // 每个测试开始前清空 workspace 文件，避免状态串扰
        if (Directory.Exists(WorkspaceDir))
        {
            foreach (var f in Directory.GetFiles(WorkspaceDir, "workspace.json"))
                File.Delete(f);
        }
        _manager = new WorkspaceManager();
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void AddSource_IncreasesCount()
    {
        var source = MakeSource("test.md", SourceType.Document);

        _manager.AddSource(source);

        var sources = _manager.GetSources();
        Assert.Single(sources);
    }

    [Fact]
    public void AddSource_SourceHasGeneratedId()
    {
        var source = MakeSource("file.md", SourceType.Document);

        _manager.AddSource(source);

        Assert.NotNull(source.Id);
        Assert.NotEmpty(source.Id);
    }

    [Fact]
    public void AddSource_MultipleSources_AllReturned()
    {
        _manager.AddSource(MakeSource("a.md", SourceType.Document, "id1"));
        _manager.AddSource(MakeSource("b.md", SourceType.Document, "id2"));

        Assert.Equal(2, _manager.GetSources().Count);
    }

    [Fact]
    public void RemoveSource_Existing_Removes()
    {
        _manager.AddSource(MakeSource("test.md", SourceType.Document, "to-remove"));

        _manager.RemoveSource("to-remove");

        Assert.Empty(_manager.GetSources());
        Assert.Null(_manager.GetSource("to-remove"));
    }

    [Fact]
    public void RemoveSource_NonExisting_NoThrow()
    {
        _manager.RemoveSource("non-existing");

        Assert.Empty(_manager.GetSources());
    }

    [Fact]
    public void RemoveSource_DoesNotAffectOtherSources()
    {
        _manager.AddSource(MakeSource("keep.md", SourceType.Document, "keep"));
        _manager.AddSource(MakeSource("remove.md", SourceType.Document, "remove"));

        _manager.RemoveSource("remove");

        Assert.Single(_manager.GetSources());
        Assert.NotNull(_manager.GetSource("keep"));
    }

    [Fact]
    public void GetSource_ById_ReturnsCorrectSource()
    {
        _manager.AddSource(MakeSource("a.md", SourceType.Document, "id-a"));
        _manager.AddSource(MakeSource("b.md", SourceType.Document, "id-b"));

        var result = _manager.GetSource("id-a");

        Assert.NotNull(result);
        Assert.Equal("id-a", result.Id);
        Assert.Equal("a.md", result.Name);
    }

    [Fact]
    public void GetSource_NonExisting_ReturnsNull()
    {
        Assert.Null(_manager.GetSource("nonexistent"));
    }

    [Fact]
    public void GetSourceByUri_FindsByUri()
    {
        _manager.AddSource(MakeSource("guide.md", SourceType.Document, "g1", uri: @"D:\docs\guide.md"));

        var result = _manager.GetSourceByUri(@"D:\docs\guide.md");

        Assert.NotNull(result);
        Assert.Equal("g1", result.Id);
    }

    [Fact]
    public void GetSourceByUri_CaseInsensitive()
    {
        _manager.AddSource(MakeSource("guide.md", SourceType.Document, "g1", uri: @"D:\Docs\Guide.MD"));

        var result = _manager.GetSourceByUri(@"d:\docs\guide.md");

        Assert.NotNull(result);
    }

    [Fact]
    public void GetSourceByUri_NonExisting_ReturnsNull()
    {
        Assert.Null(_manager.GetSourceByUri(@"D:\nothing.md"));
    }

    [Fact]
    public void UpdateSource_ModifiesExisting()
    {
        _manager.AddSource(MakeSource("old.md", SourceType.Document, "upd"));

        var updated = new KnowledgeSource
        {
            Id = "upd",
            Name = "updated.md",
            SourceType = SourceType.Document,
            Uri = "old.md",
            IsEnabled = false
        };
        _manager.UpdateSource(updated);

        var result = _manager.GetSource("upd");
        Assert.NotNull(result);
        Assert.Equal("updated.md", result.Name);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void UpdateSource_NonExisting_DoesNothing()
    {
        var source = MakeSource("ghost.md", SourceType.Document, "ghost");

        _manager.UpdateSource(source);

        Assert.Empty(_manager.GetSources());
    }

    [Fact]
    public void UpdateSource_OnlyAffectsTargetedSource()
    {
        _manager.AddSource(MakeSource("a.md", SourceType.Document, "a"));
        _manager.AddSource(MakeSource("b.md", SourceType.Document, "b"));

        var updated = new KnowledgeSource
        {
            Id = "a",
            Name = "a-updated",
            SourceType = SourceType.Document,
            Uri = "a.md"
        };
        _manager.UpdateSource(updated);

        var b = _manager.GetSource("b")!;
        Assert.Equal("b.md", b.Name);
    }

    [Fact]
    public void HasSourceByType_WhenExists_ReturnsTrue()
    {
        _manager.AddSource(MakeSource("code.cs", SourceType.Code));

        Assert.True(_manager.HasSourceByType(SourceType.Code));
    }

    [Fact]
    public void HasSourceByType_WhenNotExists_ReturnsFalse()
    {
        _manager.AddSource(MakeSource("doc.md", SourceType.Document));

        Assert.False(_manager.HasSourceByType(SourceType.Code));
    }

    [Fact]
    public void HasSourceByType_Empty_ReturnsFalse()
    {
        Assert.False(_manager.HasSourceByType(SourceType.Code));
    }

    [Fact]
    public void GetSources_WithWorkspaceId_FiltersCorrectly()
    {
        var src1 = new KnowledgeSource
        {
            Id = "ws1-src",
            Name = "ws1.md",
            SourceType = SourceType.Document,
            WorkspaceId = "ws1"
        };
        var src2 = new KnowledgeSource
        {
            Id = "ws2-src",
            Name = "ws2.md",
            SourceType = SourceType.Document,
            WorkspaceId = "ws2"
        };
        _manager.AddSource(src1);
        _manager.AddSource(src2);

        var ws1Sources = _manager.GetSources("ws1");

        Assert.Single(ws1Sources);
        Assert.Equal("ws1-src", ws1Sources[0].Id);
    }

    [Fact]
    public void GetSources_NullWorkspaceId_ReturnsAll()
    {
        _manager.AddSource(MakeSource("a.md", SourceType.Document, "a"));
        _manager.AddSource(MakeSource("b.md", SourceType.Document, "b"));

        var all = _manager.GetSources(null);

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetSources_ReturnsSnapshot()
    {
        _manager.AddSource(MakeSource("original.md", SourceType.Document, "snap"));

        var snapshot = _manager.GetSources();
        _manager.AddSource(MakeSource("new.md", SourceType.Document, "new"));

        Assert.Single(snapshot);
        Assert.Equal(2, _manager.GetSources().Count);
    }

    [Fact]
    public async Task MultiThread_AddRemove_NoException()
    {
        for (int i = 0; i < 5; i++)
        {
            var idx = i;
            await Task.Run(() => _manager.AddSource(MakeSource($"f{idx}.md", SourceType.Document, $"id{idx}")));
        }

        for (int i = 0; i < 5; i++)
        {
            var idx = i;
            await Task.Run(() => _manager.RemoveSource($"id{idx}"));
        }

        Assert.Empty(_manager.GetSources());
    }

    // ============ Helpers ============

    private static KnowledgeSource MakeSource(string name, SourceType type, string? id = null, string? uri = null)
    {
        return new KnowledgeSource
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            SourceType = type,
            Uri = uri ?? name
        };
    }
}
