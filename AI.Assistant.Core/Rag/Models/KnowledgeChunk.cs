using AI.Assistant.Core.Rag.Interfaces;

namespace AI.Assistant.Core.Rag.Models;

public class KnowledgeChunk : IKnowledgeChunk
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public SourceType SourceType { get; set; } = SourceType.Unknown;
    public string SourceUri { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public DateTime IndexedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    IReadOnlyDictionary<string, string> IKnowledgeChunk.Metadata => Metadata;
}
