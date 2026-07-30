using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

[Obsolete("Use IRetriever instead")]
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
