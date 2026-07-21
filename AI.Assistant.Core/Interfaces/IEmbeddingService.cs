namespace AI.Assistant.Core.Interfaces;

/// <summary>
/// 向量化服务接口 - 将文本转换为向量（Embedding）
/// 用于 RAG 场景：将文档/问题转为向量，便于语义搜索
/// 实现类：OllamaEmbeddingService（调用 Ollama 的 embedding 模型）
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 将单条文本转换为向量
    /// </summary>
    /// <param name="text">要转换的文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>浮点数数组，表示文本的向量表示</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量将文本转换为向量（更高效）
    /// </summary>
    /// <param name="texts">要转换的文本集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>向量数组的列表</returns>
    Task<IList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
}
