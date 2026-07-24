using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Context;

/// <summary>RAG 上下文，包含拼接后的文本和来源信息</summary>
public class RagContext
{
    /// <summary>已拼接好的纯文本，可直接注入 Prompt</summary>
    public string ContextText { get; init; } = "";

    /// <summary>实际使用的检索结果来源（被 ContextText 包含的）</summary>
    public IReadOnlyList<RetrievedCodeChunk> Sources { get; init; } = [];

    /// <summary>估算的 token 数（ContextText.Length / 3，代码中英混合经验值）</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>检索到的代码块总数</summary>
    public int TotalRetrieved { get; init; }

    /// <summary>实际拼接使用的代码块数</summary>
    public int TotalUsed { get; init; }
}
