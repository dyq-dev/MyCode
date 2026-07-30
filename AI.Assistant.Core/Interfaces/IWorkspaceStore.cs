using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Interfaces;

public interface IWorkspaceStore
{
    Task<List<KnowledgeSource>> LoadAsync();
    Task SaveAsync(IEnumerable<KnowledgeSource> sources);
}
