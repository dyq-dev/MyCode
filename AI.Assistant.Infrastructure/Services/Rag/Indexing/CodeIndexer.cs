using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Indexing;

/// <summary>
/// 代码索引器：将扫描、分块、嵌入、存储、清理串联为完整索引流程。
/// IndexProjectAsync 执行全量重建（先全部写入，再清理脏数据）；
/// IncrementalIndexAsync 执行增量更新（对比差异，只处理变更文件）。
/// </summary>
public class CodeIndexer : ICodeIndexer
{
    private readonly IProjectScanner _scanner;
    private readonly IIndexComparer _comparer;
    private readonly IChunkManager _chunkManager;
    private readonly IEmbeddingService _embedding;
    private readonly ICodeIndexStore _store;

    public CodeIndexer(
        IProjectScanner scanner,
        IIndexComparer comparer,
        IChunkManager chunkManager,
        IEmbeddingService embedding,
        ICodeIndexStore store)
    {
        _scanner = scanner;
        _comparer = comparer;
        _chunkManager = chunkManager;
        _embedding = embedding;
        _store = store;
    }

    public async Task<IndexResult> IndexProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        // [1] 扫描项目文件
        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(projectPath, cancellationToken);
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

        // [2] 逐个文件分块 → 批量嵌入 → 收集 EmbeddedChunk
        var allEmbedded = new List<EmbeddedChunk>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chunks = await _chunkManager
                    .ChunkAsync(file, projectPath, cancellationToken)
                    .ToListAsync(cancellationToken);

                var vectors = await _embedding.EmbedBatchAsync(
                    chunks.Select(c => c.Content), cancellationToken);

                foreach (var (chunk, vector) in chunks.Zip(vectors))
                {
                    allEmbedded.Add(new EmbeddedChunk { Chunk = chunk, Vector = vector });
                }

                scannedPaths.Add(file.FilePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"分块/嵌入失败 '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [3] 一次性保存所有带向量的分块
        if (allEmbedded.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(allEmbedded, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"保存失败（已分块 {allEmbedded.Count} 个）: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [4] 清理已被删除的陈旧文件（尽力而为，不阻断流程）
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
        result.ChunksCreated = allEmbedded.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }

    /// <summary>增量索引：扫描 → 对比 → 删除旧数据 → 分块+嵌入 → 存储</summary>
    public async Task<IndexResult> IncrementalIndexAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new IndexResult();

        // [1] 扫描项目文件
        IList<CodeFile> scannedFiles;
        try
        {
            scannedFiles = await _scanner.ScanProjectAsync(projectPath, cancellationToken);
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

        // [2] 获取已有索引记录
        IList<IndexFileRecord> indexedRecords;
        try
        {
            indexedRecords = await _store.GetIndexedFilesAsync(projectPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Errors.Add($"获取索引记录失败: {ex.Message}");
            result.Duration = DateTime.UtcNow - startedAt;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [3] 对比扫描结果与索引记录，得到变更集
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

        // [4] 先删除被修改文件的旧分块（重嵌入前清理）
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

        // [5] 删除已移除文件的索引数据
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

        // [6] 对新文件和已修改文件进行分块 + 嵌入（跳过删除失败的文件）
        var allEmbedded = new List<EmbeddedChunk>();

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

                var vectors = await _embedding.EmbedBatchAsync(
                    chunks.Select(c => c.Content), cancellationToken);

                foreach (var (chunk, vector) in chunks.Zip(vectors))
                {
                    allEmbedded.Add(new EmbeddedChunk { Chunk = chunk, Vector = vector });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.FilesFailed++;
                result.Errors.Add($"分块/嵌入失败 '{file.FilePath}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [7] 批量保存新的带向量分块
        if (allEmbedded.Count > 0)
        {
            try
            {
                await _store.SaveChunksAsync(allEmbedded, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add($"保存失败（已分块 {allEmbedded.Count} 个）: {ex.Message}");
                result.Duration = DateTime.UtcNow - startedAt;
                return result;
            }
        }

        result.Success = result.FilesFailed == 0;
        result.ChunksCreated = allEmbedded.Count;
        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }
}
