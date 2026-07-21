using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface ICodeIndexStore
{
    Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteChunksByFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task DeleteProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IList<IndexFileRecord>> GetIndexedFilesAsync(string projectPath, CancellationToken cancellationToken = default);
}
