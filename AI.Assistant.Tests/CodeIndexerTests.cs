using System.Collections.Concurrent;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Indexing;

namespace AI.Assistant.Tests;

public class CodeIndexerTests
{
    private const string ProjectPath = @"D:\test";
    private readonly FakeScanner _scanner = new();
    private readonly FakeIndexComparer _comparer = new();
    private readonly FakeChunkManager _chunkManager = new();
    private readonly FakeCodeIndexStore _store = new();
    private readonly CodeIndexer _indexer;

    public CodeIndexerTests()
    {
        _indexer = new CodeIndexer(_scanner, _comparer, _chunkManager, _store);
    }

    private static CodeFile File(string path, string hash = "h")
        => new() { FilePath = path, FileHash = hash, Content = "", Language = "csharp", Encoding = "utf-8" };

    private static CodeChunk Chunk(string filePath)
        => new() { Id = Guid.NewGuid().ToString("N"), FilePath = filePath, Content = "", Language = "csharp", ChunkType = CodeChunkType.File, StartLine = 1, EndLine = 1, ProjectPath = ProjectPath, IndexedAt = DateTime.UtcNow };

    private static IndexFileRecord Record(string filePath, string hash = "h")
        => new() { FilePath = filePath, FileHash = hash, IndexedAt = DateTime.UtcNow };

    // ============ IndexProjectAsync — 基本流程 ============

