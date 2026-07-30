using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IDocumentParser
{
    SourceType SourceType { get; }
    string[] SupportedExtensions { get; }
    IAsyncEnumerable<KnowledgeChunk> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
