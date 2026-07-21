using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Indexing;

public class CodeIndexer : ICodeIndexer
{
    private readonly IProjectScanner _scanner;
    private readonly IIndexComparer _comparer;
    private readonly IChunkManager _chunkManager;
    private readonly ICodeIndexStore _store;

    public CodeIndexer(
        IProjectScanner scanner,
        IIndexComparer comparer,
        IChunkManager chunkManager,
        ICodeIndexStore store)
    {
        _scanner = scanner;
        _comparer = comparer;
        _chunkManager = chunkManager;
        _store = store;
    }

    public async Task<IndexResult> IndexProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        // [1] Scan
        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(projectPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"Scan failed: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.FilesScanned = scannedFiles.Count;

        // [2] Chunk all files
        var allChunks = new List<CodeChunk>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chunks = await _chunkManager
                    .ChunkAsync(file, projectPath, cancellationToken)
                    .ToListAsync(cancellationToken);

                allChunks.AddRange(chunks);
                scannedPaths.Add(file.FilePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"Chunk failed for '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [3] Save all chunks (upsert — new points alongside existing ones)
        if (allChunks.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(allChunks, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"Save failed after chunking {allChunks.Count} chunks: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [4] Cleanup stale files (best-effort)
        try
        {
            var indexedRecords = await _store.GetIndexedFilesAsync(projectPath, cancellationToken);

            foreach (var record in indexedRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!scannedPaths.Contains(record.FilePath))
                {
                    try
                    {
                        await _store.DeleteChunksByFileAsync(record.FilePath, cancellationToken);
                        result.FilesDeleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        result.Errors.Add($"Failed to delete stale file '{record.FilePath}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"Cleanup scan failed (new data is saved, stale records remain): {ex.Message}");
        }

        result.Success = result.FilesFailed == 0;
        result.ChunksCreated = allChunks.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }

    public async Task<IndexResult> IncrementalIndexAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        // [1] Scan
        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(projectPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"Scan failed: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.FilesScanned = scannedFiles.Count;

        // [2] Get indexed records
        IList<IndexFileRecord> indexedRecords;
        try
        {
            indexedRecords = await _store.GetIndexedFilesAsync(projectPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"GetIndexedFiles failed: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [3] Compare
        var changeSet = _comparer.Compare(scannedFiles, indexedRecords);
        result.FilesAdded = changeSet.Added.Count;
        result.FilesModified = changeSet.Modified.Count;

        if (!changeSet.HasChanges)
        {
            result.Success = true;
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [4] Delete old data for modified files (before re-chunking)
        var failedDeletePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in changeSet.Modified)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _store.DeleteChunksByFileAsync(file.FilePath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedDeletePaths.Add(file.FilePath);
                result.FilesFailed++;
                result.Errors.Add($"Delete failed for modified file '{file.FilePath}': {ex.Message}");
            }
        }

        // [5] Delete data for removed files
        foreach (var record in changeSet.Deleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _store.DeleteChunksByFileAsync(record.FilePath, cancellationToken);
                result.FilesDeleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"Delete failed for deleted file '{record.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [6] Chunk new + modified files (skip those that failed to delete)
        var allChunks = new List<CodeChunk>();

        foreach (var file in changeSet.Added.Concat(changeSet.Modified))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (failedDeletePaths.Contains(file.FilePath))
                continue;

            try
            {
                var chunks = await _chunkManager
                    .ChunkAsync(file, projectPath, cancellationToken)
                    .ToListAsync(cancellationToken);

                allChunks.AddRange(chunks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"Chunk failed for '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [7] Save all new chunks
        if (allChunks.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(allChunks, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"Save failed after chunking {allChunks.Count} chunks: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        result.Success = result.FilesFailed == 0;
        result.ChunksCreated = allChunks.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }
}
