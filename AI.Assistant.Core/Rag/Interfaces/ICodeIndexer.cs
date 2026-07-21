using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface ICodeIndexer
{
    Task<IndexResult> IndexProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IndexResult> IncrementalIndexAsync(string projectPath, CancellationToken cancellationToken = default);
}
