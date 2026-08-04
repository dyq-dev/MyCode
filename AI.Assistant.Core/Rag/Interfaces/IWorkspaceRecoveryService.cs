using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

/// <summary>
/// 从向量数据库恢复工作区注册表。
/// 当 workspace.json 丢失但向量数据库仍有 chunk 数据时，
/// 自动重建 KnowledgeSource 列表。
/// </summary>
public interface IWorkspaceRecoveryService
{
    /// <summary>
    /// 从向量数据库恢复所有知识源。
    /// </summary>
    Task<IList<KnowledgeSource>> RecoverSourcesAsync(CancellationToken cancellationToken = default);
}
