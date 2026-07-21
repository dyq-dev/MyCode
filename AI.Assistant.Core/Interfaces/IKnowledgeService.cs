using AI.Assistant.Core.Models;

namespace AI.Assistant.Core.Interfaces;

/// <summary>
/// 知识库服务（预留，暂未实现 RAG）
/// </summary>
public interface IKnowledgeService
{
    Task<KnowledgeContext> RetrieveAsync(string query, CancellationToken cancellationToken = default);
}
