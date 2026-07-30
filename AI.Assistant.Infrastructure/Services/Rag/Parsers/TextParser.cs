using System.Runtime.CompilerServices;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Parsers;

public class TextParser : IDocumentParser
{
    public SourceType SourceType => SourceType.Text;
    public string[] SupportedExtensions => [".txt"];

    public async IAsyncEnumerable<KnowledgeChunk> ParseAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        yield return new KnowledgeChunk
        {
            Id = Guid.NewGuid().ToString("N"),
            Content = content.Trim(),
            SourceType = SourceType.Text,
            SourceUri = filePath,
            ProjectPath = filePath,
            IndexedAt = DateTime.UtcNow
        };
    }
}
