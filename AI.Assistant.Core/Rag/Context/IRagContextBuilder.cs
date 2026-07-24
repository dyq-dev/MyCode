using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Context;

/// <summary>
/// RAG 上下文构建器——将检索到的代码分块转换为 Chat 可用的上下文。
/// 职责：RetrievedCodeChunk[] → RagContext（拼接文本 + 来源 + 估算 token）。
/// 不负责 Prompt 拼接、聊天、Memory、文件读取。
/// </summary>
public interface IRagContextBuilder
{
    /// <summary>构建 RAG 上下文</summary>
    /// <param name="chunks">检索到的代码分块（已按 Score 降序）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<RagContext> BuildAsync(
        IList<RetrievedCodeChunk> chunks,
        CancellationToken cancellationToken = default);
}
