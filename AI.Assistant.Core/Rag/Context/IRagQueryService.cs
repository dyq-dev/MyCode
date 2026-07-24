namespace AI.Assistant.Core.Rag.Context;

/// <summary>
/// RAG 查询服务——根据用户消息判断是否需要 RAG 并执行检索。
/// 职责：判断关键词 → ICodeRetriever → IRagContextBuilder → RagQueryResult。
/// 不负责 Prompt 拼接、聊天、Memory。
/// </summary>
public interface IRagQueryService
{
    /// <summary>对用户消息执行 RAG 查询（关键词匹配 + 向量检索 + 上下文构建）</summary>
    /// <param name="userMessage">用户输入文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<RagQueryResult> QueryAsync(
        string userMessage,
        CancellationToken cancellationToken = default);
}