    [Fact]
    public async Task IndexProjectAsync_ScansChunksAndSaves()
    {
        _scanner.Files = [File("a.cs"), File("b.cs")];

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesScanned);
        Assert.Equal(2, result.ChunksCreated);
        Assert.Equal(2, _store.SavedChunks.Count);
    }

    [Fact]
    public async Task IndexProjectAsync_CleanupStaleFiles()
    {
        _scanner.Files = [File("a.cs")];
        _store.IndexedFiles.Add(Record("a.cs"));
        _store.IndexedFiles.Add(Record("old.cs"));

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Contains("old.cs", _store.DeletedFiles);
    }

    [Fact]
    public async Task IndexProjectAsync_EmptyProject()
    {
        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesScanned);
        Assert.Equal(0, result.ChunksCreated);
    }

    [Fact]
    public async Task IndexProjectAsync_ChunkFailure_SkipsFile()
    {
        _scanner.Files = [File("good.cs"), File("bad.cs")];
        _chunkManager.FailFor = ["bad.cs"];

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.Equal(1, result.FilesFailed);
        Assert.Single(result.Errors);
        Assert.Equal(1, result.ChunksCreated);
        Assert.Single(_store.SavedChunks);
        Assert.Equal("good.cs", _store.SavedChunks[0].FilePath);
    }

    [Fact]
    public async Task IndexProjectAsync_CleanupGetRecordsFailure_StillSucceeds()
    {
        _scanner.Files = [File("a.cs")];
        _store.FailGetIndexedFiles = true;

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.ChunksCreated);
        Assert.Single(result.Errors); // cleanup warning
    }

    // ============ IndexProjectAsync — 错误 ============

    [Fact]
    public async Task IndexProjectAsync_ScanFailure_ReturnsFailure()
    {
        _scanner.ThrowOnScan = true;

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("Scan failed", result.Errors[0]);
    }

    [Fact]
    public async Task IndexProjectAsync_SaveFailure_ReturnsFailure()
    {
        _scanner.Files = [File("a.cs")];
        _store.FailSave = true;

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.False(result.Success);
        Assert.Contains("Save failed", result.Errors[0]);
    }

    // ============ IncrementalIndexAsync — 基本流程 ============

    [Fact]
    public async Task IncrementalIndexAsync_NoChanges_ReturnsEarly()
    {
        _scanner.Files = [File("a.cs", "h1")];
        _store.IndexedFiles = [Record("a.cs", "h1")];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(0, result.ChunksCreated);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Empty(_store.SavedChunks);
    }

    [Fact]
    public async Task IncrementalIndexAsync_NewFiles_ChunksAndSaves()
    {
        _scanner.Files = [File("a.cs", "h1"), File("b.cs", "h2")];
        _comparer.Added = [File("a.cs", "h1"), File("b.cs", "h2")];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesAdded);
        Assert.Equal(2, result.ChunksCreated);
        Assert.Equal(2, _store.SavedChunks.Count);
    }

    [Fact]
    public async Task IncrementalIndexAsync_ModifiedFiles_DeletesOldAndSavesNew()
    {
        _scanner.Files = [File("a.cs", "h2")];
        _store.IndexedFiles = [Record("a.cs", "h1")];
        _comparer.Modified = [File("a.cs", "h2")];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesModified);
        Assert.Contains("a.cs", _store.DeletedFiles);
        Assert.Single(_store.SavedChunks);
    }

    [Fact]
    public async Task IncrementalIndexAsync_DeletedFiles_RemovesThem()
    {
        _scanner.Files = [File("keep.cs", "h1")];
        _store.IndexedFiles = [Record("keep.cs", "h1"), Record("gone.cs", "h2")];
        _comparer.Deleted = [Record("gone.cs", "h2")];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Contains("gone.cs", _store.DeletedFiles);
    }

    [Fact]
    public async Task IncrementalIndexAsync_MixedChanges()
    {
        _scanner.Files = [File("added.cs", "h1"), File("modified.cs", "h2"), File("unchanged.cs", "h3")];
        _store.IndexedFiles = [Record("modified.cs", "h_old"), Record("unchanged.cs", "h3"), Record("deleted.cs", "h4")];
        _comparer.Added = [File("added.cs", "h1")];
        _comparer.Modified = [File("modified.cs", "h2")];
        _comparer.Deleted = [Record("deleted.cs", "h4")];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesAdded);
        Assert.Equal(1, result.FilesModified);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(2, result.ChunksCreated);
        Assert.Equal(2, _store.SavedChunks.Count);
        Assert.Contains("deleted.cs", _store.DeletedFiles);
        Assert.Contains("modified.cs", _store.DeletedFiles);
    }

    // ============ IncrementalIndexAsync — 错误 ============

    [Fact]
    public async Task IncrementalIndexAsync_ScanFailure_ReturnsFailure()
    {
        _scanner.ThrowOnScan = true;

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.False(result.Success);
        Assert.Contains("Scan failed", result.Errors[0]);
    }

    [Fact]
    public async Task IncrementalIndexAsync_GetIndexedFilesFailure_ReturnsFailure()
    {
        _scanner.Files = [File("a.cs")];
        _store.FailGetIndexedFiles = true;

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.False(result.Success);
        Assert.Contains("GetIndexedFiles failed", result.Errors[0]);
    }

    [Fact]
    public async Task IncrementalIndexAsync_ChunkFailure_SkipsFile()
    {
        _scanner.Files = [File("good.cs", "h1"), File("bad.cs", "h2")];
        _comparer.Added = [File("good.cs", "h1"), File("bad.cs", "h2")];
        _chunkManager.FailFor = ["bad.cs"];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.Equal(1, result.FilesFailed);
        Assert.Single(_store.SavedChunks);
        Assert.Equal("good.cs", _store.SavedChunks[0].FilePath);
    }

    [Fact]
    public async Task IncrementalIndexAsync_DeleteModifiedFailure_SkipsReChunk()
    {
        _scanner.Files = [File("a.cs", "h2")];
        _store.IndexedFiles = [Record("a.cs", "h1")];
        _comparer.Modified = [File("a.cs", "h2")];
        _store.FailDeleteFor = ["a.cs"];

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.Equal(1, result.FilesFailed);
        Assert.Empty(_store.SavedChunks); // skip chunk because delete failed
    }

    [Fact]
    public async Task IncrementalIndexAsync_SaveFailure_ReturnsFailure()
    {
        _scanner.Files = [File("a.cs", "h1")];
        _comparer.Added = [File("a.cs", "h1")];
        _store.FailSave = true;

        var result = await _indexer.IncrementalIndexAsync(ProjectPath);

        Assert.False(result.Success);
        Assert.Contains("Save failed", result.Errors[0]);
    }

    // ============ Cancellation ============

    [Fact]
    public async Task IndexProjectAsync_RespectsCancellation()
    {
        _scanner.Files = [File("a.cs")];
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _indexer.IndexProjectAsync(ProjectPath, cts.Token));
    }

    [Fact]
    public async Task IncrementalIndexAsync_RespectsCancellation()
    {
        _scanner.Files = [File("a.cs")];
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _indexer.IncrementalIndexAsync(ProjectPath, cts.Token));
    }

    // ============ Duration ============

    [Fact]
    public async Task IndexProjectAsync_SetsDuration()
    {
        _scanner.Files = [File("a.cs")];

        var result = await _indexer.IndexProjectAsync(ProjectPath);

        Assert.True(result.Duration.TotalMilliseconds > 0);
    }

    // ============ Fake Implementations ============

    private sealed class FakeScanner : IProjectScanner
    {
        public bool ThrowOnScan { get; set; }
        public IList<CodeFile> Files { get; set; } = [];

        public Task<IList<CodeFile>> ScanProjectAsync(string projectPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowOnScan)
                throw new InvalidOperationException("Scan failed");
            return Task.FromResult(Files);
        }
    }

    private sealed class FakeIndexComparer : IIndexComparer
    {
        public IList<CodeFile> Added { get; set; } = [];
        public IList<CodeFile> Modified { get; set; } = [];
        public IList<IndexFileRecord> Deleted { get; set; } = [];

        public FileChangeSet Compare(IList<CodeFile> scannedFiles, IList<IndexFileRecord> indexedRecords)
        {
            var added = Added.Count > 0 ? Added : scannedFiles
                .Where(s => !indexedRecords.Any(r =>
                    r.FilePath.Equals(s.FilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var modified = Modified.Count > 0 ? Modified : scannedFiles
                .Where(s => indexedRecords.Any(r =>
                    r.FilePath.Equals(s.FilePath, StringComparison.OrdinalIgnoreCase) &&
                    !r.FileHash.Equals(s.FileHash, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var deleted = Deleted.Count > 0 ? Deleted : indexedRecords
                .Where(r => !scannedFiles.Any(s =>
                    s.FilePath.Equals(r.FilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Remove modified from added (a file shouldn't be both)
            added = added.Where(a => !modified.Any(m =>
                m.FilePath.Equals(a.FilePath, StringComparison.OrdinalIgnoreCase))).ToList();

            return new FileChangeSet { Added = added, Modified = modified, Deleted = deleted };
        }
    }

    private sealed class FakeChunkManager : IChunkManager
    {
        public HashSet<string> FailFor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public async IAsyncEnumerable<CodeChunk> ChunkAsync(CodeFile file, string projectPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            if (FailFor.Contains(file.FilePath))
                throw new InvalidOperationException($"Chunk failed for {file.FilePath}");
            yield return new CodeChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = file.FilePath,
                Content = file.Content,
                Language = file.Language,
                ChunkType = CodeChunkType.File,
                StartLine = 1,
                EndLine = 1,
                ProjectPath = projectPath,
                IndexedAt = DateTime.UtcNow
            };
        }
    }

    private sealed class FakeCodeIndexStore : ICodeIndexStore
    {
        public List<IndexFileRecord> IndexedFiles { get; set; } = [];
        public List<CodeChunk> SavedChunks { get; } = [];
        public List<string> DeletedFiles { get; } = [];
        public bool FailGetIndexedFiles { get; set; }
        public bool FailSave { get; set; }
        public HashSet<string> FailDeleteFor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailSave)
                throw new InvalidOperationException("Save failed");
            SavedChunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task DeleteChunksByFileAsync(string filePath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailDeleteFor.Contains(filePath))
                throw new InvalidOperationException($"Delete failed for {filePath}");
            DeletedFiles.Add(filePath);
            return Task.CompletedTask;
        }

        public Task DeleteProjectAsync(string projectPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IList<IndexFileRecord>> GetIndexedFilesAsync(string projectPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailGetIndexedFiles)
                throw new InvalidOperationException("GetIndexedFiles failed");
            return Task.FromResult<IList<IndexFileRecord>>(IndexedFiles);
        }
    }
}
