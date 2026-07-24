using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

/// <summary>代码索引存储器，负责持久化代码分块及其向量</summary>
public interface ICodeIndexStore
{
    /// <summary>保存一批原始代码分块（内部生成零向量，兼容旧调用方）</summary>
    Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>保存一批已嵌入的代码分块（含向量），推荐使用此重载</summary>
    Task SaveChunksAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>删除指定文件的所有分块及索引记录</summary>
    Task DeleteChunksByFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>删除整个项目的所有分块及索引记录</summary>
    Task DeleteProjectAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>获取项目下已索引的文件记录列表</summary>
    Task<IList<IndexFileRecord>> GetIndexedFilesAsync(string projectPath, CancellationToken cancellationToken = default);
}
