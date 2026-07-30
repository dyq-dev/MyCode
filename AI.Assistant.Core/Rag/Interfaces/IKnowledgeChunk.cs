using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IKnowledgeChunk
{
    string Id { get; }
    string SourceId { get; }
    string Content { get; }
    SourceType SourceType { get; }
    string SourceUri { get; }
    string ProjectPath { get; }
    DateTime IndexedAt { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}
