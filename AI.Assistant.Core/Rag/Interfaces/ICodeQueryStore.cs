using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

[Obsolete("Use IQueryStore instead")]
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
