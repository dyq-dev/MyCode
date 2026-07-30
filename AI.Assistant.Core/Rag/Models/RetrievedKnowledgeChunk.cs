using AI.Assistant.Core.Rag.Interfaces;

namespace AI.Assistant.Core.Rag.Models;

public class RetrievedKnowledgeChunk
{
    public IKnowledgeChunk Chunk { get; init; } = null!;
    public float Score { get; init; }
}
