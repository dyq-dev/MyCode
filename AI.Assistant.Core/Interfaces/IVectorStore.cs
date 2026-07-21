namespace AI.Assistant.Core.Interfaces;

/// <summary>
/// 向量存储接口 - 存储和检索向量数据
/// 用于 RAG 场景：存储文档向量，根据问题向量进行相似度搜索
/// 实现类：QdrantVectorStore（调用 Qdrant 向量数据库）
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// 插入或更新向量数据
    /// </summary>
    /// <param name="collection">集合名称（类似数据库表）</param>
    /// <param name="id">向量唯一标识</param>
    /// <param name="vector">向量数据</param>
    /// <param name="metadata">元数据（如原文、来源等）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UpsertAsync(string collection, string id, float[] vector, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据向量进行相似度搜索
    /// </summary>
    /// <param name="collection">集合名称</param>
    /// <param name="queryVector">查询向量</param>
    /// <param name="topK">返回最相似的前 K 条结果</param>
    /// <param name="filter">payload 等值过滤条件（如 sessionId），为 null 不过滤</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>按相似度排序的搜索结果</returns>
    Task<IList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK = 5, Dictionary<string, string>? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定向量
    /// </summary>
    /// <param name="collection">集合名称</param>
    /// <param name="id">要删除的向量标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 向量搜索结果
/// </summary>
public class VectorSearchResult
{
    /// <summary>向量标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>相似度分数（越高越相似）</summary>
    public float Score { get; set; }

    /// <summary>关联的元数据</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
