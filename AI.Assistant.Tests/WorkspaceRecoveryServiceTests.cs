using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag;
using Qdrant.Client.Grpc;
using System.Globalization;

namespace AI.Assistant.Tests;

public class WorkspaceRecoveryServiceTests
{
    private readonly FakeQdrantIndexStorage _storage = new();
    private readonly RagOptions _options = new() { QdrantCollectionName = "code_rag" };
    private readonly WorkspaceRecoveryService _service;

    public WorkspaceRecoveryServiceTests()
    {
        _service = new WorkspaceRecoveryService(_storage, _options);
    }

    [Fact]
    public async Task RecoverSourcesAsync_EmptyCollection_ReturnsEmpty()
    {
        var result = await _service.RecoverSourcesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecoverSourcesAsync_WithChunks_GroupsBySourceId()
    {
        var sourceId = "abc123";
        _storage.AddPoint("code_rag", "chunk1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String(sourceId),
            ["source_uri"] = ValueMaker.String(@"C:\docs\test.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:00:00Z")
        });
        _storage.AddPoint("code_rag", "chunk2", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String(sourceId),
            ["source_uri"] = ValueMaker.String(@"C:\docs\test.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:05:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Single(result);
        Assert.Equal(sourceId, result[0].Id);
        Assert.Equal("test.pdf", result[0].Name);
        Assert.Equal(SourceType.Pdf, result[0].SourceType);
        Assert.Equal(@"C:\docs\test.pdf", result[0].Uri);
        Assert.Equal("default", result[0].WorkspaceId);
        Assert.True(result[0].IsEnabled);
        Assert.False(result[0].AutoSync);
    }

    [Fact]
    public async Task RecoverSourcesAsync_MultipleSources_ReturnsAll()
    {
        _storage.AddPoint("code_rag", "c1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src1"),
            ["source_uri"] = ValueMaker.String(@"C:\docs\file1.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:00:00Z")
        });
        _storage.AddPoint("code_rag", "c2", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src2"),
            ["source_uri"] = ValueMaker.String(@"C:\docs\file2.md"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-16T10:00:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Name == "file1.pdf" && s.SourceType == SourceType.Pdf);
        Assert.Contains(result, s => s.Name == "file2.md" && s.SourceType == SourceType.Markdown);
    }

    [Fact]
    public async Task RecoverSourcesAsync_CollectionNotExists_ReturnsEmpty()
    {
        _options.QdrantCollectionName = "nonexistent";
        var result = await _service.RecoverSourcesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecoverSourcesAsync_MissingSourceId_SkipsChunk()
    {
        _storage.AddPoint("code_rag", "chunk1", new Dictionary<string, Value>
        {
            ["source_uri"] = ValueMaker.String(@"C:\docs\test.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:00:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecoverSourcesAsync_UsesCorrectCollection()
    {
        _options.QdrantCollectionName = "my_custom_collection";
        _storage.CreateCollectionAsync("my_custom_collection", new VectorParams { Size = 128, Distance = Distance.Cosine });
        _storage.AddPoint("my_custom_collection", "c1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src1"),
            ["source_uri"] = ValueMaker.String(@"C:\file.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:00:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task RecoverSourcesAsync_FileExtensionMapping_Pdf()
    {
        await TestExtensionMapping(@".pdf", SourceType.Pdf);
    }

    [Fact]
    public async Task RecoverSourcesAsync_FileExtensionMapping_Markdown()
    {
        await TestExtensionMapping(@".md", SourceType.Markdown);
    }

    [Fact]
    public async Task RecoverSourcesAsync_FileExtensionMapping_Text()
    {
        await TestExtensionMapping(@".txt", SourceType.Text);
    }

    [Fact]
    public async Task RecoverSourcesAsync_FileExtensionMapping_Code()
    {
        await TestExtensionMapping(@".cs", SourceType.Code);
    }

    [Fact]
    public async Task RecoverSourcesAsync_FileExtensionMapping_Document()
    {
        await TestExtensionMapping(@".docx", SourceType.Document);
    }

    private async Task TestExtensionMapping(string ext, SourceType expectedType)
    {
        _storage.AddPoint("code_rag", "c1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src1"),
            ["source_uri"] = ValueMaker.String($@"C:\file{ext}"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:00:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].SourceType);
    }

    [Fact]
    public async Task RecoverSourcesAsync_ParsesCreatedAtFromIndexedAt()
    {
        _storage.AddPoint("code_rag", "c1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src1"),
            ["source_uri"] = ValueMaker.String(@"C:\file.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("2024-01-15T10:30:00Z")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), DateTimeKind.Utc), result[0].CreatedAt);
    }

    [Fact]
    public async Task RecoverSourcesAsync_InvalidIndexedAt_UsesNow()
    {
        _storage.AddPoint("code_rag", "c1", new Dictionary<string, Value>
        {
            ["source_id"] = ValueMaker.String("src1"),
            ["source_uri"] = ValueMaker.String(@"C:\file.pdf"),
            ["source_type"] = ValueMaker.String("unknown"),
            ["indexed_at"] = ValueMaker.String("invalid-date")
        });

        var result = await _service.RecoverSourcesAsync();

        Assert.True(result[0].CreatedAt <= DateTime.UtcNow);
    }
}

internal static class ValueMaker
{
    public static Value String(string s) => new() { StringValue = s };
}
