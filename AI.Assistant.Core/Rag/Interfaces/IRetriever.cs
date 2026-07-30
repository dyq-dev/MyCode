using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IRetriever
{
    Task<IList<RetrievedKnowledgeChunk>> VectorSearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
