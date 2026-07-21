using System.Runtime.CompilerServices;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Chunking;

public class ChunkManager : IChunkManager
{
    private readonly IEnumerable<IChunkStrategy> _strategies;
    private readonly IChunkStrategy _fallback;

    public ChunkManager(IEnumerable<IChunkStrategy> strategies)
    {
        _strategies = strategies.Where(s => s.SupportedExtensions.Length > 0);
        _fallback = strategies.FirstOrDefault(s => s.SupportedExtensions.Length == 0)
            ?? throw new InvalidOperationException("No fallback strategy (SupportedExtensions == []) registered.");
    }

    public async IAsyncEnumerable<CodeChunk> ChunkAsync(
        CodeFile file,
        string projectPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(file.FilePath);
        var strategy = _strategies.FirstOrDefault(s =>
            s.SupportedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            ?? _fallback;

        await foreach (var chunk in strategy.ChunkAsync(file, projectPath, cancellationToken))
        {
            yield return chunk;
        }
    }
}
