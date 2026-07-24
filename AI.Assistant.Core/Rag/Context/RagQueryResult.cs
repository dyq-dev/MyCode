namespace AI.Assistant.Core.Rag.Context;

/// <summary>RAG 查询结果</summary>
public class RagQueryResult
{
    /// <summary>是否有可用上下文</summary>
    public bool HasContext { get; init; }

    /// <summary>拼接后的代码上下文纯文本</summary>
    public string? ContextText { get; init; }

    /// <summary>实际使用的代码块数</summary>
    public int ChunksUsed { get; init; }

    /// <summary>ContextText 估算 token 数</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>错误消息（RAG 失败时设置，不阻断聊天）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>完整检索链路调试信息（仅 EnableDebugInfo=true 时填充）</summary>
    public RagDebugInfo? DebugInfo { get; init; }
}
