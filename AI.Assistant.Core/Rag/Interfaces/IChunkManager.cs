using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IChunkManager
{
    IAsyncEnumerable<CodeChunk> ChunkAsync(CodeFile file, string projectPath, CancellationToken cancellationToken = default);
}
