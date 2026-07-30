using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IIndexer
{
    Task<IndexResult> IndexSourceAsync(
        string sourceUri,
        CancellationToken cancellationToken = default);

    Task<IndexResult> IncrementalIndexAsync(
        string sourceUri,
        CancellationToken cancellationToken = default);
}
