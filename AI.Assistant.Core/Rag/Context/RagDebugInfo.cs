namespace AI.Assistant.Core.Rag.Context;

/// <summary>RAG 检索全链路调试信息</summary>
public class RagDebugInfo
{
    /// <summary>用户原始输入</summary>
    public string UserQuery { get; init; } = "";

    /// <summary>是否触发 RAG（关键词匹配成功）</summary>
    public bool Triggered { get; init; }

    /// <summary>命中的首个关键词</summary>
    public string? MatchedKeyword { get; init; }

    /// <summary>最低分数阈值</summary>
    public double MinimumScoreThreshold { get; init; }

    /// <summary>Retriever 耗时（Embedding + Qdrant 搜索）</summary>
    public TimeSpan RetrievalElapsed { get; init; }

    /// <summary>ContextBuilder 耗时（拼接 + 截断）</summary>
    public TimeSpan ContextBuildElapsed { get; init; }

    /// <summary>Retriever 返回的原始 Chunk 数（阈值过滤前）</summary>
    public int RawChunksReturned { get; init; }

    /// <summary>阈值过滤后的 Chunk 数</summary>
    public int ChunksAfterFilter { get; init; }

    /// <summary>ContextBuilder 最终使用数</summary>
    public int ChunksUsedByBuilder { get; init; }

    /// <summary>估算 Token 数</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>每个 Chunk 的详细信息</summary>
    public IReadOnlyList<RagChunkDebugInfo> Chunks { get; init; } = Array.Empty<RagChunkDebugInfo>();
}
