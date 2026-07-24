using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

/// <summary>
/// 代码查询存储器——封装向量检索细节，供 ICodeRetriever 调用。
/// 职责：接收查询向量，返回相关代码分块。
/// 底层向量库实现细节（集合名、过滤条件、字段映射）由此接口的实现封装。
/// </summary>
public interface ICodeQueryStore
{
    /// <summary>根据查询向量搜索最相似的代码分块</summary>
    /// <param name="queryVector">查询向量</param>
    /// <param name="topK">返回结果数量上限</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IList<RetrievedCodeChunk>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
