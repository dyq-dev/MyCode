using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Indexing;

public class CodeIndexer : IIndexer
{
    private readonly IProjectScanner _scanner;
    private readonly IIndexComparer _comparer;
    private readonly IChunkManager _chunkManager;
    private readonly IEmbeddingService _embedding;
    private readonly IKnowledgeStore _store;

    public CodeIndexer(
        IProjectScanner scanner,
        IIndexComparer comparer,
        IChunkManager chunkManager,
        IEmbeddingService embedding,
        IKnowledgeStore store)
    {
        _scanner = scanner;
        _comparer = comparer;
        _chunkManager = chunkManager;
        _embedding = embedding;
        _store = store;
    }

    public async Task<IndexResult> IndexSourceAsync(string sourceUri, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(sourceUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"扫描失败: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.FilesScanned = scannedFiles.Count;

        var allChunks = new List<IKnowledgeChunk>();
        var codeChunkPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chunks = await _chunkManager
                    .ChunkAsync(file, sourceUri, cancellationToken)
                    .ToListAsync(cancellationToken);

                foreach (var chunk in chunks)
                    allChunks.Add(chunk);

                codeChunkPaths.Add(file.FilePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"分块失败 '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (allChunks.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(allChunks, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"保存失败（已分块 {allChunks.Count} 个）: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var indexedRecords = await _store.GetIndexedFilesAsync(sourceUri, cancellationToken);

            foreach (var record in indexedRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!codeChunkPaths.Contains(record.FilePath))
                {
                    try
                    {
                        await _store.DeleteChunksByFileAsync(record.FilePath, cancellationToken);
                        result.FilesDeleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        result.Errors.Add($"清理陈旧文件失败 '{record.FilePath}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"清理扫描失败（新数据已保存，陈旧记录残留）: {ex.Message}");
        }

        result.Success = result.FilesFailed == 0;
        result.ChunksCreated = allChunks.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }

    public async Task<IndexResult> IncrementalIndexAsync(string sourceUri, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(sourceUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"扫描失败: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.FilesScanned = scannedFiles.Count;

        IList<IndexFileRecord> indexedRecords;
        try
        {
            indexedRecords = await _store.GetIndexedFilesAsync(sourceUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"获取索引记录失败: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

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
                result.Errors.Add($"删除修改文件旧数据失败 '{file.FilePath}': {ex.Message}");
            }
        }

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
                result.Errors.Add($"删除已移除文件数据失败 '{record.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var newChunks = new List<IKnowledgeChunk>();

        foreach (var file in changeSet.Added.Concat(changeSet.Modified))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (failedDeletePaths.Contains(file.FilePath))
                continue;

            try
            {
                var chunks = await _chunkManager
                    .ChunkAsync(file, sourceUri, cancellationToken)
                    .ToListAsync(cancellationToken);

                newChunks.AddRange(chunks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"分块失败 '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (newChunks.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(newChunks, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"保存失败（已分块 {newChunks.Count} 个）: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        result.Success = result.FilesFailed == 0;
        result.ChunksCreated = newChunks.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }
}
