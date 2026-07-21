using System.Runtime.CompilerServices;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Chunking.Strategies;

public class WholeFileChunkStrategy : IChunkStrategy
{
    public string[] SupportedExtensions => [];

    public async IAsyncEnumerable<CodeChunk> ChunkAsync(
        CodeFile file,
        string projectPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();

        var endLine = file.Content.Length == 0
            ? 0
            : file.Content[^1] == '\n'
                ? file.Content.Count(c => c == '\n')
                : file.Content.Count(c => c == '\n') + 1;

        yield return new CodeChunk
        {
            Id = Guid.NewGuid().ToString("N"),
            VectorId = string.Empty,
            FilePath = file.FilePath,
            Content = file.Content,
            Language = file.Language,
            ChunkType = CodeChunkType.File,
            StartLine = 1,
            EndLine = endLine,
            Namespace = null,
            ClassName = null,
            MethodName = null,
            SymbolName = null,
            ProjectPath = projectPath,
            IndexedAt = DateTime.UtcNow
        };
    }
}
