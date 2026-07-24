using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

/// <summary>
/// 代码检索器——Code RAG 查询入口。
/// 职责：自然语言查询 → Embedding → 向量搜索 → TopK 代码分块。
/// 不负责 Prompt 拼接、聊天、Memory、文件读取。
/// </summary>
public interface ICodeRetriever
{
    /// <summary>向量检索：输入自然语言查询，返回相关代码块</summary>
    /// <param name="query">用户查询文本</param>
    /// <param name="topK">返回结果数量，受 RagOptions.MaxTopK 限制</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IList<RetrievedCodeChunk>> VectorSearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
