using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IProjectScanner
{
    Task<IList<CodeFile>> ScanProjectAsync(string projectPath, CancellationToken cancellationToken = default);
}
