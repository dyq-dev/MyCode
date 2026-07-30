using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IWorkspaceManager
{
    IReadOnlyList<KnowledgeSource> GetSources(string? workspaceId = null);
    KnowledgeSource? GetSource(string id);
    KnowledgeSource? GetSourceByUri(string uri);
    void AddSource(KnowledgeSource source);
    void RemoveSource(string id);
    void UpdateSource(KnowledgeSource source);
    bool HasSourceByType(SourceType type);
    Task LoadAsync();
}
